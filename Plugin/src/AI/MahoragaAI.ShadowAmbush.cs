using System.Collections.Generic;
using GameNetcodeStuff;
using MahoragaLC.Config;
using MahoragaLC.Effects;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;

namespace MahoragaLC.AI
{
    /// <summary>
    /// Late-game Shadow Dive &amp; Ambush Re-Summon (state 7: Submerging -> warp -> state 5: Summoning).
    /// When it is late in the day, Mahoraga has not seen any player for ShadowAmbushLostSightDelay seconds,
    /// no living Old Bird exists in Mahoraga's domain, and cooldown has elapsed, the server pre-validates
    /// a safe NavMesh ambush point ~4.5-8.0 m in front of a living player in the same domain, plays the
    /// downward Summon_Submerge dive + shadow pool, warps cleanly to the ambush point facing the player,
    /// and immediately triggers TryBeginSummonServer emergence.
    /// </summary>
    public partial class MahoragaAI
    {
        private static readonly int SubmergeHash = Animator.StringToHash("Submerge");
        private static readonly int SubmergeSpeedHash = Animator.StringToHash("SubmergeSpeed");
        private static readonly int SubmergeStateHash = Animator.StringToHash("Summon_Submerge");

        private static readonly float[] PrimaryAmbushDistances = { 6.0f, 5.0f, 7.2f, 4.5f, 8.0f };
        private static readonly float[] PrimaryAmbushYawOffsets = { 0f, -20f, 20f, -38f, 38f };
        private static readonly float[] FallbackAmbushDistances = { 5.5f, 7.0f, 4.5f };
        private static readonly float[] FallbackAmbushYawOffsets = { 60f, -60f, 90f, -90f, 120f, -120f, 150f, -150f, 180f };

        private readonly List<PlayerControllerB> _ambushPlayerCandidates = new List<PlayerControllerB>(8);
        private readonly NavMeshPath _ambushConnectivityPath = new NavMeshPath();

        private bool _submergeActive;
        private float _submergeElapsed;
        private SubmergeTimeline _submergeTimeline = new SubmergeTimeline(SubmergeTimeline.ReferenceDuration);
        private float _submergeClipLength = -1f;

        private float _lastPlayerSightTime = -999f;
        private float _shadowAmbushCooldownEndTime;
        private float _nextAmbushEvalTime;
        private Vector3 _pendingAmbushPos;
        private Quaternion _pendingAmbushRot = Quaternion.identity;
        private int _pendingAmbushTargetClientId = -1;

        /// <summary>True while Mahoraga is diving downward into the shadow pool (state 7).</summary>
        internal bool IsSubmerging => _submergeActive || currentBehaviourStateIndex == (int)MahoragaState.Submerging;

        private void InitShadowAmbushServer()
        {
            float now = Time.time;
            _lastPlayerSightTime = now;
            _shadowAmbushCooldownEndTime = 0f;
            _nextAmbushEvalTime = now + 2.0f;
            _pendingAmbushTargetClientId = -1;
        }

        private void RecordPlayerSightServer()
        {
            _lastPlayerSightTime = Time.time;
        }

        // -------------------------------------------------------------------------------------------------
        // Server gating & pre-validation
        // -------------------------------------------------------------------------------------------------

        private bool TryEvaluateShadowAmbushServer(MahoragaConfig? cfg)
        {
            if (!IsServer || cfg == null || isEnemyDead)
            {
                return false;
            }

            float now = Time.time;
            if (now < _nextAmbushEvalTime)
            {
                return false;
            }
            _nextAmbushEvalTime = now + 1.0f;

            float normalizedTime = TimeOfDay.Instance != null ? TimeOfDay.Instance.normalizedTimeOfDay : 0f;
            float timeSinceLostSight = now - _lastPlayerSightTime;
            float cooldownRemaining = Mathf.Max(0f, _shadowAmbushCooldownEndTime - now);

            bool canTrigger = ShadowAmbushLogic.CanTriggerAmbush(
                enableShadowAmbush: cfg.EnableShadowAmbush.Value,
                normalizedTimeOfDay: normalizedTime,
                minTimeOfDay: cfg.ShadowAmbushMinTime.Value,
                timeSinceLostSight: timeSinceLostSight,
                lostSightDelay: cfg.ShadowAmbushLostSightDelay.Value,
                cooldownRemaining: cooldownRemaining,
                hasLivingOldBirdInDomain: HasLivingOldBirdInDomainServer(),
                isDead: isEnemyDead,
                isAttacking: currentBehaviourStateIndex == (int)MahoragaState.Attacking,
                isAdapting: currentBehaviourStateIndex == (int)MahoragaState.Adapting,
                isSummoning: IsSummoning,
                isSubmerging: IsSubmerging,
                isEngagingEnemy: currentBehaviourStateIndex == (int)MahoragaState.EngagingEnemy,
                isStunned: stunNormalizedTimer > 0f || stunnedIndefinitely > 0,
                isForcingDoor: _forcingDoor != null);

            if (!canTrigger)
            {
                return false;
            }

            if (!TryFindShadowAmbushTargetServer(out Vector3 ambushPos, out Quaternion ambushRot, out PlayerControllerB? target))
            {
                // Back off slightly before repeating NavMesh/LOS candidate sweeps.
                _nextAmbushEvalTime = now + 3.0f;
                return false;
            }

            _pendingAmbushPos = ambushPos;
            _pendingAmbushRot = ambushRot;
            _pendingAmbushTargetClientId = (int)target!.playerClientId;

            BeginSubmergeServer(cfg);
            return true;
        }

        private bool TryFindShadowAmbushTargetServer(out Vector3 ambushPos, out Quaternion ambushRot, out PlayerControllerB? ambushPlayer)
        {
            ambushPos = Vector3.zero;
            ambushRot = Quaternion.identity;
            ambushPlayer = null;

            if (StartOfRound.Instance == null || agent == null || !agent.enabled)
            {
                return false;
            }

            _ambushPlayerCandidates.Clear();
            PlayerControllerB[] players = StartOfRound.Instance.allPlayerScripts;
            for (int i = 0; i < players.Length; i++)
            {
                PlayerControllerB p = players[i];
                if (p == null || p.isPlayerDead || !p.isPlayerControlled)
                {
                    continue;
                }

                // Strict same-domain requirement (inside factory vs outside).
                if (p.isInsideFactory != !isOutside)
                {
                    continue;
                }

                if (!PlayerIsTargetable(p))
                {
                    continue;
                }

                if (isOutside && p.isInHangarShipRoom && StartOfRound.Instance.hangarDoorsClosed)
                {
                    continue;
                }

                _ambushPlayerCandidates.Add(p);
            }

            if (_ambushPlayerCandidates.Count == 0)
            {
                return false;
            }

            // Fisher-Yates shuffle so Mahoraga picks a random living targetable player in its domain.
            for (int i = _ambushPlayerCandidates.Count - 1; i > 0; i--)
            {
                int swapIdx = UnityEngine.Random.Range(0, i + 1);
                (_ambushPlayerCandidates[i], _ambushPlayerCandidates[swapIdx]) = (_ambushPlayerCandidates[swapIdx], _ambushPlayerCandidates[i]);
            }

            for (int i = 0; i < _ambushPlayerCandidates.Count; i++)
            {
                PlayerControllerB candidate = _ambushPlayerCandidates[i];
                if (TryFindAmbushPointForPlayerServer(candidate, out ambushPos, out ambushRot))
                {
                    ambushPlayer = candidate;
                    return true;
                }
            }

            return false;
        }

        private bool TryFindAmbushPointForPlayerServer(PlayerControllerB player, out Vector3 ambushPos, out Quaternion ambushRot)
        {
            ambushPos = Vector3.zero;
            ambushRot = Quaternion.identity;

            if (StartOfRound.Instance == null || agent == null)
            {
                return false;
            }

            Vector3 playerPos = player.transform.position;
            Vector3 playerEye = player.gameplayCamera != null
                ? player.gameplayCamera.transform.position
                : playerPos + Vector3.up * 1.65f;

            Vector3 lookForward = player.gameplayCamera != null
                ? player.gameplayCamera.transform.forward
                : player.transform.forward;
            lookForward.y = 0f;
            if (lookForward.sqrMagnitude < 0.001f)
            {
                lookForward = player.transform.forward;
                lookForward.y = 0f;
            }
            if (lookForward.sqrMagnitude < 0.001f)
            {
                lookForward = Vector3.forward;
            }
            else
            {
                lookForward.Normalize();
            }

            int areaMask = agent.areaMask != 0 ? agent.areaMask : agentMask;
            int obstacleMask = StartOfRound.Instance.collidersAndRoomMaskAndDefault;
            bool hasPlayerNav = NavMesh.SamplePosition(playerPos, out NavMeshHit playerNavHit, 2.5f, areaMask);

            // Pass 1: directly in front of the player's forward look direction (4.5 - 8.0 m).
            for (int d = 0; d < PrimaryAmbushDistances.Length; d++)
            {
                float dist = PrimaryAmbushDistances[d];
                for (int a = 0; a < PrimaryAmbushYawOffsets.Length; a++)
                {
                    Vector3 dir = Quaternion.Euler(0f, PrimaryAmbushYawOffsets[a], 0f) * lookForward;
                    Vector3 probe = playerPos + dir * dist;
                    if (ValidateAmbushCandidatePoint(probe, playerPos, playerEye, hasPlayerNav, playerNavHit.position, areaMask, obstacleMask, out ambushPos, out ambushRot))
                    {
                        return true;
                    }
                }
            }

            // Pass 2: radial ring fallback around the player.
            for (int d = 0; d < FallbackAmbushDistances.Length; d++)
            {
                float dist = FallbackAmbushDistances[d];
                for (int a = 0; a < FallbackAmbushYawOffsets.Length; a++)
                {
                    Vector3 dir = Quaternion.Euler(0f, FallbackAmbushYawOffsets[a], 0f) * lookForward;
                    Vector3 probe = playerPos + dir * dist;
                    if (ValidateAmbushCandidatePoint(probe, playerPos, playerEye, hasPlayerNav, playerNavHit.position, areaMask, obstacleMask, out ambushPos, out ambushRot))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private bool ValidateAmbushCandidatePoint(
            Vector3 probe,
            Vector3 playerPos,
            Vector3 playerEye,
            bool hasPlayerNav,
            Vector3 playerNavPos,
            int areaMask,
            int obstacleMask,
            out Vector3 ambushPos,
            out Quaternion ambushRot)
        {
            ambushPos = Vector3.zero;
            ambushRot = Quaternion.identity;

            if (!NavMesh.SamplePosition(probe, out NavMeshHit hit, 2.0f, areaMask))
            {
                return false;
            }

            Vector3 candidate = hit.position;
            if (Mathf.Abs(candidate.y - playerPos.y) > 2.2f)
            {
                return false;
            }

            if (HuntLogic.IsOutsideAt(candidate.y, isOutside) != isOutside)
            {
                return false;
            }

            Vector3 flatToPlayer = playerPos - candidate;
            flatToPlayer.y = 0f;
            float horizDist = flatToPlayer.magnitude;
            if (horizDist < 3.8f || horizDist > 9.0f)
            {
                return false;
            }

            // Verify vertical body clearance so Mahoraga never emerges inside a ceiling slab or low vent.
            float bodyCheckHeight = Mathf.Min(3.2f * ScaleFactor, isOutside ? 6.5f : 3.4f);
            if (Physics.Linecast(candidate + Vector3.up * 0.5f, candidate + Vector3.up * bodyCheckHeight, obstacleMask, QueryTriggerInteraction.Ignore))
            {
                return false;
            }

            // Verify clear line of sight between the player and Mahoraga's emerging torso.
            Vector3 chestPos = candidate + Vector3.up * (1.8f * ScaleFactor);
            if (Physics.Linecast(playerEye, chestPos, obstacleMask, QueryTriggerInteraction.Ignore))
            {
                return false;
            }

            // Ensure the ambush spot is on the same connected NavMesh region as the player (not behind a locked wall/pit).
            if (hasPlayerNav)
            {
                if (!NavMesh.CalculatePath(candidate, playerNavPos, areaMask, _ambushConnectivityPath) ||
                    _ambushConnectivityPath.status != NavMeshPathStatus.PathComplete)
                {
                    return false;
                }
            }

            ambushPos = candidate;
            ambushRot = Quaternion.LookRotation(flatToPlayer.normalized, Vector3.up);
            return true;
        }

        private void BeginSubmergeServer(MahoragaConfig cfg)
        {
            var timeline = new SubmergeTimeline(cfg.ShadowAmbushSubmergeDuration.Value);

            _forcingDoor = null;
            StopSearch(currentSearch);
            targetPlayer = null;
            movingTowardsTargetPlayer = false;
            moveTowardsDestination = false;

            if (agent != null && agent.enabled)
            {
                agent.speed = 0f;
                if (agent.isOnNavMesh)
                {
                    agent.ResetPath();
                    agent.isStopped = true;
                }
            }

            _shadowAmbushCooldownEndTime = Time.time + cfg.ShadowAmbushCooldown.Value + timeline.Duration + cfg.SummonDuration.Value;

            EnsureBehaviourStatesArray();
            previousBehaviourStateIndex = currentBehaviourStateIndex;
            currentBehaviourStateIndex = (int)MahoragaState.Submerging;
            currentBehaviourState = enemyBehaviourStates[(int)MahoragaState.Submerging];

            if (NetworkObject != null && NetworkObject.IsSpawned)
            {
                StartSubmergeClientRpc(timeline.Duration);
            }
            else
            {
                StartSubmergeLocal(timeline.Duration, 0f);
            }
        }

        private void CompleteSubmergeAndAmbushServer()
        {
            if (isEnemyDead)
            {
                EndSubmergePresentation();
                return;
            }

            Vector3 warpPos = _pendingAmbushPos;
            int areaMask = agent != null && agent.areaMask != 0 ? agent.areaMask : agentMask;
            if (NavMesh.SamplePosition(warpPos, out NavMeshHit hit, 2.0f, areaMask))
            {
                warpPos = hit.position;
            }

            float yawDegrees = _pendingAmbushRot.eulerAngles.y;
            PlayerControllerB? victim = ResolvePlayerByClientId(_pendingAmbushTargetClientId);
            if (victim != null && PlayerIsTargetable(victim))
            {
                Vector3 toVictim = victim.transform.position - warpPos;
                toVictim.y = 0f;
                if (toVictim.sqrMagnitude > 0.01f)
                {
                    yawDegrees = Quaternion.LookRotation(toVictim.normalized, Vector3.up).eulerAngles.y;
                }
            }

            if (NetworkObject != null && NetworkObject.IsSpawned)
            {
                WarpForAmbushClientRpc(warpPos, yawDegrees);
            }
            else
            {
                ApplyAmbushWarpLocal(warpPos, yawDegrees);
            }

            SyncPositionToClients();
            RecordPlayerSightServer();

            if (!TryBeginSummonServer(MahoragaConfig.Current, forceSummon: true))
            {
                EndSubmergePresentation();
                if (agent != null && agent.enabled && agent.isOnNavMesh)
                {
                    agent.isStopped = false;
                }
                moveTowardsDestination = true;
                EnterPatrolStateServer();
            }
        }

        private void SendSubmergeSyncIfActive(ClientRpcParams targetParams)
        {
            if (_submergeActive)
            {
                ApplySubmergeSyncClientRpc(_submergeElapsed, _submergeTimeline.Duration, targetParams);
            }
        }

        // -------------------------------------------------------------------------------------------------
        // All machines
        // -------------------------------------------------------------------------------------------------

        private void StartSubmergeLocal(float duration, float elapsed)
        {
            _summonActive = false;
            _summonPreHidden = false;
            _submergeTimeline = new SubmergeTimeline(duration);
            _submergeElapsed = Mathf.Max(0f, elapsed);
            _submergeActive = true;

            EnsureBehaviourStatesArray();
            previousBehaviourStateIndex = currentBehaviourStateIndex;
            currentBehaviourStateIndex = (int)MahoragaState.Submerging;
            currentBehaviourState = enemyBehaviourStates[(int)MahoragaState.Submerging];

            ResolveSummonPivot();

            if (_summonEffect == null)
            {
                _summonEffect = GetComponentInChildren<MahoragaSummonEffect>(includeInactive: true);
            }

            _summonEffect?.Begin(SummonTimeline.PoolRadius * ScaleFactor);

            if (_submergeElapsed <= 0.1f)
            {
                audioController?.PlaySummonRumble(0f);
            }

            if (creatureAnimator != null)
            {
                creatureAnimator.ResetTrigger(SummonTriggerHash);
                creatureAnimator.SetFloat(SubmergeSpeedHash, ComputeSubmergeAnimatorSpeed(_submergeTimeline.Duration));
                if (_submergeElapsed <= 0.02f)
                {
                    creatureAnimator.SetTrigger(SubmergeHash);
                }
                else
                {
                    creatureAnimator.Play(SubmergeStateHash, 0, Mathf.Clamp01(_submergeElapsed / _submergeTimeline.Duration));
                }
            }

            ApplySubmergeVisuals(_submergeElapsed);
        }

        private float ComputeSubmergeAnimatorSpeed(float duration)
        {
            if (_submergeClipLength < 0f)
            {
                _submergeClipLength = SubmergeTimeline.ReferenceDuration;
                RuntimeAnimatorController? controller = creatureAnimator != null ? creatureAnimator.runtimeAnimatorController : null;
                if (controller != null)
                {
                    AnimationClip[] clips = controller.animationClips;
                    for (int i = 0; i < clips.Length; i++)
                    {
                        if (clips[i] != null && clips[i].name == "Summon_Submerge")
                        {
                            _submergeClipLength = clips[i].length;
                            break;
                        }
                    }
                }
            }

            return Mathf.Clamp(_submergeClipLength / Mathf.Max(0.5f, duration), 0.25f, 3f);
        }

        private void UpdateSubmerge(float dt)
        {
            if (!_submergeActive)
            {
                return;
            }

            if (isEnemyDead)
            {
                EndSubmergePresentation();
                return;
            }

            if (stunNormalizedTimer >= 0f)
            {
                stunNormalizedTimer = -1f;
            }

            if (stunnedIndefinitely > 0)
            {
                stunnedIndefinitely = 0;
            }

            _submergeElapsed += dt;
            ApplySubmergeVisuals(_submergeElapsed);

            if (_submergeTimeline.IsFinished(_submergeElapsed))
            {
                if (IsServer)
                {
                    CompleteSubmergeAndAmbushServer();
                }
                else
                {
                    EndSubmergePresentationForHandoff();
                }
            }
        }

        private void ApplySubmergeVisuals(float t)
        {
            SubmergeTimeline tl = _submergeTimeline;
            ApplyModelOffset(tl.ModelOffset(t));
            SetRenderersVisible(tl.IsModelVisible(t));
            ApplyBodyTint(tl.ShadowCoat(t));
            _summonEffect?.Evaluate(tl.PoolScale(t), tl.DropletRate(t), t);
        }

        private void ApplyAmbushWarpLocal(Vector3 ambushPos, float yawDegrees)
        {
            EndSubmergePresentationForHandoff();

            serverPosition = ambushPos;
            targetYRotation = yawDegrees;
            previousYRotation = yawDegrees;
            tempVelocity = Vector3.zero;

            if (IsServer && agent != null && agent.enabled)
            {
                agent.Warp(ambushPos);
            }
            else
            {
                transform.position = ambushPos;
            }

            transform.rotation = Quaternion.Euler(0f, yawDegrees, 0f);
            _lastFramePosition = ambushPos;
        }

        /// <summary>Handoff between Submerge end and immediate Summon_Emerge start: stays hidden briefly without 1-frame pop-in.</summary>
        private void EndSubmergePresentationForHandoff()
        {
            _submergeActive = false;
            _summonEffect?.End();
            ApplyModelOffset(SummonTimeline.SubmergedOffset);
            SetRenderersVisible(false);
            _summonPreHidden = true;
            _summonSpawnTime = Time.time;
        }

        /// <summary>Full visual restoration on death, destroy, or abort (always restores _BaseColor tint and renderers).</summary>
        private void EndSubmergePresentation()
        {
            _submergeActive = false;
            _summonEffect?.End();
            ApplyModelOffset(0f);
            SetRenderersVisible(true);
            ApplyBodyTint(1f);
        }

        // -------------------------------------------------------------------------------------------------
        // RPCs
        // -------------------------------------------------------------------------------------------------

        [ClientRpc]
        public void StartSubmergeClientRpc(float duration)
        {
            if (isEnemyDead)
            {
                return;
            }

            StartSubmergeLocal(duration, 0f);
        }

        [ClientRpc]
        public void ApplySubmergeSyncClientRpc(float elapsed, float duration, ClientRpcParams rpcParams = default)
        {
            if (IsServer || isEnemyDead)
            {
                return;
            }

            if (_submergeActive)
            {
                _submergeElapsed = Mathf.Max(_submergeElapsed, elapsed);
                return;
            }

            StartSubmergeLocal(duration, elapsed);
        }

        [ClientRpc]
        public void WarpForAmbushClientRpc(Vector3 ambushPos, float yawDegrees)
        {
            if (isEnemyDead)
            {
                return;
            }

            ApplyAmbushWarpLocal(ambushPos, yawDegrees);
        }
    }
}
