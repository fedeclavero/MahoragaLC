using System;
using System.Collections.Generic;
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
    ///      spawns the Old Bird's own explosion prefab as a PURE VISUAL (no Landmine.SpawnExplosion, which would damage players/enemies
    ///      and hit Mahoraga itself), a camera shake by distance and the large-explosion clip at the position.
    ///   2. After a short delay the SERVER despawns the NetworkObject (NetworkObject.Despawn, which is exactly what EnemyAI.KillEnemy(destroy)
    ///      does server-side; it works whoever owns the Old Bird). EnemyAI.OnDestroy then removes it from SpawnedEnemies, subtracts its
    ///      power level and runs CancelSpecialAnimationWithPlayer. Missiles in flight (RadMechMissile) destroy themselves when their
    ///      RadMechScript reference dies; the Old Bird's AudioSources go with it.
    /// </summary>
    public partial class MahoragaAI
    {
        private const float OldBirdDestructionDelay = 0.45f;

        private struct PendingDestruction
        {
            public RadMechAI OldBird;
            public float DespawnAt;
        }

        private readonly Dictionary<int, float> _oldBirdDurability = new Dictionary<int, float>();
        private readonly List<PendingDestruction> _pendingDestructions = new List<PendingDestruction>();

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

            // Hit feedback only: RadMechAI keeps no HP, base HitEnemy just plays the enemy-type hit sound.
            try
            {
                oldBird.HitEnemyOnLocalClient(1, transform.forward, null, playHitSFX: true, -1);
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogWarning($"[OldBird] hit feedback failed: {ex.Message}");
            }

            if (remaining <= 0f)
            {
                BeginOldBirdDestructionServer(oldBird);
            }
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

            // Stop it shooting during the death throes (AimAndShootCycle only fires while stunNormalizedTimer <= 0).
            oldBird.stunNormalizedTimer = 1f;
            if (oldBird.agent != null && oldBird.agent.enabled)
            {
                oldBird.agent.speed = 0f;
            }

            float height = MeasureOldBirdHeight(oldBird);
            OldBirdDestroyedClientRpc(new NetworkObjectReference(no), oldBird.transform.position, height);
            _pendingDestructions.Add(new PendingDestruction { OldBird = oldBird, DespawnAt = Time.time + OldBirdDestructionDelay });

            int id = oldBird.GetInstanceID();
            _oldBirdDurability.Remove(id);
            _ignoredEnemies.Block(id, Time.time + 60f);
            _enemyAggro.Remove(id);
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
            // freeRotationInInteractAnimation and the dead-body attachment behind).
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
                    AudioSource.PlayClipAtPoint(clip, position + Vector3.up * 2f, 1f);
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
    }
}
