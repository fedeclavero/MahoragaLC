using MahoragaLC.Config;
using MahoragaLC.Effects;
using Unity.Netcode;
using UnityEngine;

namespace MahoragaLC.AI
{
    /// <summary>
    /// Summoning sequence (state 5): Mahoraga is called out of a pool of black shadow like Megumi's Ten Shadows
    /// ritual in Shibuya. The server decides when it starts (always at spawn) and when it ends; every machine
    /// plays the presentation from its own local clock, driven by the pure SummonTimeline curves.
    ///
    /// Network model:
    ///  - StartSummonClientRpc(duration): sent once by the server from Start(); clients start their clock at 0.
    ///  - ApplySummonSyncClientRpc(elapsed, duration): sent only to a client that asks for a full sync while the
    ///    sequence is running (late join / late spawn), so it resumes at the right moment (no replay of old sounds).
    ///  - The end is the normal SyncBehaviourStateClientRpc that leaves state 5 (server) plus each client's own
    ///    clock reaching the duration, so a lost/late message can never leave the model stuck below the floor.
    ///  - The rise moves the visual child "ModelContainer" only; the root with the NavMeshAgent never moves.
    /// </summary>
    public partial class MahoragaAI
    {
        private static readonly int SummonTriggerHash = Animator.StringToHash("Summon");
        private static readonly int SummonSpeedHash = Animator.StringToHash("SummonSpeed");
        private static readonly int SummonStateHash = Animator.StringToHash("Summon_Emerge");
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        /// <summary>Second (inside mahoraga_summon.ogg) at which the deep emergence boom happens.</summary>
        private const float SummonClipBoomTime = 2.6f;
        private const float PreHideTimeoutSeconds = 1.5f;

        private bool _summonActive;
        private float _summonElapsed;
        private SummonTimeline _summonTimeline = new SummonTimeline(SummonTimeline.ReferenceDuration);
        private bool _summonRumbleStarted;
        private bool _summonRiseShakeDone;
        private bool _summonBoomShakeDone;
        private bool _summonGongDone;
        private bool _summonRoarDone;

        private Transform? _modelContainer; // the "SummonOffset" pivot
        private Vector3 _modelBaseLocalPosition;
        private MahoragaSummonEffect? _summonEffect;
        private bool _summonPreHidden;
        private float _summonSpawnTime;
        private bool _renderersVisible = true;
        private float _summonWheelExtraAngle;
        private float _summonClipLength = -1f;

        /// <summary>True while the invocation is running (state 5 or the local presentation clock).</summary>
        public bool IsSummoning => _summonActive || currentBehaviourStateIndex == (int)MahoragaState.Summoning;

        private bool IsSummonInvulnerable => IsSummoning;

        /// <summary>
        /// Finds (or, for bundles built before this feature, creates) the dedicated "SummonOffset" pivot between the
        /// root and "ModelContainer". Hierarchy: MahoragaPrefab / SummonOffset / ModelContainer / Mahoraga.
        /// Only SummonOffset.localPosition is ever written by the summoning; no scale is touched anywhere, so other
        /// systems (growth) can scale the root or ModelContainer independently.
        /// </summary>
        private void ResolveSummonPivot()
        {
            if (_modelContainer != null)
            {
                return;
            }

            Transform? pivot = transform.Find("SummonOffset");
            if (pivot == null)
            {
                Transform? model = transform.Find("ModelContainer");
                if (model == null)
                {
                    return;
                }

                var pivotGo = new GameObject("SummonOffset");
                pivot = pivotGo.transform;
                pivot.SetParent(transform, false);
                pivot.localPosition = Vector3.zero;
                pivot.localRotation = Quaternion.identity;
                pivot.localScale = Vector3.one;
                model.SetParent(pivot, false);
            }

            _modelContainer = pivot;
            _modelBaseLocalPosition = pivot.localPosition;
        }

        /// <summary>Server: if spawned off the NavMesh (e.g. a debug spawn on a ledge), snap to the nearest point.</summary>
        private void EnsureAgentOnNavMeshServer()
        {
            if (agent == null || !agent.enabled || agent.isOnNavMesh)
            {
                return;
            }

            if (UnityEngine.AI.NavMesh.SamplePosition(transform.position, out UnityEngine.AI.NavMeshHit hit, 8f, agentMask))
            {
                agent.Warp(hit.position);
                Plugin.Logger.LogWarning($"Mahoraga spawned off the NavMesh at {transform.position}; warped to {hit.position}.");
            }
            else
            {
                Plugin.Logger.LogWarning($"Mahoraga spawned off the NavMesh at {transform.position} and no NavMesh was found within 8 m.");
            }
        }

        /// <summary>Called from Awake: caches the visual root and hides the model so it never pops in at full height.</summary>
        private void InitSummonVisuals()
        {
            _summonSpawnTime = Time.time;
            ResolveSummonPivot();

            _summonEffect = GetComponentInChildren<MahoragaSummonEffect>(includeInactive: true);

            MahoragaConfig? cfg = MahoragaConfig.Current;
            if (cfg != null && cfg.EnableSummonSequence.Value)
            {
                // Hidden from the very first frame; released by the summon clock, or by the timeout if the
                // server turned out not to be summoning (different config, old build, late join after the end).
                _summonPreHidden = true;
                ApplyModelOffset(SummonTimeline.SubmergedOffset);
                SetRenderersVisible(false);
            }
        }

        // -------------------------------------------------------------------------------------------------
        // Server
        // -------------------------------------------------------------------------------------------------

        /// <summary>Starts the sequence on the server. Returns false when the sequence is disabled.</summary>
        private bool TryBeginSummonServer(MahoragaConfig? cfg)
        {
            if (cfg == null || !cfg.EnableSummonSequence.Value || isEnemyDead)
            {
                return false;
            }

            var timeline = new SummonTimeline(cfg.SummonDuration.Value);

            previousBehaviourStateIndex = currentBehaviourStateIndex;
            currentBehaviourStateIndex = (int)MahoragaState.Summoning;
            if (enemyBehaviourStates != null && enemyBehaviourStates.Length > (int)MahoragaState.Summoning)
            {
                currentBehaviourState = enemyBehaviourStates[(int)MahoragaState.Summoning];
            }

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

            if (NetworkObject != null && NetworkObject.IsSpawned)
            {
                // Also executes locally on the host.
                StartSummonClientRpc(timeline.Duration);
            }
            else
            {
                StartSummonLocal(timeline.Duration, 0f);
            }

            return true;
        }

        private void EndSummonServer()
        {
            EndSummonPresentation();

            if (agent != null && agent.enabled && agent.isOnNavMesh)
            {
                agent.isStopped = false;
            }

            moveTowardsDestination = true;

            // The summon roar already announced him: do not roar again the instant he starts chasing.
            _roarCooldownTimer = Mathf.Max(_roarCooldownTimer, RoarCooldownSeconds * 0.5f);

            EnterPatrolStateServer();
        }

        private void SendSummonSyncIfActive(ClientRpcParams targetParams)
        {
            if (_summonActive)
            {
                ApplySummonSyncClientRpc(_summonElapsed, _summonTimeline.Duration, targetParams);
            }
        }

        // -------------------------------------------------------------------------------------------------
        // All machines
        // -------------------------------------------------------------------------------------------------

        private void StartSummonLocal(float duration, float elapsed)
        {
            _summonTimeline = new SummonTimeline(duration);
            _summonElapsed = Mathf.Max(0f, elapsed);
            _summonActive = true;
            _summonPreHidden = false;
            _summonWheelExtraAngle = 0f;

            previousBehaviourStateIndex = currentBehaviourStateIndex;
            currentBehaviourStateIndex = (int)MahoragaState.Summoning;
            EnsureBehaviourStatesArray();
            currentBehaviourState = enemyBehaviourStates[(int)MahoragaState.Summoning];

            // Events that are already in the past for a late joiner are marked done so they are not replayed.
            float t = _summonElapsed;
            SummonTimeline tl = _summonTimeline;
            _summonRumbleStarted = false;
            _summonRiseShakeDone = t > tl.EmergeStart;
            _summonBoomShakeDone = t > tl.EmergeEnd;
            _summonGongDone = t > tl.WheelStart;
            _summonRoarDone = t > tl.RoarTime;

            ResolveSummonPivot();

            if (_summonEffect == null)
            {
                _summonEffect = GetComponentInChildren<MahoragaSummonEffect>(includeInactive: true);
            }

            _summonEffect?.Begin(SummonTimeline.PoolRadius);

            if (creatureAnimator != null)
            {
                creatureAnimator.SetFloat(SummonSpeedHash, ComputeSummonAnimatorSpeed(tl.Duration));
                if (t <= 0.02f)
                {
                    creatureAnimator.SetTrigger(SummonTriggerHash);
                }
                else
                {
                    creatureAnimator.Play(SummonStateHash, 0, Mathf.Clamp01(t / tl.Duration));
                }
            }

            ApplySummonVisuals(t);
        }

        /// <summary>Animator speed so the Summon_Emerge clip (whatever its authored length) spans the whole sequence.</summary>
        private float ComputeSummonAnimatorSpeed(float duration)
        {
            if (_summonClipLength < 0f)
            {
                _summonClipLength = SummonTimeline.ReferenceDuration;
                RuntimeAnimatorController? controller = creatureAnimator != null ? creatureAnimator.runtimeAnimatorController : null;
                if (controller != null)
                {
                    AnimationClip[] clips = controller.animationClips;
                    float fallback = -1f;
                    for (int i = 0; i < clips.Length; i++)
                    {
                        if (clips[i] == null)
                        {
                            continue;
                        }

                        if (clips[i].name == "Summon_Emerge")
                        {
                            _summonClipLength = clips[i].length;
                            fallback = -1f;
                            break;
                        }

                        if (clips[i].name == "Adapt_WheelSpin")
                        {
                            fallback = clips[i].length;
                        }
                    }

                    if (fallback > 0f)
                    {
                        _summonClipLength = fallback;
                    }
                }
            }

            return Mathf.Clamp(_summonClipLength / Mathf.Max(0.5f, duration), 0.2f, 3f);
        }

        /// <summary>Per-frame driver (all machines). Called from Update().</summary>
        private void UpdateSummon(float dt)
        {
            if (_summonPreHidden && !_summonActive && Time.time - _summonSpawnTime > PreHideTimeoutSeconds)
            {
                RevealModel();
            }

            if (!_summonActive)
            {
                return;
            }

            if (isEnemyDead)
            {
                EndSummonPresentation();
                return;
            }

            // Invulnerable also means un-stunnable (ZapGun re-applies stunnedIndefinitely after SetEnemyStunned).
            if (stunNormalizedTimer > 0f)
            {
                stunNormalizedTimer = 0f;
            }

            if (stunnedIndefinitely > 0)
            {
                stunnedIndefinitely = 0;
            }

            _summonElapsed += dt;
            ApplySummonVisuals(_summonElapsed);
            FireSummonEvents(_summonElapsed);

            if (_summonTimeline.IsFinished(_summonElapsed))
            {
                if (IsServer)
                {
                    EndSummonServer();
                }
                else
                {
                    EndSummonPresentation();
                }
            }
        }

        private void ApplySummonVisuals(float t)
        {
            SummonTimeline tl = _summonTimeline;
            ApplyModelOffset(tl.ModelOffset(t));
            SetRenderersVisible(tl.IsModelVisible(t));
            ApplyBodyTint(tl.ShadowCoat(t));
            _summonWheelExtraAngle = tl.WheelExtraAngle(t);
            _summonEffect?.Evaluate(tl.PoolScale(t), tl.DropletRate(t), t);
        }

        private void FireSummonEvents(float t)
        {
            SummonTimeline tl = _summonTimeline;
            MahoragaConfig? cfg = MahoragaConfig.Current;
            bool shake = cfg == null || cfg.SummonCameraShake.Value;

            // The rumble clip is authored so its boom lands at 2.6 s; shift the start so the boom lands exactly
            // on EmergeEnd for any configured duration (start later when longer, seek into the clip when shorter).
            float clipTime = t - (tl.EmergeEnd - SummonClipBoomTime);
            if (!_summonRumbleStarted && clipTime >= 0f)
            {
                _summonRumbleStarted = true;
                audioController?.PlaySummonRumble(clipTime);
            }

            if (!_summonRiseShakeDone && t >= tl.EmergeStart)
            {
                _summonRiseShakeDone = true;
                if (shake)
                {
                    ShakeLocalCamera(ScreenShakeType.Small, 16f, ScreenShakeType.Small, 0f);
                }
            }

            if (!_summonBoomShakeDone && t >= tl.EmergeEnd)
            {
                _summonBoomShakeDone = true;
                if (shake)
                {
                    ShakeLocalCamera(ScreenShakeType.Big, 10f, ScreenShakeType.Small, 18f);
                }
            }

            if (!_summonGongDone && t >= tl.WheelStart)
            {
                _summonGongDone = true;
                _emissiveDelayTimer = 0f;
                _emissivePeakIntensity = 60f;
                _emissiveFlashDuration = 1.8f;
                _emissiveFlashTimer = _emissiveFlashDuration;
                audioController?.PlayWheelTurn();
            }

            if (!_summonRoarDone && t >= tl.RoarTime)
            {
                _summonRoarDone = true;
                audioController?.PlayRoar();
                if (shake)
                {
                    ShakeLocalCamera(ScreenShakeType.Big, 9f, ScreenShakeType.Small, 16f);
                }
            }
        }

        private void ShakeLocalCamera(ScreenShakeType near, float nearRange, ScreenShakeType far, float farRange)
        {
            if (HUDManager.Instance == null || GameNetworkManager.Instance == null)
            {
                return;
            }

            GameNetcodeStuff.PlayerControllerB? local = GameNetworkManager.Instance.localPlayerController;
            if (local == null)
            {
                return;
            }

            GameNetcodeStuff.PlayerControllerB listener = local.isPlayerDead && local.spectatedPlayerScript != null
                ? local.spectatedPlayerScript
                : local;

            float dist = Vector3.Distance(listener.transform.position, transform.position);
            if (dist <= nearRange)
            {
                HUDManager.Instance.ShakeCamera(near);
            }
            else if (farRange > 0f && dist <= farRange)
            {
                HUDManager.Instance.ShakeCamera(far);
            }
        }

        /// <summary>Returns everything to the normal, fully emerged state (idempotent).</summary>
        private void EndSummonPresentation()
        {
            _summonActive = false;
            _summonPreHidden = false;
            _summonWheelExtraAngle = 0f;
            ApplyModelOffset(0f);
            SetRenderersVisible(true);
            ApplyBodyTint(1f);
            _summonEffect?.End();
        }

        private void RevealModel()
        {
            _summonPreHidden = false;
            ApplyModelOffset(0f);
            SetRenderersVisible(true);
        }

        private void ApplyModelOffset(float yOffset)
        {
            if (_modelContainer != null)
            {
                _modelContainer.localPosition = _modelBaseLocalPosition + Vector3.up * yOffset;
            }
        }

        private void SetRenderersVisible(bool visible)
        {
            if (_renderersVisible == visible || skinnedMeshRenderers == null)
            {
                return;
            }

            _renderersVisible = visible;
            for (int i = 0; i < skinnedMeshRenderers.Length; i++)
            {
                if (skinnedMeshRenderers[i] != null)
                {
                    skinnedMeshRenderers[i].enabled = visible;
                }
            }
        }

        private void ApplyBodyTint(float k)
        {
            if (mainRenderer == null || _propBlock == null)
            {
                return;
            }

            mainRenderer.GetPropertyBlock(_propBlock);
            _propBlock.SetColor(BaseColorId, new Color(k, k, k, 1f));
            mainRenderer.SetPropertyBlock(_propBlock);
        }

        // -------------------------------------------------------------------------------------------------
        // RPCs
        // -------------------------------------------------------------------------------------------------

        [ClientRpc]
        public void StartSummonClientRpc(float duration)
        {
            if (isEnemyDead)
            {
                return;
            }

            StartSummonLocal(duration, 0f);
        }

        [ClientRpc]
        public void ApplySummonSyncClientRpc(float elapsed, float duration, ClientRpcParams rpcParams = default)
        {
            if (IsServer || isEnemyDead)
            {
                return;
            }

            if (_summonActive)
            {
                // Already running from StartSummonClientRpc: just catch up, never replay events.
                _summonElapsed = Mathf.Max(_summonElapsed, elapsed);
                return;
            }

            StartSummonLocal(duration, elapsed);
        }
    }
}
