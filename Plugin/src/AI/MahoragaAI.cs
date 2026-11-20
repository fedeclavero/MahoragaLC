using System;
using GameNetcodeStuff;
using MahoragaLC.Audio;
using MahoragaLC.Config;
using MahoragaLC.Patches;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;

namespace MahoragaLC.AI
{
    public enum MahoragaState
    {
        Patrolling = 0,
        Investigating = 1,
        Chasing = 2,
        Attacking = 3,
        Adapting = 4,
        Summoning = 5,
        EngagingEnemy = 6, // fights Old Birds (MahoragaAI.Combat.cs)
        Submerging = 7     // late-game shadow dive before ambush re-summon (MahoragaAI.ShadowAmbush.cs)
    }

    /// <summary>
    /// Server-authoritative apex EnemyAI for Divine General Mahoraga.
    /// Ownership permanently remains on Server/Host (ChangeOwnershipOfEnemy is never called).
    /// All serialized fields strictly match ASSET_CONTRACT.md Section 7.3.
    /// </summary>
    public partial class MahoragaAI : EnemyAI
    {
        // Exact SerializedField names required by ASSET_CONTRACT.md Section 7.3
        public Transform? wheelBone;
        public Transform? swordBone;
        public Light? wheelGlowLight;
        public MahoragaAudioController? audioController;
        public Renderer? mainRenderer;

        // Exact Animator parameter hashes from ASSET_CONTRACT.md Section 4.2
        private static readonly int SpeedHash = Animator.StringToHash("Speed");
        private static readonly int IsChasingHash = Animator.StringToHash("IsChasing");
        private static readonly int IsStunnedHash = Animator.StringToHash("IsStunned");
        private static readonly int IsDeadHash = Animator.StringToHash("IsDead");
        private static readonly int AttackCleaveHash = Animator.StringToHash("AttackCleave");
        private static readonly int AttackSlamHash = Animator.StringToHash("AttackSlam");
        private static readonly int AdaptHash = Animator.StringToHash("Adapt");

        private static readonly int EmissiveColorId = Shader.PropertyToID("_EmissiveColor");
        private static readonly Color BaseGoldColor = new Color(1.0f, 0.722f, 0.188f, 1.0f); // #FFB830

        private const int InteractableLayerMask = 1 << 9; // Layer 9: InteractableObject (DoorLock)
        private const float AttackEntryRange = 2.6f;
        private const float CleaveReach = 3.0f;
        private const float CleaveHalfArcDeg = 55f; // 110° forward cone
        private const float SlamReach = 3.5f;
        private const float VerticalHitTolerance = 1.8f;
        private const float CleaveWindupTime = 0.38f;
        private const float CleaveDuration = 1.15f;
        private const float SlamWindupTime = 0.55f;
        private const float SlamDuration = 1.45f;
        private const float PostAttackCooldown = 1.35f;
        private const float AdaptationDuration = 2.20f;
        private const float WheelSpinDelay = 0.25f;
        private const float WheelSpinDuration = 0.80f;
        private const float InvestigateTimeout = 8.0f;
        private const float InvestigateArrivalPause = 1.5f;
        private const float HearingMemorySeconds = 8f;
        private const float TouchRange = 1f;
        private const float RoarCooldownSeconds = 12.0f;
        private const float LockedDoorForceDuration = 2.20f;
        private const float LockedDoorScanRange = 3.2f;

        private readonly MahoragaAdaptationSystem _adaptation = new MahoragaAdaptationSystem();
        private readonly Collider[] _doorOverlapBuffer = new Collider[8];
        private NavMeshPath _safePath = null!;
        private readonly Vector3[] _safePathCorners = new Vector3[64];

        private float _baseAgentRadius;

        private MaterialPropertyBlock? _propBlock;
        private int _wheelMaterialIndex = -1;
        private Quaternion _wheelRestLocalRotation = Quaternion.identity;
        private bool _hasWheelRestRotation;
        private float _wheelDisplayAngle;
        private float _wheelSpinStartAngle;
        private float _wheelSpinTargetAngle;
        private float _wheelSpinElapsed = 999f;
        private bool _wheelSpinActive;
        private float _wheelSpinDelay = WheelSpinDelay;
        private float _wheelSpinDuration = WheelSpinDuration;

        private float _emissivePeakIntensity;
        private float _emissiveFlashTimer;
        private float _emissiveFlashDuration = 1.8f;
        private float _emissiveDelayTimer;

        private Vector3 _lastFramePosition;
        private float _smoothedHorizontalSpeed;
        private bool _deathHandled;

        // Server-authoritative runtime timers & state
        private Vector3 _investigatePos;
        private float _investigateTimer;
        private float _investigateArrivalTimer;
        private bool _investigateArrived;

        private Vector3 _lastKnownTargetPos;
        private float _lastPlayerContactTime;
        private Vector3 _lastAudiblePosition;
        private float _lastAudibleTime = float.NegativeInfinity;
        private float _roarCooldownTimer;
        private float _attackCooldownTimer;
        private float _deflectRpcCooldownTimer;

        private bool _currentAttackIsSlam;
        private float _attackElapsed;
        private bool _attackHitResolved;
        private bool _attackSlashPlayed;
        private int _attacksSinceLastSlam;

        private float _adaptationElapsed;

        private DoorLock? _forcingDoor;
        private float _doorForceElapsed;
        private int _doorBashStage;

        private float _clientContactCooldown;

        public override void Awake()
        {
            base.Awake();
            ResolvePrefabReferences();
            CacheWheelRestRotation();
            _propBlock = new MaterialPropertyBlock();
            InitSummonVisuals();
            InstallThreatProxy();
            ResolveScaleRefs();
        }

        public override void Start()
        {
            base.Start();
            _safePath = new NavMeshPath();

            EnsureBehaviourStatesArray();
            ResolvePrefabReferences();
            CacheWheelRestRotation();

            // Manual spawns can use either prefab; apply the jump mask after the domain is set.
            bool spawnedOutside = HuntLogic.IsOutsideAt(transform.position.y, isOutside);
            if (isOutside != spawnedOutside)
            {
                SetEnemyOutside(spawnedOutside);
            }
            ConfigureTraversal();

            _baseAgentRadius = agent != null ? agent.radius : (isOutside ? 0.85f : 0.55f);
            SyncBestiaryScanNodeId();

            MahoragaConfig? cfg = MahoragaConfig.Current;
            enemyHP = cfg != null ? cfg.MaxHP.Value : 18;
            openDoorSpeedMultiplier = 1.5f;
            AIIntervalTime = 0.2f;
            syncMovementSpeed = 0.22f;
            _lastFramePosition = transform.position;

            ApplyVisualGlow(0.4f);

            if (IsServer)
            {
                currentBehaviourStateIndex = (int)MahoragaState.Patrolling;
                currentBehaviourState = enemyBehaviourStates[0];
                EnsureAgentOnNavMeshServer();
                InitShadowAmbushServer();

                // Spawn = summoning sequence (state 5); it hands over to patrol/hunt when it ends.
                if (!TryBeginSummonServer(cfg))
                {
                    StartSearch(transform.position);
                }
            }
            else if (NetworkObject != null && NetworkObject.IsSpawned)
            {
                RequestFullSyncServerRpc();
            }
        }

        private void SyncBestiaryScanNodeId()
        {
            TerminalNode? bestiaryNode = Plugin.BestiaryNode;
            if (bestiaryNode == null || bestiaryNode.creatureFileID < 0)
            {
                return;
            }

            ScanNodeProperties[] scanNodes = GetComponentsInChildren<ScanNodeProperties>(includeInactive: true);
            for (int i = 0; i < scanNodes.Length; i++)
            {
                ScanNodeProperties node = scanNodes[i];
                if (node != null && node.creatureScanID < 0)
                {
                    node.creatureScanID = bestiaryNode.creatureFileID;
                }
            }
        }

        private void EnsureBehaviourStatesArray()
        {
            const int StateCount = 8;
            if (enemyBehaviourStates != null && enemyBehaviourStates.Length >= StateCount)
            {
                return;
            }

            string[] names =
            {
                "Patrolling",
                "Investigating",
                "Chasing",
                "Attacking",
                "Adapting",
                "Summoning",
                "EngagingEnemy",
                "Submerging"
            };
            var states = new EnemyBehaviourState[StateCount];
            for (int i = 0; i < StateCount; i++)
            {
                states[i] = (enemyBehaviourStates != null && i < enemyBehaviourStates.Length && enemyBehaviourStates[i] != null)
                    ? enemyBehaviourStates[i]
                    : new EnemyBehaviourState { name = names[i] };
            }
            enemyBehaviourStates = states;
        }

        /// <summary>
        /// Anti-clipping replacement for EnemyAI.SetDestinationToPosition: samples onto the agent's walkable
        /// areaMask and validates the path before writing destination. Never writes raw off-mesh coordinates.
        /// </summary>
        private bool SetSafeDestinationServer(Vector3 rawTarget, float sampleRadius = 4f)
        {
            if (agent == null || !agent.enabled || !agent.isOnNavMesh)
            {
                return false;
            }

            if (TryBoardOpenShipServer(rawTarget))
            {
                return true;
            }
            if (TryGetShipApproachPoint(rawTarget, out Vector3 approach))
            {
                rawTarget = approach;
                sampleRadius = 0.75f;
            }
            float radius = Mathf.Max(0.5f, sampleRadius);
            int areaMask = agent.areaMask != 0 ? agent.areaMask : agentMask;
            if (!NavMesh.SamplePosition(rawTarget, out NavMeshHit hit, radius, areaMask))
            {
                return false;
            }

            if (!agent.CalculatePath(hit.position, _safePath))
            {
                return false;
            }

            if (_safePath.status == NavMeshPathStatus.PathComplete)
            {
                moveTowardsDestination = true;
                movingTowardsTargetPlayer = false;
                destination = hit.position;
                return true;
            }

            if (_safePath.status == NavMeshPathStatus.PathPartial)
            {
                int cornerCount = _safePath.GetCornersNonAlloc(_safePathCorners);
                if (cornerCount > 0)
                {
                    Vector3 lastCorner = _safePathCorners[cornerCount - 1];
                    if ((lastCorner - transform.position).sqrMagnitude < 0.95f * 0.95f &&
                        TryBeginJumpServer(transform.position, hit.position, useLink: false))
                    {
                        return true;
                    }

                    if ((lastCorner - transform.position).sqrMagnitude > 0.4f * 0.4f)
                    {
                        moveTowardsDestination = true;
                        movingTowardsTargetPlayer = false;
                        destination = lastCorner;
                        return true;
                    }
                }
            }

            return false;
        }

        public override void NavigateTowardsTargetPlayer()
        {
            if (targetPlayer == null || agent == null || !agent.enabled || !agent.isOnNavMesh)
            {
                return;
            }

            if (setDestinationToPlayerInterval > 0f)
            {
                setDestinationToPlayerInterval -= Time.deltaTime;
                return;
            }

            setDestinationToPlayerInterval = 0.2f;

            int areaMask = agent.areaMask != 0 ? agent.areaMask : agentMask;
            if (!NavMesh.SamplePosition(targetPlayer.transform.position, out NavMeshHit hit, 2.5f, areaMask))
            {
                return;
            }

            Vector3 sampled = hit.position;
            if (IsSeparatedByMineshaftElevator(sampled) &&
                RoundManager.Instance != null &&
                RoundManager.Instance.currentMineshaftElevator != null)
            {
                Vector3 elevatorTarget = RoundManager.Instance.currentMineshaftElevator.elevatorMovingDown
                    ? RoundManager.Instance.currentMineshaftElevator.elevatorBottomPointDoor.position
                    : RoundManager.Instance.currentMineshaftElevator.elevatorTopPoint.position;
                if (!NavMesh.SamplePosition(elevatorTarget, out NavMeshHit elevHit, 2.5f, areaMask))
                {
                    return;
                }
                sampled = elevHit.position;
            }

            if (!agent.CalculatePath(sampled, _safePath))
            {
                return;
            }

            if (_safePath.status == NavMeshPathStatus.PathComplete)
            {
                moveTowardsDestination = true;
                destination = sampled;
            }
            else if (_safePath.status == NavMeshPathStatus.PathPartial)
            {
                int cornerCount = _safePath.GetCornersNonAlloc(_safePathCorners);
                if (cornerCount > 0)
                {
                    Vector3 lastCorner = _safePathCorners[cornerCount - 1];
                    if ((lastCorner - transform.position).sqrMagnitude > 0.4f * 0.4f)
                    {
                        moveTowardsDestination = true;
                        destination = lastCorner;
                    }
                }
            }
        }

        private void ResolvePrefabReferences()
        {
            if (audioController == null)
            {
                audioController = GetComponent<MahoragaAudioController>();
            }

            if (mainRenderer == null)
            {
                mainRenderer = GetComponentInChildren<SkinnedMeshRenderer>(includeInactive: true);
            }

            if (mainRenderer != null)
            {
                Material[] materials = mainRenderer.sharedMaterials;
                for (int i = 0; i < materials.Length; i++)
                {
                    if (materials[i] != null && materials[i].name == "Mahoraga_Wheel_Mat")
                    {
                        _wheelMaterialIndex = i;
                        break;
                    }
                }
            }

            if (wheelBone == null)
            {
                wheelBone = FindDescendantByName(transform, "WheelBone");
            }

            if (swordBone == null)
            {
                swordBone = FindDescendantByName(transform, "SwordBone");
            }

            if (wheelGlowLight == null && wheelBone != null)
            {
                wheelGlowLight = wheelBone.GetComponentInChildren<Light>(includeInactive: true);
            }

            if (eye == null)
            {
                eye = FindDescendantByName(transform, "EyeTransform");
            }
        }

        private void CacheWheelRestRotation()
        {
            if (!_hasWheelRestRotation && wheelBone != null)
            {
                _wheelRestLocalRotation = wheelBone.localRotation;
                _hasWheelRestRotation = true;
            }
        }

        private static Transform? FindDescendantByName(Transform root, string targetName)
        {
            for (int i = 0; i < root.childCount; i++)
            {
                Transform child = root.GetChild(i);
                if (string.Equals(child.name, targetName, StringComparison.Ordinal))
                {
                    return child;
                }

                Transform? found = FindDescendantByName(child, targetName);
                if (found != null)
                {
                    return found;
                }
            }
            return null;
        }

        public override void Update()
        {
            base.Update();

            float dt = Time.deltaTime;
            if (IsServer)
            {
                TickJumpServer(dt);
                TickShipDeckServer();
            }
            if (_clientContactCooldown > 0f)
            {
                _clientContactCooldown -= dt;
            }

            // Enforce ZapGun / Adapting / Summon-Submerge / Dead stun immunity on ALL clients (handles PatcherTool's post-SetEnemyStunned stunnedIndefinitely++).
            bool adaptingNow = currentBehaviourStateIndex == (int)MahoragaState.Adapting;
            bool summonOrSubmergeNow = IsSummonOrSubmergeActive;
            if (isEnemyDead || adaptingNow || summonOrSubmergeNow || _adaptation.IsAdapted(AdaptationCategory.ZapGun))
            {
                if (stunnedIndefinitely > 0)
                {
                    stunnedIndefinitely = 0;
                }
                if ((adaptingNow || isEnemyDead || summonOrSubmergeNow) && stunNormalizedTimer >= 0f)
                {
                    stunNormalizedTimer = -1f;
                }
            }

            UpdateClientPresentation(dt);
            UpdateSummon(dt);
            UpdateSubmerge(dt);
            TickScale(dt);

            if (IsServer && isEnemyDead)
            {
                // An Old Bird whose destruction already started must still be despawned if Mahoraga dies meanwhile.
                TickPendingOldBirdDestructions(Time.time);
            }

            if (!IsServer || isEnemyDead || _jumping)
            {
                return;
            }

            TickServerTimersAndActiveStates(dt);
        }

        private void LateUpdate()
        {
            if (wheelBone != null)
            {
                if (!_hasWheelRestRotation)
                {
                    CacheWheelRestRotation();
                }

                // Deterministic cumulative +45° per turn around WheelBone local Y (hub normal).
                wheelBone.localRotation = _wheelRestLocalRotation * Quaternion.Euler(0f, _wheelDisplayAngle + _summonWheelExtraAngle, 0f);
            }
        }

        private void UpdateClientPresentation(float dt)
        {
            // Horizontal displacement for client animator Speed & stride footsteps.
            Vector3 currentPos = transform.position;
            Vector3 delta = currentPos - _lastFramePosition;
            delta.y = 0f;
            float horizDist = delta.magnitude;
            _lastFramePosition = currentPos;

            float instantSpeed;
            if (IsServer && agent != null && agent.enabled)
            {
                Vector3 vel = agent.velocity;
                vel.y = 0f;
                instantSpeed = vel.magnitude;
            }
            else
            {
                instantSpeed = dt > 0.0001f ? horizDist / dt : 0f;
            }

            _smoothedHorizontalSpeed = Mathf.Lerp(_smoothedHorizontalSpeed, instantSpeed, 1f - Mathf.Exp(-12f * dt));

            bool summonOrSubmerge = IsSummonOrSubmergeActive;
            bool isStunned = (stunNormalizedTimer > 0f || stunnedIndefinitely > 0)
                && !isEnemyDead
                && !summonOrSubmerge
                && currentBehaviourStateIndex != (int)MahoragaState.Adapting;

            bool isChasing = !isEnemyDead && !summonOrSubmerge &&
                (currentBehaviourStateIndex == (int)MahoragaState.Chasing ||
                 currentBehaviourStateIndex == (int)MahoragaState.Attacking ||
                 currentBehaviourStateIndex == (int)MahoragaState.EngagingEnemy);

            if (creatureAnimator != null)
            {
                creatureAnimator.SetFloat(SpeedHash, (isEnemyDead || summonOrSubmerge) ? 0f : _smoothedHorizontalSpeed);
                creatureAnimator.SetBool(IsChasingHash, isChasing);
                creatureAnimator.SetBool(IsStunnedHash, isStunned);
                creatureAnimator.SetBool(IsDeadHash, isEnemyDead);
            }

            if (!isEnemyDead && !isStunned && !summonOrSubmerge && currentBehaviourStateIndex != (int)MahoragaState.Adapting)
            {
                if (Time.time >= _stopStridesUntil && _smoothedHorizontalSpeed > 0.35f)
                {
                    audioController?.TickStrideFootsteps(horizDist, isChasing);
                }
            }
            else
            {
                audioController?.ResetStride();
            }

            UpdateWheelSpinAnimation(dt);
            UpdateEmissiveFlash(dt);
        }

        private void UpdateWheelSpinAnimation(float dt)
        {
            if (!_wheelSpinActive)
            {
                return;
            }

            _wheelSpinElapsed += dt;
            float spinTime = _wheelSpinElapsed - _wheelSpinDelay;
            if (spinTime <= 0f)
            {
                return;
            }

            float u = Mathf.Clamp01(spinTime / _wheelSpinDuration);
            // Smooth mechanical cubic ease-out for heavy Dharma wheel click.
            float eased = 1f - Mathf.Pow(1f - u, 3f);
            _wheelDisplayAngle = Mathf.Lerp(_wheelSpinStartAngle, _wheelSpinTargetAngle, eased);

            if (u >= 1f)
            {
                _wheelDisplayAngle = _wheelSpinTargetAngle;
                _wheelSpinActive = false;
            }
        }

        private void UpdateEmissiveFlash(float dt)
        {
            if (isEnemyDead)
            {
                ApplyVisualGlow(0f);
                return;
            }

            float baseIdleGlow = 0.4f + Mathf.Min(_adaptation.WheelStepCount, 12) * 0.14f;

            if (_emissiveDelayTimer > 0f)
            {
                _emissiveDelayTimer -= dt;
                ApplyVisualGlow(baseIdleGlow);
                return;
            }

            if (_emissiveFlashTimer > 0f)
            {
                _emissiveFlashTimer = Mathf.Max(0f, _emissiveFlashTimer - dt);
                float t = _emissiveFlashDuration > 0.001f ? (_emissiveFlashTimer / _emissiveFlashDuration) : 0f;
                float intensity = Mathf.Lerp(baseIdleGlow, _emissivePeakIntensity, t * t);
                ApplyVisualGlow(intensity);
            }
            else
            {
                ApplyVisualGlow(baseIdleGlow);
            }
        }

        private void ApplyVisualGlow(float emissiveIntensity)
        {
            if (mainRenderer != null && _propBlock != null)
            {
                if (_wheelMaterialIndex >= 0)
                {
                    mainRenderer.GetPropertyBlock(_propBlock, _wheelMaterialIndex);
                    _propBlock.SetColor(EmissiveColorId, BaseGoldColor * emissiveIntensity);
                    mainRenderer.SetPropertyBlock(_propBlock, _wheelMaterialIndex);
                }
                else
                {
                    // Old bundles had one material and no body light floor.
                    mainRenderer.GetPropertyBlock(_propBlock);
                    _propBlock.SetColor(EmissiveColorId, BaseGoldColor * emissiveIntensity);
                    mainRenderer.SetPropertyBlock(_propBlock);
                }
            }

            if (wheelGlowLight != null)
            {
                bool lightOn = !isEnemyDead && emissiveIntensity > 1.5f;
                wheelGlowLight.enabled = lightOn;
                if (lightOn)
                {
                    float norm = Mathf.Clamp01((emissiveIntensity - 1.5f) / 58.5f);
                    wheelGlowLight.intensity = norm * 450f;
                }
            }
        }

        private void TickServerTimersAndActiveStates(float dt)
        {
            if (IsSummonOrSubmergeActive)
            {
                TickPendingOldBirdDestructions(Time.time);
                return;
            }

            TickCombatServer(dt);

            if (_roarCooldownTimer > 0f)
            {
                _roarCooldownTimer -= dt;
            }

            if (_attackCooldownTimer > 0f)
            {
                _attackCooldownTimer -= dt;
            }

            if (_deflectRpcCooldownTimer > 0f)
            {
                _deflectRpcCooldownTimer -= dt;
            }

            // Sustained ZapGun beam exposure evaluation while PatcherTool holds stunnedIndefinitely > 0.
            if (stunnedIndefinitely > 0 && currentBehaviourStateIndex != (int)MahoragaState.Adapting)
            {
                MahoragaConfig? cfg = MahoragaConfig.Current;
                int maxHp = cfg != null ? cfg.MaxHP.Value : 18;
                AdaptationSettings settings = cfg != null ? cfg.Adaptation : _fallbackAdaptation;

                StunEvaluation beamEval = _adaptation.EvaluateSustainedZapBeam(
                    enemyHP,
                    maxHp,
                    isCurrentlyAdapting: false,
                    settings,
                    Time.time);

                if (beamEval.TriggeredWheelClick)
                {
                    TriggerWheelClickClientRpc((byte)beamEval.Category, (byte)beamEval.StageAfter, _adaptation.AdaptedMask, _adaptation.WheelStepCount, enemyHP, playSpinAnim: false);
                }

                if (beamEval.TriggeredAdaptation)
                {
                    EnterAdaptationStateServer(AdaptationCategory.ZapGun, beamEval.FinalHP);
                    return;
                }
            }

            // Adapting state (State 4): locked stance for 2.20s.
            if (currentBehaviourStateIndex == (int)MahoragaState.Adapting)
            {
                if (agent != null && agent.enabled)
                {
                    agent.speed = 0f;
                }

                _adaptationElapsed += dt;
                if (_adaptationElapsed >= AdaptationDuration)
                {
                    ResumeAfterAdaptationServer();
                }
                return;
            }

            // Pause attacks/door breach while actively stunned.
            if (stunNormalizedTimer > 0f || stunnedIndefinitely > 0)
            {
                if (agent != null && agent.enabled)
                {
                    agent.speed = 0f;
                }
                return;
            }

            // Locked door forcing sequence.
            if (_forcingDoor != null)
            {
                TickLockedDoorBreachServer(dt);
                return;
            }

            // Active Attacking state (State 3).
            if (currentBehaviourStateIndex == (int)MahoragaState.Attacking)
            {
                TickAttackStateServer(dt);
                return;
            }

            // Investigating timers.
            if (currentBehaviourStateIndex == (int)MahoragaState.Investigating)
            {
                _investigateTimer -= dt;
                if (_investigateArrived)
                {
                    _investigateArrivalTimer -= dt;
                }
            }
        }

        public override void DoAIInterval()
        {
            if (!IsServer || isEnemyDead || StartOfRound.Instance == null || StartOfRound.Instance.allPlayersDead)
            {
                return;
            }

            // Summoning / Submerging: motionless & invulnerable until the sequence finishes.
            if (IsSummonOrSubmergeActive)
            {
                return;
            }

            if (_jumping || _onShipDeck)
            {
                return;
            }
            GuardOffMeshLinkServer();
            if (_jumping)
            {
                return;
            }
            base.DoAIInterval();

            if (currentBehaviourStateIndex == (int)MahoragaState.Adapting ||
                currentBehaviourStateIndex == (int)MahoragaState.Attacking)
            {
                return;
            }

            if (stunNormalizedTimer > 0f || stunnedIndefinitely > 0)
            {
                if (agent != null && agent.enabled)
                {
                    agent.speed = 0f;
                }
                return;
            }

            if (_forcingDoor != null)
            {
                return;
            }

            MahoragaConfig? cfg = MahoragaConfig.Current;

            if (currentBehaviourStateIndex == (int)MahoragaState.Patrolling ||
                currentBehaviourStateIndex == (int)MahoragaState.Investigating ||
                currentBehaviourStateIndex == (int)MahoragaState.Chasing)
            {
                // Old Bird #1 Priority overrides player chase/retaliation on the same tick.
                if (EvaluateEnemyEngagementServer(cfg))
                {
                    return;
                }

                if (currentBehaviourStateIndex != (int)MahoragaState.Chasing)
                {
                    PlayerControllerB? avenger = GetRetaliationPlayer();
                    if (avenger != null)
                    {
                        EnterChaseStateServer(avenger);
                        return;
                    }
                }
            }

            switch ((MahoragaState)currentBehaviourStateIndex)
            {
                case MahoragaState.Patrolling:
                    RunPatrolInterval(cfg);
                    break;

                case MahoragaState.EngagingEnemy:
                    RunEngageEnemyInterval(cfg);
                    break;

                case MahoragaState.Investigating:
                    RunInvestigateInterval(cfg);
                    break;

                case MahoragaState.Chasing:
                    RunChaseInterval(cfg);
                    break;
            }
        }

        private void RunPatrolInterval(MahoragaConfig? cfg)
        {
            if (agent != null && agent.enabled)
            {
                agent.speed = cfg != null ? cfg.PatrolSpeed.Value : 3.2f;
            }
            openDoorSpeedMultiplier = 0.85f;

            PlayerControllerB? nearby = FindNearbyPlayer();
            if (nearby != null)
            {
                EnterChaseStateServer(nearby);
                return;
            }

            if (TryPatrolGapServer() || TryEvaluateShadowAmbushServer(cfg))
            {
                return;
            }

            if (currentSearch == null || !currentSearch.inProgress)
            {
                StartSearch(transform.position);
            }

            TryScanForLockedDoorServer(cfg);
        }

        private void RunInvestigateInterval(MahoragaConfig? cfg)
        {
            if (agent != null && agent.enabled)
            {
                agent.speed = _investigateArrived ? 0f : (cfg != null ? cfg.InvestigateSpeed.Value : 4.5f);
            }
            openDoorSpeedMultiplier = 1.1f;

            PlayerControllerB? nearby = FindNearbyPlayer();
            if (nearby != null)
            {
                EnterChaseStateServer(nearby);
                return;
            }

            if (TryEvaluateShadowAmbushServer(cfg))
            {
                return;
            }

            float distToPoint = Vector3.Distance(transform.position, _investigatePos);
            if (!_investigateArrived && distToPoint <= 2.2f)
            {
                _investigateArrived = true;
                _investigateArrivalTimer = InvestigateArrivalPause;
                moveTowardsDestination = false;
            }
            else if (!_investigateArrived)
            {
                if (!SetSafeDestinationServer(_investigatePos, 5f))
                {
                    _investigateArrived = true;
                    _investigateArrivalTimer = Mathf.Min(_investigateArrivalTimer, 0.4f);
                    moveTowardsDestination = false;
                }
                else
                {
                    TryScanForLockedDoorServer(cfg);
                }
            }

            if (_investigateTimer <= 0f || (_investigateArrived && _investigateArrivalTimer <= 0f))
            {
                EnterPatrolStateServer();
            }
        }

        private void RunChaseInterval(MahoragaConfig? cfg)
        {
            float baseChase = cfg != null ? cfg.ChaseSpeed.Value : 6.2f;
            float bonusPerAdapt = cfg != null ? cfg.ChaseSpeedPerAdaptation.Value : 0.35f;
            float escalatedSpeed = baseChase + Mathf.Min(4, _adaptation.AdaptedCategoryCount) * bonusPerAdapt;

            if (agent != null && agent.enabled)
            {
                agent.speed = escalatedSpeed;
            }
            openDoorSpeedMultiplier = 1.65f;

            if (targetPlayer == null || !PlayerIsTargetable(targetPlayer))
            {
                EnterInvestigateStateServer(_lastKnownTargetPos);
                return;
            }

            TryRetaliationRetargetServer(cfg);

            PlayerControllerB player = targetPlayer!;
            bool inContact = CanSenseByProximity(player);
            if (inContact)
            {
                RememberPlayerContact(player.transform.position);
            }

            float sinceContact = Time.time - _lastPlayerContactTime;
            if (sinceContact >= HearingMemorySeconds ||
                (sinceContact > 0.8f && Vector3.Distance(transform.position, _lastKnownTargetPos) < 0.85f) ||
                !SetSafeDestinationServer(_lastKnownTargetPos, 1.5f))
            {
                EnterInvestigateStateServer(_lastKnownTargetPos);
                return;
            }

            if (_jumping || TryScanForLockedDoorServer(cfg))
            {
                return;
            }

            // Attack range evaluation (including railing/ledge Divergent Slam punish; requires clear strike path so never swings into walls).
            if (_attackCooldownTimer <= 0f && (inContact || (sinceContact < 0.8f &&
                (player.transform.position - _lastKnownTargetPos).sqrMagnitude < 1f)))
            {
                Vector3 toTarget = targetPlayer.transform.position - transform.position;
                float horizDist = new Vector2(toTarget.x, toTarget.z).magnitude;
                float vertDelta = Mathf.Abs(toTarget.y);

                float sc = ScaleFactor;
                bool inStandardRange = horizDist <= AttackEntryRange * sc && vertDelta <= VerticalHitTolerance * sc;
                bool inRailingPunishRange = !inStandardRange
                    && horizDist <= (SlamReach - 0.15f) * sc
                    && vertDelta <= VerticalHitTolerance * sc
                    && agent != null
                    && agent.enabled
                    && agent.isOnNavMesh
                    && (agent.pathStatus == NavMeshPathStatus.PathPartial || agent.remainingDistance <= 0.75f);

                if ((inStandardRange || inRailingPunishRange) && HasClearStrikePathToPlayer(targetPlayer))
                {
                    bool useSlam = ShouldUseSlamAttack(inRailingPunishRange);
                    EnterAttackStateServer(useSlam);
                }
            }
        }

        private bool ShouldUseSlamAttack(bool forceSlam)
        {
            if (forceSlam)
            {
                return true;
            }

            int nearbyCount = CountTargetablePlayersWithin(3.8f);
            if (nearbyCount >= 2)
            {
                return true;
            }

            if (_attacksSinceLastSlam >= 2)
            {
                return true;
            }

            float slamChance = _adaptation.AdaptedCategoryCount >= 1 ? 0.45f : 0.30f;
            return UnityEngine.Random.value < slamChance;
        }

        private int CountTargetablePlayersWithin(float radius)
        {
            if (StartOfRound.Instance == null)
            {
                return 0;
            }

            float radiusSq = radius * radius;
            int count = 0;
            PlayerControllerB[] players = StartOfRound.Instance.allPlayerScripts;
            for (int i = 0; i < players.Length; i++)
            {
                PlayerControllerB p = players[i];
                if (p == null || !PlayerIsTargetable(p))
                {
                    continue;
                }

                if ((p.transform.position - transform.position).sqrMagnitude <= radiusSq)
                {
                    count++;
                }
            }
            return count;
        }

        private void EnterPatrolStateServer()
        {
            _forcingDoor = null;
            targetPlayer = null;
            movingTowardsTargetPlayer = false;

            SwitchBehaviourAndSyncServer(MahoragaState.Patrolling, -1);
            StartSearch(transform.position);
        }

        private void EnterInvestigateStateServer(Vector3 targetPos)
        {
            _forcingDoor = null;
            StopSearch(currentSearch);
            targetPlayer = null;
            movingTowardsTargetPlayer = false;

            _investigatePos = targetPos;
            _investigateTimer = InvestigateTimeout;
            _investigateArrived = false;
            _investigateArrivalTimer = InvestigateArrivalPause;

            SwitchBehaviourAndSyncServer(MahoragaState.Investigating, -1);
            if (!SetSafeDestinationServer(_investigatePos, 5f))
            {
                _investigateArrived = true;
                _investigateArrivalTimer = 0.4f;
                moveTowardsDestination = false;
            }
        }

        private void EnterChaseStateServer(PlayerControllerB player, Vector3? contactPosition = null)
        {
            _forcingDoor = null;
            StopSearch(currentSearch);
            targetPlayer = player;
            RememberPlayerContact(contactPosition ?? player.transform.position);
            setDestinationToPlayerInterval = 0f;
            moveTowardsDestination = true;
            RecordPlayerContactServer();

            SwitchBehaviourAndSyncServer(MahoragaState.Chasing, (int)player.playerClientId);
            SetSafeDestinationServer(_lastKnownTargetPos, 1.5f);
        }

        private void SetChaseTargetServer(PlayerControllerB player)
        {
            targetPlayer = player;
            RememberPlayerContact(player.transform.position);
            setDestinationToPlayerInterval = 0f;
            moveTowardsDestination = true;
            RecordPlayerContactServer();
            SyncBehaviourStateClientRpc((int)MahoragaState.Chasing, (int)player.playerClientId, playRoar: false);
            SetSafeDestinationServer(_lastKnownTargetPos, 1.5f);
        }

        private void EnterAttackStateServer(bool isSlam, EnemyAI? enemyTarget = null)
        {
            StopSearch(currentSearch);
            _attackEnemyTarget = enemyTarget;
            _currentAttackIsSlam = isSlam;
            _attackElapsed = 0f;
            _attackHitResolved = false;
            _attackSlashPlayed = false;
            _attacksSinceLastSlam = isSlam ? 0 : (_attacksSinceLastSlam + 1);

            int targetId = enemyTarget == null && targetPlayer != null ? (int)targetPlayer.playerClientId : -1;
            SwitchBehaviourAndSyncServer(MahoragaState.Attacking, targetId);
            TriggerAttackClientRpc(isSlam, isDoorBash: false);
        }

        private void TickAttackStateServer(float dt)
        {
            _attackElapsed += dt;

            float windup = _currentAttackIsSlam ? SlamWindupTime : CleaveWindupTime;
            float duration = _currentAttackIsSlam ? SlamDuration : CleaveDuration;

            if (agent != null && agent.enabled)
            {
                agent.speed = _attackElapsed < windup ? 1.2f : 0f;
            }

            // Track target smoothly during windup.
            if (_attackElapsed < windup)
            {
                if (_attackEnemyTarget != null && !_attackEnemyTarget.isEnemyDead)
                {
                    FacePositionSmooth(_attackEnemyTarget.transform.position, 9f, dt);
                }
                else if (targetPlayer != null && PlayerIsTargetable(targetPlayer))
                {
                    FacePositionSmooth(_lastKnownTargetPos, 9f, dt);
                }
            }

            // Cleave slash audio at t = 0.20s.
            if (!_currentAttackIsSlam && !_attackSlashPlayed && _attackElapsed >= 0.20f)
            {
                _attackSlashPlayed = true;
            }

            if (!_attackHitResolved && _attackElapsed >= windup)
            {
                _attackHitResolved = true;
                ResolveAttackHitServer(_currentAttackIsSlam);
            }

            if (_attackElapsed >= duration)
            {
                _attackCooldownTimer = PostAttackCooldown;
                if (_attackEnemyTarget != null && TryReturnToEnemyEngagementAfterAttack())
                {
                    return;
                }

                _attackEnemyTarget = null;
                PlayerControllerB? followUp = ChoosePlayerAfterAction(targetPlayer);
                if (followUp != null)
                {
                    EnterChaseStateServer(followUp);
                }
                else
                {
                    PlayerControllerB? next = FindNearbyPlayer();
                    if (next != null)
                    {
                        EnterChaseStateServer(next);
                    }
                    else
                    {
                        EnterPatrolStateServer();
                    }
                }
            }
        }

        private void ResolveAttackHitServer(bool isSlam)
        {
            if (StartOfRound.Instance == null)
            {
                return;
            }

            MahoragaConfig? cfg = MahoragaConfig.Current;
            int damage = isSlam
                ? (cfg != null ? cfg.SlamDamage.Value : 65)
                : (cfg != null ? cfg.CleaveDamage.Value : 55);
            CauseOfDeath cause = isSlam ? CauseOfDeath.Crushing : CauseOfDeath.Mauling;
            float scale = ScaleFactor;
            float reach = (isSlam ? SlamReach : CleaveReach) * scale;
            float vertTolerance = VerticalHitTolerance * scale;
            float knockMult = (cfg != null ? cfg.PlayerKnockbackMultiplier.Value : 1f) * Mathf.Sqrt(scale);

            bool anyHit = false;
            Vector3 impactOrigin = transform.position + transform.forward * (isSlam ? 0.6f : 1.4f) * scale + Vector3.up * 1.4f * scale;
            Vector3 rayOrigin = transform.position + Vector3.up * 1.8f * scale;

            PlayerControllerB[] players = StartOfRound.Instance.allPlayerScripts;
            for (int i = 0; i < players.Length; i++)
            {
                PlayerControllerB p = players[i];
                if (p == null || !PlayerIsTargetable(p))
                {
                    continue;
                }

                Vector3 toPlayer = p.transform.position - transform.position;
                float vertDelta = Mathf.Abs(toPlayer.y);
                if (vertDelta > vertTolerance)
                {
                    continue;
                }

                Vector3 flatToPlayer = new Vector3(toPlayer.x, 0f, toPlayer.z);
                float horizDist = flatToPlayer.magnitude;
                if (horizDist > reach)
                {
                    continue;
                }

                if (!isSlam && horizDist > 0.35f)
                {
                    float angle = Vector3.Angle(transform.forward, flatToPlayer);
                    if (angle > CleaveHalfArcDeg)
                    {
                        continue;
                    }
                }

                // Wall LOS check so attacks never clip through solid facility walls (chest, head or feet must be exposed).
                if (!HasClearStrikePathToPlayer(p))
                {
                    continue;
                }

                Vector3 knockDir = horizDist > 0.05f ? flatToPlayer.normalized : transform.forward;
                // Applied by the victim's client through externalForceAutoFade (decays at 2/s: distance ~ speed / 2).
                Vector3 knockback = (knockDir * (isSlam ? 14f : 9f) + Vector3.up * (isSlam ? 3f : 1.5f)) * knockMult;

                ApplyPlayerDamageClientRpc((int)p.playerClientId, damage, (int)cause, knockback);
                anyHit = true;
            }

            // Enemies that are hostile to us (current target, recent aggressors, Old Birds being hunted) take the same swing.
            if (ResolveEnemyHitsServer(isSlam, reach, rayOrigin, cfg))
            {
                anyHit = true;
            }

            if (isSlam || anyHit)
            {
                PlayAttackImpactClientRpc(isSlam, impactOrigin);
            }
        }

        private bool TryScanForLockedDoorServer(MahoragaConfig? cfg)
        {
            bool canForceLocked = cfg == null || cfg.CanForceLockedDoors.Value;

            Vector3 probeCenter = transform.position + transform.forward * 1.4f + Vector3.up * 1.5f;
            int hits = Physics.OverlapSphereNonAlloc(probeCenter, 1.6f, _doorOverlapBuffer, InteractableLayerMask, QueryTriggerInteraction.Collide);
            bool brakeForClosedDoor = false;

            for (int i = 0; i < hits; i++)
            {
                Collider col = _doorOverlapBuffer[i];
                if (col == null)
                {
                    continue;
                }

                DoorLock? door = col.GetComponent<DoorLock>() ?? col.GetComponentInParent<DoorLock>();
                if (door == null)
                {
                    continue;
                }

                Vector3 toDoor = door.transform.position - transform.position;
                toDoor.y = 0f;
                float pivotDist = toDoor.magnitude;

                Vector3 closestEdge = col.ClosestPoint(transform.position + Vector3.up * 1.2f);
                Vector3 toEdge = closestEdge - transform.position;
                toEdge.y = 0f;
                float edgeDist = toEdge.magnitude;

                // Confirm door lies ahead of Mahoraga's approach vector (unless already flush against it).
                Vector3 aheadVec = toEdge.sqrMagnitude > 0.04f ? toEdge : toDoor;
                if (aheadVec.sqrMagnitude > 0.04f && Vector3.Dot(transform.forward, aheadVec.normalized) < 0.25f)
                {
                    continue;
                }

                if (door.isLocked)
                {
                    if (canForceLocked && pivotDist <= LockedDoorScanRange)
                    {
                        _forcingDoor = door;
                        _doorForceElapsed = 0f;
                        _doorBashStage = 0;
                        return true;
                    }
                    continue;
                }

                // Closed unlocked door brake: slow down so DoorLock.OnTriggerStay opens it before Mahoraga clips through.
                if (Mathf.Min(pivotDist, edgeDist) <= 1.8f &&
                    door.navMeshObstacle != null &&
                    door.navMeshObstacle.enabled)
                {
                    brakeForClosedDoor = true;
                }
            }

            if (brakeForClosedDoor && agent != null && agent.enabled)
            {
                agent.speed = Mathf.Min(agent.speed, 0.8f);
            }

            return false;
        }

        private void TickLockedDoorBreachServer(float dt)
        {
            if (_forcingDoor == null || !_forcingDoor.isLocked)
            {
                _forcingDoor = null;
                return;
            }

            if (agent != null && agent.enabled)
            {
                agent.speed = 0f;
            }

            FacePositionSmooth(_forcingDoor.transform.position, 8f, dt);
            _doorForceElapsed += dt;

            if (_doorBashStage == 0 && _doorForceElapsed >= 0.4f)
            {
                _doorBashStage = 1;
                TriggerAttackClientRpc(isSlam: false, isDoorBash: true);
                PlayAttackImpactClientRpc(isSlam: false, _forcingDoor.transform.position);
            }
            else if (_doorBashStage == 1 && _doorForceElapsed >= 1.6f)
            {
                _doorBashStage = 2;
                TriggerAttackClientRpc(isSlam: false, isDoorBash: true);
                PlayAttackImpactClientRpc(isSlam: false, _forcingDoor.transform.position);
            }

            if (_doorForceElapsed >= LockedDoorForceDuration)
            {
                DoorLock door = _forcingDoor;
                _forcingDoor = null;

                door.UnlockDoorSyncWithServer();
                AnimatedObjectTrigger? animTrigger = door.GetComponent<AnimatedObjectTrigger>();
                if (animTrigger != null)
                {
                    animTrigger.TriggerAnimationNonPlayer(useSecondaryAudiosOnAnimatedObjects, overrideBool: true);
                }
                door.OpenDoorAsEnemyServerRpc();
                PlayAttackImpactClientRpc(isSlam: true, door.transform.position);
            }
        }

        private void EnterAdaptationStateServer(AdaptationCategory category, int newHP)
        {
            _forcingDoor = null;
            StopSearch(currentSearch);

            enemyHP = newHP;
            stunNormalizedTimer = -1f;
            stunnedIndefinitely = 0;
            _adaptationElapsed = 0f;

            currentBehaviourStateIndex = (int)MahoragaState.Adapting;
            if (enemyBehaviourStates != null && enemyBehaviourStates.Length > 4)
            {
                currentBehaviourState = enemyBehaviourStates[4];
            }

            if (agent != null && agent.enabled)
            {
                agent.speed = 0f;
            }

            TriggerAdaptationClientRpc(
                (byte)category,
                _adaptation.AdaptedMask,
                _adaptation.WheelStepCount,
                enemyHP);
        }

        private void ResumeAfterAdaptationServer()
        {
            _attackEnemyTarget = null;
            PlayerControllerB? preferred = ChoosePlayerAfterAction(targetPlayer);
            if (preferred != null)
            {
                EnterChaseStateServer(preferred);
                return;
            }

            PlayerControllerB? spotted = FindNearbyPlayer();
            if (spotted != null)
            {
                EnterChaseStateServer(spotted);
            }
            else
            {
                EnterPatrolStateServer();
            }
        }

        public override void DetectNoise(Vector3 noisePosition, float noiseLoudness, int timesPlayedInOneSpot = 0, int noiseID = 0)
        {
            if (!IsServer || isEnemyDead || _jumping || _onShipDeck || IsSummonOrSubmergeActive || stunNormalizedTimer > 0f ||
                stunnedIndefinitely > 0 || StartOfRound.Instance == null || timesPlayedInOneSpot > 15 ||
                noiseID == 675189 || noiseID == 546 || (noisePosition - transform.position).sqrMagnitude < 0.04f ||
                HuntLogic.IsOutsideAt(noisePosition.y, isOutside) != isOutside)
            {
                return;
            }

            PlayerControllerB? source = PlayerFootstepPatch.Source;
            if (source != null && (source.isCrouching || !PlayerIsTargetable(source)))
            {
                return;
            }

            int mask = StartOfRound.Instance.collidersAndRoomMaskAndDefault;
            if (Physics.Linecast(transform.position + Vector3.up * 0.8f, noisePosition + Vector3.up * 0.8f,
                mask, QueryTriggerInteraction.Ignore))
            {
                noiseLoudness *= 0.5f;
            }

            float range = noiseLoudness * 18f;
            if (noiseLoudness < 0.25f || (noisePosition - transform.position).sqrMagnitude > range * range)
            {
                return;
            }

            _lastAudiblePosition = noisePosition;
            _lastAudibleTime = Time.time;
            if (source != null && targetPlayer == source)
            {
                RememberPlayerContact(noisePosition);
            }

            if (currentBehaviourStateIndex == (int)MahoragaState.Patrolling ||
                currentBehaviourStateIndex == (int)MahoragaState.Investigating)
            {
                if (source != null)
                {
                    EnterChaseStateServer(source, noisePosition);
                }
                else if (!_investigateArrived && currentBehaviourStateIndex == (int)MahoragaState.Investigating &&
                         (_investigatePos - noisePosition).sqrMagnitude < 0.75f * 0.75f)
                {
                    _investigateTimer = InvestigateTimeout;
                }
                else
                {
                    EnterInvestigateStateServer(noisePosition);
                }
            }
        }

        public override void HitEnemy(int force = 1, PlayerControllerB? playerWhoHit = null, bool playHitSFX = false, int hitID = -1)
        {
            // Invulnerable while being summoned.
            if (IsSummonInvulnerable)
            {
                return;
            }

            // Suppress base PlayOneShot(hitEnemyVoiceSFX) null warnings; handle impact/deflect SFX cleanly ourselves.
            base.HitEnemy(force, playerWhoHit, playHitSFX: false, hitID);

            if (isEnemyDead || !IsServer)
            {
                return;
            }

            // Classification, progressive adaptation, retaliation and Old Bird rules live in MahoragaAI.Combat.cs.
            HandleDamageServer(force, playerWhoHit, playHitSFX, hitID);
        }

        public override void OnCollideWithPlayer(Collider other)
        {
            base.OnCollideWithPlayer(other);
            if (_clientContactCooldown > 0f || isEnemyDead || _jumping)
            {
                return;
            }

            if (currentBehaviourStateIndex == (int)MahoragaState.Adapting ||
                currentBehaviourStateIndex == (int)MahoragaState.Attacking ||
                IsSummonInvulnerable)
            {
                return;
            }

            PlayerControllerB? player = MeetsStandardPlayerCollisionConditions(other);
            if (player == null)
            {
                return;
            }

            _clientContactCooldown = 0.45f;
            RequestContactAttackServerRpc((int)player.playerClientId);
        }

        public override void KillEnemy(bool destroy = false)
        {
            bool hadDieSfx = creatureVoice != null && dieSFX != null;
            base.KillEnemy(destroy);

            if (_deathHandled)
            {
                return;
            }
            _deathHandled = true;

            _forcingDoor = null;
            StopSearch(currentSearch);
            ClearCombatState();
            EndSummonPresentation();
            EndSubmergePresentation();

            if (agent != null)
            {
                agent.enabled = false;
            }

            if (creatureAnimator != null)
            {
                creatureAnimator.SetBool(IsDeadHash, true);
                creatureAnimator.SetBool(IsStunnedHash, false);
                creatureAnimator.SetBool(IsChasingHash, false);
                creatureAnimator.SetFloat(SpeedHash, 0f);
            }

            // Disable hit/contact colliders on corpse.
            EnemyAICollisionDetect[] detectors = GetComponentsInChildren<EnemyAICollisionDetect>(includeInactive: true);
            for (int i = 0; i < detectors.Length; i++)
            {
                Collider? col = detectors[i].GetComponent<Collider>();
                if (col != null)
                {
                    col.enabled = false;
                }
            }

            ApplyVisualGlow(0f);
            audioController?.OnMahoragaDied(playDeathAudio: !hadDieSfx);
        }

        public override void OnDestroy()
        {
            ClearCombatState();
            EndSummonPresentation();
            EndSubmergePresentation();
            base.OnDestroy();
        }

        private void SwitchBehaviourAndSyncServer(MahoragaState newState, int targetPlayerClientId)
        {
            int stateIdx = (int)newState;
            bool wasChasing = currentBehaviourStateIndex == (int)MahoragaState.Chasing
                || currentBehaviourStateIndex == (int)MahoragaState.Attacking
                || currentBehaviourStateIndex == (int)MahoragaState.EngagingEnemy;

            bool playRoar = !wasChasing
                && newState == MahoragaState.Chasing
                && !isEnemyDead
                && _roarCooldownTimer <= 0f;

            if (playRoar)
            {
                _roarCooldownTimer = RoarCooldownSeconds;
            }

            previousBehaviourStateIndex = currentBehaviourStateIndex;
            currentBehaviourStateIndex = stateIdx;
            if (enemyBehaviourStates != null && stateIdx >= 0 && stateIdx < enemyBehaviourStates.Length)
            {
                currentBehaviourState = enemyBehaviourStates[stateIdx];
            }

            SyncBehaviourStateClientRpc(stateIdx, targetPlayerClientId, playRoar);
        }

        private PlayerControllerB? FindNearbyPlayer()
        {
            if (StartOfRound.Instance == null)
            {
                return null;
            }

            PlayerControllerB? nearest = null;
            float nearestDistance = TouchRange * TouchRange;
            foreach (PlayerControllerB player in StartOfRound.Instance.allPlayerScripts)
            {
                if (player == null || !CanSenseByProximity(player))
                {
                    continue;
                }

                float distance = (player.transform.position - transform.position).sqrMagnitude;
                if (distance <= nearestDistance)
                {
                    nearestDistance = distance;
                    nearest = player;
                }
            }
            return nearest;
        }

        private bool CanSenseByProximity(PlayerControllerB player)
        {
            return (player.transform.position - transform.position).sqrMagnitude <= TouchRange * TouchRange &&
                PlayerIsTargetable(player) && HasClearStrikePathToPlayer(player);
        }

        private void RememberPlayerContact(Vector3 position)
        {
            _lastKnownTargetPos = position;
            _lastPlayerContactTime = Time.time;
        }



        private void FacePositionSmooth(Vector3 worldPos, float turnRate, float dt)
        {
            Vector3 dir = worldPos - transform.position;
            dir.y = 0f;
            if (dir.sqrMagnitude < 0.001f)
            {
                return;
            }

            Quaternion desired = Quaternion.LookRotation(dir.normalized, Vector3.up);
            transform.rotation = Quaternion.Slerp(transform.rotation, desired, 1f - Mathf.Exp(-turnRate * dt));
        }

        private static PlayerControllerB? ResolvePlayerByClientId(int clientId)
        {
            if (clientId < 0 || StartOfRound.Instance == null)
            {
                return null;
            }

            PlayerControllerB[] players = StartOfRound.Instance.allPlayerScripts;
            for (int i = 0; i < players.Length; i++)
            {
                if (players[i] != null && (int)players[i].playerClientId == clientId)
                {
                    return players[i];
                }
            }
            return null;
        }

        // =========================================================================
        // 9 Custom NGO 1.12.2 NetcodePatcher RPCs (ARCHITECTURE.md Section 4.3)
        // =========================================================================

        [ClientRpc]
        public void SyncBehaviourStateClientRpc(int newState, int targetPlayerClientId, bool playRoar)
        {
            if (currentBehaviourStateIndex != newState)
            {
                previousBehaviourStateIndex = currentBehaviourStateIndex;
                currentBehaviourStateIndex = newState;
            }

            if (enemyBehaviourStates != null && newState >= 0 && newState < enemyBehaviourStates.Length)
            {
                currentBehaviourState = enemyBehaviourStates[newState];
            }

            targetPlayer = ResolvePlayerByClientId(targetPlayerClientId);

            // Server-authoritative roar flag ensures identical playback on Host and remote clients without client-side timer drift.
            if (playRoar && !isEnemyDead)
            {
                audioController?.PlayRoar();
            }
        }

        [ClientRpc]
        public void TriggerAttackClientRpc(bool isSlam, bool isDoorBash)
        {
            if (isEnemyDead)
            {
                return;
            }

            if (creatureAnimator != null)
            {
                creatureAnimator.SetTrigger(isSlam ? AttackSlamHash : AttackCleaveHash);
            }

            if (isSlam)
            {
                audioController?.PlayRoar(0.8f);
            }
            else if (!isDoorBash)
            {
                audioController?.PlaySlash();
            }
        }

        [ClientRpc]
        public void ApplyPlayerDamageClientRpc(int targetPlayerClientId, int damage, int causeOfDeath, Vector3 knockbackForce)
        {
            PlayerControllerB? localPlayer = GameNetworkManager.Instance != null
                ? GameNetworkManager.Instance.localPlayerController
                : null;

            if (localPlayer == null || (int)localPlayer.playerClientId != targetPlayerClientId)
            {
                return;
            }

            if (localPlayer.isPlayerDead || !localPlayer.AllowPlayerDeath())
            {
                return;
            }

            localPlayer.DamagePlayer(
                damageNumber: damage,
                hasDamageSFX: true,
                callRPC: true,
                causeOfDeath: (CauseOfDeath)causeOfDeath,
                deathAnimation: 0,
                fallDamage: false,
                force: knockbackForce);

            // DamagePlayer only uses 'force' for the ragdoll of a killed player. A living player is shoved the way Landmine
            // does it (externalForceAutoFade is decayed by PlayerControllerB itself), computed here on the owning client.
            if (!localPlayer.isPlayerDead && knockbackForce.sqrMagnitude > 0.25f)
            {
                if (knockbackForce.magnitude > 10f)
                {
                    localPlayer.CancelSpecialTriggerAnimations();
                }

                if (!localPlayer.inVehicleAnimation)
                {
                    localPlayer.externalForceAutoFade += knockbackForce;
                }
            }
        }

        [ClientRpc]
        public void PlayAttackImpactClientRpc(bool isSlam, Vector3 impactPos)
        {
            audioController?.PlayImpact(isSlam ? 1.0f : 0.9f);

            PlayerControllerB? localPlayer = GameNetworkManager.Instance != null
                ? GameNetworkManager.Instance.localPlayerController
                : null;

            if (localPlayer == null || HUDManager.Instance == null)
            {
                return;
            }

            float dist = Vector3.Distance(localPlayer.transform.position, impactPos);
            if (isSlam)
            {
                if (dist <= 9f)
                {
                    HUDManager.Instance.ShakeCamera(ScreenShakeType.Big);
                }
                else if (dist <= 14f)
                {
                    HUDManager.Instance.ShakeCamera(ScreenShakeType.Small);
                }
            }
            else if (dist <= 10f)
            {
                HUDManager.Instance.ShakeCamera(ScreenShakeType.Small);
            }
        }

        [ClientRpc]
        public void TriggerAdaptationClientRpc(byte categoryId, int newAdaptedMask, int wheelStepCount, int newHP)
        {
            _adaptation.SyncFromServer(newAdaptedMask, wheelStepCount);
            enemyHP = newHP;
            stunNormalizedTimer = -1f;
            stunnedIndefinitely = 0;

            previousBehaviourStateIndex = currentBehaviourStateIndex;
            currentBehaviourStateIndex = (int)MahoragaState.Adapting;

            if (creatureAnimator != null)
            {
                creatureAnimator.SetBool(IsStunnedHash, false);
                creatureAnimator.SetTrigger(AdaptHash);
            }

            // Smooth +45° rotation around WheelBone local Y synced with t = 0.25s..1.05s of Adapt_WheelSpin.
            _wheelSpinStartAngle = _wheelDisplayAngle;
            _wheelSpinTargetAngle = ComputeWheelAngle(wheelStepCount);
            _wheelSpinDelay = WheelSpinDelay;
            _wheelSpinDuration = WheelSpinDuration;
            _wheelSpinElapsed = 0f;
            _wheelSpinActive = true;

            // Radiant golden HDR emissive pulse peaking at t = 0.35s and decaying over 1.8s.
            _emissiveDelayTimer = 0.35f;
            _emissivePeakIntensity = 60f;
            _emissiveFlashDuration = 1.8f;
            _emissiveFlashTimer = _emissiveFlashDuration;

            audioController?.PlayWheelTurn();
        }

        /// <summary>
        /// Incremental +45 deg Dharma wheel spoke turn (1 ball -> next ball, NEVER 360 deg).
        /// When <paramref name="playSpinAnim"/> is true (e.g. Old Bird hit / growth step), also triggers a crisp
        /// Adapt_WheelSpin pose when not attacking/summoning/submerging.
        /// </summary>
        [ClientRpc]
        public void TriggerWheelClickClientRpc(byte categoryId, byte stage, int newAdaptedMask, int wheelStepCount, int newHP, bool playSpinAnim)
        {
            _adaptation.SyncFromServer(newAdaptedMask, wheelStepCount);
            if (!IsServer)
            {
                enemyHP = newHP;
            }

            _wheelSpinStartAngle = _wheelDisplayAngle;
            _wheelSpinTargetAngle = ComputeWheelAngle(wheelStepCount);
            _wheelSpinDelay = playSpinAnim ? 0.08f : 0f;
            _wheelSpinDuration = playSpinAnim ? 0.50f : 0.28f;
            _wheelSpinElapsed = 0f;
            _wheelSpinActive = true;

            if (!isEnemyDead)
            {
                if (playSpinAnim &&
                    creatureAnimator != null &&
                    !IsSummonOrSubmergeActive &&
                    currentBehaviourStateIndex != (int)MahoragaState.Attacking)
                {
                    creatureAnimator.SetTrigger(AdaptHash);
                }

                _emissiveDelayTimer = 0f;
                _emissivePeakIntensity = (playSpinAnim ? 36f : 22f) + stage * 6f;
                _emissiveFlashDuration = playSpinAnim ? 1.0f : 0.7f;
                _emissiveFlashTimer = _emissiveFlashDuration;

                if (playSpinAnim)
                {
                    audioController?.PlayWheelTurn(0.85f);
                }
                else
                {
                    audioController?.PlayWheelClick(0.55f + 0.1f * stage);
                }
            }
        }

        [ClientRpc]
        public void PlayDeflectFeedbackClientRpc(byte categoryId)
        {
            if (isEnemyDead)
            {
                return;
            }

            _emissiveDelayTimer = 0f;
            _emissivePeakIntensity = 18f;
            _emissiveFlashDuration = 0.35f;
            _emissiveFlashTimer = _emissiveFlashDuration;

            audioController?.PlayDeflectChime();
        }

        [ServerRpc(RequireOwnership = false)]
        public void RequestContactAttackServerRpc(int collidingPlayerClientId)
        {
            if (!IsServer || isEnemyDead)
            {
                return;
            }

            if (currentBehaviourStateIndex == (int)MahoragaState.Adapting ||
                currentBehaviourStateIndex == (int)MahoragaState.Attacking ||
                IsSummonInvulnerable ||
                stunNormalizedTimer > 0f ||
                stunnedIndefinitely > 0)
            {
                return;
            }

            PlayerControllerB? player = ResolvePlayerByClientId(collidingPlayerClientId);
            if (player == null || !PlayerIsTargetable(player))
            {
                return;
            }

            if (!CanSenseByProximity(player))
            {
                return;
            }

            if (!HasClearStrikePathToPlayer(player))
            {
                return;
            }

            targetPlayer = player;
            RememberPlayerContact(player.transform.position);
            if (_attackCooldownTimer <= 0f)
            {
                bool useSlam = ShouldUseSlamAttack(forceSlam: false);
                EnterAttackStateServer(useSlam);
            }
            else if (currentBehaviourStateIndex != (int)MahoragaState.Chasing)
            {
                EnterChaseStateServer(player);
            }
        }

        [ServerRpc(RequireOwnership = false)]
        public void RequestFullSyncServerRpc(ServerRpcParams rpcParams = default)
        {
            if (!IsServer)
            {
                return;
            }

            var targetParams = new ClientRpcParams
            {
                Send = new ClientRpcSendParams
                {
                    TargetClientIds = new[] { rpcParams.Receive.SenderClientId }
                }
            };

            ApplyFullSyncClientRpc(
                currentBehaviourStateIndex,
                _adaptation.AdaptedMask,
                _adaptation.WheelStepCount,
                _summonWheelOffsetSteps,
                enemyHP,
                isEnemyDead,
                _lastBroadcastScale,
                targetParams);

            // Late join / late spawn while summoning or submerging: resume at the right moment.
            SendSummonSyncIfActive(targetParams);
            SendSubmergeSyncIfActive(targetParams);
            if (stunNormalizedTimer > 0f)
            {
                SyncTimedStunClientRpc(stunNormalizedTimer, targetParams);
            }
        }

        [ClientRpc]
        public void ApplyFullSyncClientRpc(int state, int adaptedMask, int wheelStepCount, int summonWheelSteps, int currentHP, bool dead, float growthScale, ClientRpcParams rpcParams = default)
        {
            if (IsServer)
            {
                return;
            }

            _adaptation.SyncFromServer(adaptedMask, wheelStepCount);
            _summonWheelOffsetSteps = Mathf.Max(0, summonWheelSteps);
            enemyHP = currentHP;
            currentBehaviourStateIndex = state;

            _wheelSpinActive = false;
            _wheelDisplayAngle = ComputeWheelAngle(wheelStepCount);

            // Late joiners snap straight to the synced size (no growth animation).
            _targetScale = Mathf.Clamp(growthScale, 1f, 3.5f);
            _currentScale = _targetScale;
            ApplyScale(_currentScale);

            if (dead && !isEnemyDead)
            {
                KillEnemy(destroy: false);
            }
        }
    }
}
