using System;
using System.Collections.Generic;
using System.Reflection;
using GameNetcodeStuff;
using MahoragaLC.Config;
using Unity.Netcode;
using UnityEngine;

namespace MahoragaLC.AI
{
    /// <summary>
    /// Mahoraga destroying Old Birds.
    ///
    /// Why a custom mechanic: RadMechAI does not override HitEnemy (damage does nothing) and EnemyAI.KillEnemyOnOwnerClient needs
    /// IsOwner and aborts when !enemyType.canDie unless overrideDestroy. So Mahoraga keeps a per-Old-Bird durability counter
    /// (OldBirdDurability.StrikeDamage; a Slam counts double and a grown Mahoraga hits harder). When it hits zero:
    ///   1. OldBirdDestroyedClientRpc runs on every machine: releases any grabbed player (RadMechAI.CancelTorchPlayerAnimation, public),
    ///      freezes/silences the Old Bird during the 0.45s despawn delay, spawns its explosion prefab as a PURE VISUAL, a camera
    ///      shake by distance, and plays the large-explosion clip through a temporary Diagetic-routed 3D AudioSource.
    ///   2. After 0.45s the SERVER despawns the NetworkObject (NetworkObject.Despawn), removing it from SpawnedEnemies and freeing
    ///      its power level.
    /// </summary>
    public partial class MahoragaAI
    {
        private const float OldBirdDestructionDelay = 0.45f;
        private static readonly FieldInfo? RadMechTargetedThreatColliderField = typeof(RadMechAI)
            .GetField("targetedThreatCollider", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        private struct PendingDestruction
        {
            public RadMechAI OldBird;
            public float DespawnAt;
        }

        private readonly Dictionary<int, float> _oldBirdDurability = new Dictionary<int, float>();
        private readonly List<PendingDestruction> _pendingDestructions = new List<PendingDestruction>();

        private bool HasLivingOldBirdInDomainServer()
        {
            if (RoundManager.Instance == null)
            {
                return false;
            }

            List<EnemyAI> all = RoundManager.Instance.SpawnedEnemies;
            for (int i = 0; i < all.Count; i++)
            {
                if (all[i] is RadMechAI rad
                    && !rad.isEnemyDead
                    && rad.isOutside == isOutside
                    && !IsDestructionPending(rad))
                {
                    return true;
                }
            }

            return false;
        }

        private void StrikeOldBirdServer(RadMechAI oldBird, bool isSlam, MahoragaConfig? cfg)
        {
            if (oldBird == null || oldBird.isEnemyDead || IsDestructionPending(oldBird))
            {
                return;
            }

            int id = oldBird.GetInstanceID();
            float hits = cfg != null ? cfg.OldBirdHitsToDestroy.Value : 8f;
            if (!_oldBirdDurability.TryGetValue(id, out float remaining))
            {
                remaining = hits;
            }

            remaining -= OldBirdDurability.StrikeDamage(isSlam, ScaleFactor);
            _oldBirdDurability[id] = remaining;

            // RadMech.EnemyType has null hitBodySFX / hitEnemyVoiceSFX, so always pass playHitSFX: false.
            try
            {
                oldBird.HitEnemyOnLocalClient(1, transform.forward, null, playHitSFX: false, -1);
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogWarning($"[OldBird] hit feedback failed: {ex.Message}");
            }

            if (remaining <= 0f)
            {
                BeginOldBirdDestructionServer(oldBird);
            }
            else
            {
                AlertOldBirdToSelfServer(oldBird);
            }
        }

        private void AlertOldBirdToSelfServer(RadMechAI oldBird)
        {
            if (oldBird.isEnemyDead
                || oldBird.inFlyingMode
                || oldBird.inTorchPlayerAnimation
                || oldBird.inSpecialAnimation
                || !oldBird.ventAnimationFinished
                || oldBird.currentBehaviourStateIndex == 2)
            {
                return;
            }

            bool needsTargetSwitch = oldBird.targetedThreat == null || !ReferenceEquals(oldBird.targetedThreat.threatScript, this);
            if (!needsTargetSwitch && oldBird.currentBehaviourStateIndex == 1 && !oldBird.lostCreatureInChase && oldBird.isAlerted)
            {
                return;
            }

            if (oldBird.searchForPlayers != null && oldBird.searchForPlayers.inProgress)
            {
                oldBird.StopSearch(oldBird.searchForPlayers);
            }

            Collider? myCollider = GetMahoragaThreatCollider();
            if (myCollider != null)
            {
                RadMechTargetedThreatColliderField?.SetValue(oldBird, myCollider);
            }

            Vector3 chest = transform.position + Vector3.up * (1.8f * ScaleFactor);
            Vector3 eyePos = oldBird.eye != null ? oldBird.eye.position : oldBird.transform.position + Vector3.up * 8.85f;
            float dist = Vector3.Distance(eyePos, chest);

            if (oldBird.targetedThreat == null)
            {
                oldBird.targetedThreat = new Threat();
            }

            oldBird.SetTargetedThreat(this, chest, dist);
            oldBird.focusedThreatTransform = transform;
            oldBird.lostCreatureInChase = false;
            oldBird.losTimer = 0f;
            oldBird.hasLOS = true;

            oldBird.SwitchToBehaviourStateOnLocalClient(1);

            NetworkObject? selfNo = thisNetworkObject != null ? thisNetworkObject : NetworkObject;
            NetworkObject? birdNo = oldBird.thisNetworkObject != null ? oldBird.thisNetworkObject : oldBird.NetworkObject;
            if (selfNo != null && selfNo.IsSpawned && birdNo != null && birdNo.IsSpawned)
            {
                oldBird.SetTargetToThreatClientRpc(new NetworkObjectReference(selfNo), chest);
            }

            oldBird.SetMechAlertedToThreat();
            if (birdNo != null && birdNo.IsSpawned)
            {
                oldBird.SetMechAlertedClientRpc();
            }
        }

        private Collider? GetMahoragaThreatCollider()
        {
            EnemyAICollisionDetect[] detectors = GetComponentsInChildren<EnemyAICollisionDetect>(includeInactive: false);
            for (int i = 0; i < detectors.Length; i++)
            {
                if (detectors[i] == null)
                {
                    continue;
                }

                Collider? col = detectors[i].GetComponent<Collider>();
                if (col != null && col.enabled)
                {
                    return col;
                }
            }

            return null;
        }

        private bool IsDestructionPending(RadMechAI oldBird)
        {
            for (int i = 0; i < _pendingDestructions.Count; i++)
            {
                if (_pendingDestructions[i].OldBird == oldBird)
                {
                    return true;
                }
            }
            return false;
        }

        private void BeginOldBirdDestructionServer(RadMechAI oldBird)
        {
            NetworkObject? no = oldBird.thisNetworkObject != null ? oldBird.thisNetworkObject : oldBird.NetworkObject;
            if (no == null || !no.IsSpawned)
            {
                return;
            }

            // The server commits destruction before notifying the clients about visual effects.
            _pendingDestructions.Add(new PendingDestruction { OldBird = oldBird, DespawnAt = Time.time + OldBirdDestructionDelay });
            int id = oldBird.GetInstanceID();
            _oldBirdDurability.Remove(id);
            _ignoredEnemies.Block(id, Time.time + 60f);
            _enemyAggro.Remove(id);

            oldBird.stunNormalizedTimer = 1f;
            oldBird.isEnemyDead = true;
            if (oldBird.agent != null && oldBird.agent.enabled)
            {
                oldBird.agent.speed = 0f;
            }

            float height = MeasureOldBirdHeight(oldBird);
            OldBirdDestroyedClientRpc(new NetworkObjectReference(no), oldBird.transform.position, height);
        }

        private void TickPendingOldBirdDestructions(float now)
        {
            for (int i = _pendingDestructions.Count - 1; i >= 0; i--)
            {
                PendingDestruction p = _pendingDestructions[i];
                if (p.OldBird == null)
                {
                    _pendingDestructions.RemoveAt(i);
                    continue;
                }

                if (now < p.DespawnAt)
                {
                    continue;
                }

                _pendingDestructions.RemoveAt(i);
                NetworkObject? no = p.OldBird.thisNetworkObject != null ? p.OldBird.thisNetworkObject : p.OldBird.NetworkObject;
                if (no != null && no.IsSpawned)
                {
                    no.Despawn(true);
                }
            }
        }

        [ClientRpc]
        public void OldBirdDestroyedClientRpc(NetworkObjectReference oldBirdRef, Vector3 position, float height)
        {
            RadMechAI? oldBird = null;
            if (oldBirdRef.TryGet(out NetworkObject no) && no != null)
            {
                oldBird = no.GetComponent<RadMechAI>();
            }

            // Release a grabbed / torched player before the Old Bird disappears (CancelSpecialAnimationWithPlayer alone leaves
            // freeRotationInInteractAnimation and the dead-body attachment behind), then freeze & silence it during the 0.45s delay.
            if (oldBird != null)
            {
                try
                {
                    oldBird.CancelTorchPlayerAnimation();
                }
                catch (Exception ex)
                {
                    Plugin.Logger.LogWarning($"[OldBird] could not release grabbed player: {ex.Message}");
                }

                oldBird.isEnemyDead = true;
                oldBird.inFlyingMode = false;
                oldBird.aimingGun = false;
                oldBird.attemptingGrab = false;

                try
                {
                    oldBird.SetChargingForwardOnLocalClient(false);
                }
                catch (Exception ex)
                {
                    Plugin.Logger.LogWarning($"[OldBird] could not clear charging state: {ex.Message}");
                }

                if (oldBird.LocalLRADAudio != null) oldBird.LocalLRADAudio.Stop();
                if (oldBird.LocalLRADAudio2 != null) oldBird.LocalLRADAudio2.Stop();
                if (oldBird.engineSFX != null) oldBird.engineSFX.Stop();
                if (oldBird.flyingDistantAudio != null) oldBird.flyingDistantAudio.Stop();
                if (oldBird.chargeForwardAudio != null) oldBird.chargeForwardAudio.Stop();

                if (IsServer && oldBird.agent != null)
                {
                    oldBird.agent.enabled = false;
                }
            }

            // Purely visual/audible fireball along the body: no damage of any kind.
            GameObject? prefab = oldBird != null ? oldBird.explosionPrefab : null;
            bool useOverride = prefab != null;
            if (prefab == null && StartOfRound.Instance != null)
            {
                prefab = StartOfRound.Instance.explosionPrefab;
            }

            if (prefab != null && RoundManager.Instance != null && RoundManager.Instance.mapPropsContainer != null)
            {
                float h = Mathf.Max(3f, height);
                for (int i = 0; i < 3; i++)
                {
                    Vector3 p = position + Vector3.up * (h * (0.15f + 0.35f * i));
                    Quaternion rot = useOverride ? Quaternion.identity : Quaternion.Euler(-90f, 0f, 0f);
                    GameObject fx = Instantiate(prefab, p, rot, RoundManager.Instance.mapPropsContainer.transform);
                    fx.SetActive(true);
                }
            }

            if (oldBird != null && oldBird.largeExplosionSFX != null && oldBird.largeExplosionSFX.Length > 0)
            {
                AudioClip clip = oldBird.largeExplosionSFX[UnityEngine.Random.Range(0, oldBird.largeExplosionSFX.Length)];
                if (clip != null)
                {
                    AudioSource? mixerTemplate = audioController != null && audioController.bodySource != null
                        ? audioController.bodySource
                        : oldBird.explosionAudio;
                    AudioSource? spatialTemplate = oldBird.explosionAudio != null
                        ? oldBird.explosionAudio
                        : mixerTemplate;
                    PlaySpatialClipAtPoint(clip, position + Vector3.up * 2f, mixerTemplate, spatialTemplate);
                }
            }

            PlayerControllerB? local = GameNetworkManager.Instance != null ? GameNetworkManager.Instance.localPlayerController : null;
            if (local != null && HUDManager.Instance != null)
            {
                float dist = Vector3.Distance(local.transform.position, position);
                if (dist < 25f)
                {
                    HUDManager.Instance.ShakeCamera(ScreenShakeType.Big);
                }
                else if (dist < 45f)
                {
                    HUDManager.Instance.ShakeCamera(ScreenShakeType.Small);
                }
            }
        }

        private static void PlaySpatialClipAtPoint(AudioClip clip, Vector3 position, AudioSource? mixerTemplate, AudioSource? spatialTemplate)
        {
            Transform? parent = RoundManager.Instance != null && RoundManager.Instance.mapPropsContainer != null
                ? RoundManager.Instance.mapPropsContainer.transform
                : null;
            var go = new GameObject("MahoragaOldBirdExplosionSFX");
            if (parent != null)
            {
                go.transform.SetParent(parent, worldPositionStays: true);
            }
            go.transform.position = position;

            AudioSource src = go.AddComponent<AudioSource>();
            src.clip = clip;
            src.playOnAwake = false;
            src.loop = false;
            src.volume = 1f;
            src.spatialBlend = 1f;
            src.dopplerLevel = 0f;

            if (mixerTemplate != null)
            {
                src.outputAudioMixerGroup = mixerTemplate.outputAudioMixerGroup;
            }

            if (spatialTemplate != null)
            {
                src.rolloffMode = spatialTemplate.rolloffMode;
                src.minDistance = spatialTemplate.minDistance;
                src.maxDistance = spatialTemplate.maxDistance;
                if (spatialTemplate.rolloffMode == AudioRolloffMode.Custom)
                {
                    src.SetCustomCurve(
                        AudioSourceCurveType.CustomRolloff,
                        spatialTemplate.GetCustomCurve(AudioSourceCurveType.CustomRolloff));
                }
            }
            else
            {
                src.minDistance = 4f;
                src.maxDistance = 65f;
            }

            src.Play();
            Destroy(go, clip.length + 0.25f);
        }
    }
}
