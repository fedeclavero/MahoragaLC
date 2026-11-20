using GameNetcodeStuff;
using MahoragaLC.Config;
using MahoragaLC.Effects;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;

namespace MahoragaLC.AI
{
    /// <summary>Late-game shadow dive toward the location of the last accepted sound.</summary>
    public partial class MahoragaAI
    {
        private static readonly int SubmergeHash = Animator.StringToHash("Submerge");
        private static readonly int SubmergeSpeedHash = Animator.StringToHash("SubmergeSpeed");
        private static readonly int SubmergeStateHash = Animator.StringToHash("Summon_Submerge");

        private bool _submergeActive;
        private float _submergeElapsed;
        private SubmergeTimeline _submergeTimeline = new SubmergeTimeline(SubmergeTimeline.ReferenceDuration);
        private float _submergeClipLength = -1f;

        private float _lastPlayerContactAt = -999f;
        private float _shadowAmbushCooldownEndTime;
        private float _nextAmbushEvalTime;
        private Vector3 _pendingAmbushPos;
        private Quaternion _pendingAmbushRot = Quaternion.identity;

        /// <summary>True while Mahoraga is diving downward into the shadow pool (state 7).</summary>
        internal bool IsSubmerging => _submergeActive || currentBehaviourStateIndex == (int)MahoragaState.Submerging;

        private void InitShadowAmbushServer()
        {
            float now = Time.time;
            _lastPlayerContactAt = now;
            _shadowAmbushCooldownEndTime = 0f;
            _nextAmbushEvalTime = now + 2.0f;
        }

        private void RecordPlayerContactServer()
        {
            _lastPlayerContactAt = Time.time;
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
            float timeSinceLostSight = now - _lastPlayerContactAt;
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

            // A late-game dive still needs a recent acoustic lead, and can only use the recorded point.
            if (now - _lastAudibleTime > 24f || agent == null || !agent.enabled || !agent.isOnNavMesh ||
                !NavMesh.SamplePosition(_lastAudiblePosition, out NavMeshHit hit, 1.5f, agent.areaMask) ||
                !agent.CalculatePath(hit.position, _safePath) || _safePath.status != NavMeshPathStatus.PathComplete)
            {
                return false;
            }

            Vector3 heading = Vector3.ProjectOnPlane(hit.position - transform.position, Vector3.up);
            if (heading.sqrMagnitude < 25f || heading.sqrMagnitude > 625f)
            {
                return false;
            }

            _pendingAmbushPos = hit.position;
            _pendingAmbushRot = Quaternion.LookRotation(heading);
            _lastAudibleTime = float.NegativeInfinity;
            BeginSubmergeServer(cfg);
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
            if (NetworkObject != null && NetworkObject.IsSpawned)
            {
                WarpForAmbushClientRpc(warpPos, yawDegrees);
            }
            else
            {
                ApplyAmbushWarpLocal(warpPos, yawDegrees);
            }

            SyncPositionToClients();
            RecordPlayerContactServer();

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
