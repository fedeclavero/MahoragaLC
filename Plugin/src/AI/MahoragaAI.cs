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
        Adapting = 4
    }

    /// <summary>
    /// Server-authoritative apex EnemyAI for Divine General Mahoraga.
    /// Ownership permanently remains on Server/Host (ChangeOwnershipOfEnemy is never called).
    /// All serialized fields strictly match ASSET_CONTRACT.md Section 7.3.
    /// </summary>
    public class MahoragaAI : EnemyAI
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
        private const float ChaseLosMemorySeconds = 10.0f;
        private const float RoarCooldownSeconds = 12.0f;
        private const float LockedDoorForceDuration = 2.20f;
        private const float LockedDoorScanRange = 3.2f;

        private readonly MahoragaAdaptationSystem _adaptation = new MahoragaAdaptationSystem();
        private readonly Collider[] _doorOverlapBuffer = new Collider[8];

        private MaterialPropertyBlock? _propBlock;
        private Quaternion _wheelRestLocalRotation = Quaternion.identity;
        private bool _hasWheelRestRotation;
        private float _wheelDisplayAngle;
        private float _wheelSpinStartAngle;
        private float _wheelSpinTargetAngle;
        private float _wheelSpinElapsed = 999f;
        private bool _wheelSpinActive;

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
        private float _losLostTimer;
        private float _roarCooldownTimer;
        private float _attackCooldownTimer;
        private float _deflectRpcCooldownTimer;
        private float _lastHitFromExplosionTime = -999f;

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
        }

        public override void Start()
        {
            base.Start();

            EnsureBehaviourStatesArray();
            ResolvePrefabReferences();
            CacheWheelRestRotation();

            // Sync inside/outside domain with actual spawn elevation (supports outdoor & Imperium spawns).
            bool spawnedOutside = transform.position.y > -40f;
            if (isOutside != spawnedOutside)
            {
                SetEnemyOutside(spawnedOutside);
            }

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
                StartSearch(transform.position);
            }
            else if (NetworkObject != null && NetworkObject.IsSpawned)
            {
                RequestFullSyncServerRpc();
            }
        }

        private void EnsureBehaviourStatesArray()
        {
            if (enemyBehaviourStates != null && enemyBehaviourStates.Length >= 5)
            {
                return;
            }

            string[] names = { "Patrolling", "Investigating", "Chasing", "Attacking", "Adapting" };
            var states = new EnemyBehaviourState[5];
            for (int i = 0; i < 5; i++)
            {
                states[i] = (enemyBehaviourStates != null && i < enemyBehaviourStates.Length && enemyBehaviourStates[i] != null)
                    ? enemyBehaviourStates[i]
                    : new EnemyBehaviourState { name = names[i] };
            }
            enemyBehaviourStates = states;
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
            if (_clientContactCooldown > 0f)
            {
                _clientContactCooldown -= dt;
            }

            // Enforce ZapGun / Adapting / Dead stun immunity on ALL clients (handles PatcherTool's post-SetEnemyStunned stunnedIndefinitely++).
            bool adaptingNow = currentBehaviourStateIndex == (int)MahoragaState.Adapting;
            if (isEnemyDead || adaptingNow || _adaptation.IsAdapted(AdaptationCategory.ZapGun))
            {
                if (stunnedIndefinitely > 0)
                {
                    stunnedIndefinitely = 0;
                }
                if (adaptingNow || isEnemyDead)
                {
                    stunNormalizedTimer = 0f;
                }
            }

            UpdateClientPresentation(dt);

            if (!IsServer || isEnemyDead)
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
                wheelBone.localRotation = _wheelRestLocalRotation * Quaternion.Euler(0f, _wheelDisplayAngle, 0f);
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

            bool isStunned = (stunNormalizedTimer > 0f || stunnedIndefinitely > 0)
                && !isEnemyDead
                && currentBehaviourStateIndex != (int)MahoragaState.Adapting;

            bool isChasing = !isEnemyDead &&
                (currentBehaviourStateIndex == (int)MahoragaState.Chasing ||
                 currentBehaviourStateIndex == (int)MahoragaState.Attacking);

            if (creatureAnimator != null)
            {
                creatureAnimator.SetFloat(SpeedHash, isEnemyDead ? 0f : _smoothedHorizontalSpeed);
                creatureAnimator.SetBool(IsChasingHash, isChasing);
                creatureAnimator.SetBool(IsStunnedHash, isStunned);
                creatureAnimator.SetBool(IsDeadHash, isEnemyDead);
            }

            if (!isEnemyDead && !isStunned && currentBehaviourStateIndex != (int)MahoragaState.Adapting)
            {
                if (_smoothedHorizontalSpeed > 0.35f)
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
            float spinTime = _wheelSpinElapsed - WheelSpinDelay;
            if (spinTime <= 0f)
            {
                return;
            }

            float u = Mathf.Clamp01(spinTime / WheelSpinDuration);
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

            float baseIdleGlow = 0.4f + _adaptation.WheelStepCount * 0.35f;

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
                mainRenderer.GetPropertyBlock(_propBlock);
                _propBlock.SetColor(EmissiveColorId, BaseGoldColor * emissiveIntensity);
                mainRenderer.SetPropertyBlock(_propBlock);
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
                int threshold = cfg != null ? cfg.AdaptationThreshold.Value : 2;
                float healPct = cfg != null ? cfg.AdaptationHealPercent.Value : 0.20f;

                StunEvaluation beamEval = _adaptation.EvaluateSustainedZapBeam(
                    enemyHP,
                    maxHp,
                    isCurrentlyAdapting: false,
                    threshold,
                    healPct,
                    Time.time);

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

            switch ((MahoragaState)currentBehaviourStateIndex)
            {
                case MahoragaState.Patrolling:
                    RunPatrolInterval(cfg);
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

            PlayerControllerB? spotted = FindBestVisibleOrNearbyPlayer(viewHalfAngle: 65f, sightRange: 26f, proximityRadius: 4.0f);
            if (spotted != null)
            {
                EnterChaseStateServer(spotted);
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

            PlayerControllerB? spotted = FindBestVisibleOrNearbyPlayer(viewHalfAngle: 65f, sightRange: 30f, proximityRadius: 5.0f);
            if (spotted != null)
            {
                EnterChaseStateServer(spotted);
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
                SetDestinationToPosition(_investigatePos, checkForPath: false);
                TryScanForLockedDoorServer(cfg);
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
                PlayerControllerB? replacement = FindBestVisibleOrNearbyPlayer(viewHalfAngle: 90f, sightRange: 30f, proximityRadius: 6.0f);
                if (replacement != null)
                {
                    SetChaseTargetServer(replacement);
                }
                else
                {
                    EnterPatrolStateServer();
                    return;
                }
            }

            // Target switching: pivot if another visible player is strictly < 65% of current target distance.
            float currentDist = Vector3.Distance(transform.position, targetPlayer!.transform.position);
            PlayerControllerB? closerCandidate = FindCloserVisiblePlayer(targetPlayer, currentDist * 0.65f);
            if (closerCandidate != null)
            {
                SetChaseTargetServer(closerCandidate);
                currentDist = Vector3.Distance(transform.position, targetPlayer.transform.position);
            }

            // Track LOS memory for 10s before dropping to Investigating.
            if (HasLineOfSightToPlayer(targetPlayer, 35f))
            {
                _lastKnownTargetPos = targetPlayer.transform.position;
                _losLostTimer = 0f;
            }
            else
            {
                _losLostTimer += AIIntervalTime;
                if (_losLostTimer >= ChaseLosMemorySeconds)
                {
                    EnterInvestigateStateServer(_lastKnownTargetPos);
                    return;
                }
            }

            SetMovingTowardsTargetPlayer(targetPlayer);

            if (TryScanForLockedDoorServer(cfg))
            {
                return;
            }

            // Attack range evaluation (including railing/ledge Divergent Slam punish).
            if (_attackCooldownTimer <= 0f)
            {
                Vector3 toTarget = targetPlayer.transform.position - transform.position;
                float horizDist = new Vector2(toTarget.x, toTarget.z).magnitude;
                float vertDelta = Mathf.Abs(toTarget.y);

                bool inStandardRange = horizDist <= AttackEntryRange && vertDelta <= VerticalHitTolerance;
                bool inRailingPunishRange = !inStandardRange
                    && horizDist <= SlamReach - 0.15f
                    && vertDelta <= VerticalHitTolerance
                    && agent != null
                    && agent.enabled
                    && agent.isOnNavMesh
                    && (agent.pathStatus == NavMeshPathStatus.PathPartial || agent.remainingDistance <= 0.75f);

                if (inStandardRange || inRailingPunishRange)
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
            _losLostTimer = 0f;

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
            SetDestinationToPosition(_investigatePos, checkForPath: false);
        }

        private void EnterChaseStateServer(PlayerControllerB player)
        {
            _forcingDoor = null;
            StopSearch(currentSearch);
            targetPlayer = player;
            _lastKnownTargetPos = player.transform.position;
            _losLostTimer = 0f;

            SwitchBehaviourAndSyncServer(MahoragaState.Chasing, (int)player.playerClientId);
            SetMovingTowardsTargetPlayer(player);
        }

        private void SetChaseTargetServer(PlayerControllerB player)
        {
            targetPlayer = player;
            _lastKnownTargetPos = player.transform.position;
            _losLostTimer = 0f;
            SyncBehaviourStateClientRpc((int)MahoragaState.Chasing, (int)player.playerClientId, playRoar: false);
            SetMovingTowardsTargetPlayer(player);
        }

        private void EnterAttackStateServer(bool isSlam)
        {
            StopSearch(currentSearch);
            _currentAttackIsSlam = isSlam;
            _attackElapsed = 0f;
            _attackHitResolved = false;
            _attackSlashPlayed = false;
            _attacksSinceLastSlam = isSlam ? 0 : (_attacksSinceLastSlam + 1);

            int targetId = targetPlayer != null ? (int)targetPlayer.playerClientId : -1;
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
            if (_attackElapsed < windup && targetPlayer != null && PlayerIsTargetable(targetPlayer))
            {
                FacePositionSmooth(targetPlayer.transform.position, 9f, dt);
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
                if (targetPlayer != null && PlayerIsTargetable(targetPlayer))
                {
                    EnterChaseStateServer(targetPlayer);
                }
                else
                {
                    PlayerControllerB? next = FindBestVisibleOrNearbyPlayer(viewHalfAngle: 120f, sightRange: 26f, proximityRadius: 6.0f);
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
            float reach = isSlam ? SlamReach : CleaveReach;

            bool anyHit = false;
            Vector3 impactOrigin = transform.position + transform.forward * (isSlam ? 0.6f : 1.4f) + Vector3.up * 1.4f;
            Vector3 rayOrigin = transform.position + Vector3.up * 1.8f;

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
                if (vertDelta > VerticalHitTolerance)
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

                // Wall LOS check so attacks never clip through solid facility walls.
                Vector3 playerChest = p.transform.position + Vector3.up * 1.3f;
                if (Physics.Linecast(rayOrigin, playerChest, StartOfRound.Instance.collidersAndRoomMaskAndDefault, QueryTriggerInteraction.Ignore))
                {
                    continue;
                }

                Vector3 knockDir = horizDist > 0.05f ? flatToPlayer.normalized : transform.forward;
                Vector3 knockback = knockDir * (isSlam ? 16f : 10f) + Vector3.up * (isSlam ? 5f : 2.5f);

                ApplyPlayerDamageClientRpc((int)p.playerClientId, damage, (int)cause, knockback);
                anyHit = true;
            }

            if (isSlam || anyHit)
            {
                PlayAttackImpactClientRpc(isSlam, impactOrigin);
            }
        }

        private bool TryScanForLockedDoorServer(MahoragaConfig? cfg)
        {
            if (cfg != null && !cfg.CanForceLockedDoors.Value)
            {
                return false;
            }

            Vector3 probeCenter = transform.position + transform.forward * 1.4f + Vector3.up * 1.5f;
            int hits = Physics.OverlapSphereNonAlloc(probeCenter, 1.4f, _doorOverlapBuffer, InteractableLayerMask, QueryTriggerInteraction.Collide);
            for (int i = 0; i < hits; i++)
            {
                Collider col = _doorOverlapBuffer[i];
                if (col == null)
                {
                    continue;
                }

                DoorLock? door = col.GetComponent<DoorLock>() ?? col.GetComponentInParent<DoorLock>();
                if (door == null || !door.isLocked)
                {
                    continue;
                }

                Vector3 toDoor = door.transform.position - transform.position;
                toDoor.y = 0f;
                if (toDoor.magnitude > LockedDoorScanRange)
                {
                    continue;
                }

                // Confirm door lies ahead of Mahoraga's approach vector.
                if (toDoor.sqrMagnitude > 0.04f && Vector3.Dot(transform.forward, toDoor.normalized) < 0.25f)
                {
                    continue;
                }

                _forcingDoor = door;
                _doorForceElapsed = 0f;
                _doorBashStage = 0;
                return true;
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
            stunNormalizedTimer = 0f;
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
            if (targetPlayer != null && PlayerIsTargetable(targetPlayer))
            {
                EnterChaseStateServer(targetPlayer);
                return;
            }

            PlayerControllerB? spotted = FindBestVisibleOrNearbyPlayer(viewHalfAngle: 120f, sightRange: 30f, proximityRadius: 8.0f);
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
            if (!IsServer || isEnemyDead)
            {
                return;
            }

            if (currentBehaviourStateIndex != (int)MahoragaState.Patrolling &&
                currentBehaviourStateIndex != (int)MahoragaState.Investigating)
            {
                return;
            }

            bool noiseOutside = noisePosition.y > -40f;
            if (noiseOutside != isOutside)
            {
                return;
            }

            float maxHearingDist = 24.0f * Mathf.Clamp(noiseLoudness, 0.5f, 1.5f);
            if (Vector3.Distance(transform.position, noisePosition) > maxHearingDist)
            {
                return;
            }

            EnterInvestigateStateServer(noisePosition);
        }

        public override void HitEnemy(int force = 1, PlayerControllerB? playerWhoHit = null, bool playHitSFX = false, int hitID = -1)
        {
            // Suppress base PlayOneShot(hitEnemyVoiceSFX) null warnings; handle impact/deflect SFX cleanly ourselves.
            base.HitEnemy(force, playerWhoHit, playHitSFX: false, hitID);

            if (isEnemyDead)
            {
                return;
            }

            if (!IsServer)
            {
                return;
            }

            float now = Time.time;
            bool heldIsShovelOrKnife = playerWhoHit != null &&
                (playerWhoHit.currentlyHeldObjectServer is Shovel || playerWhoHit.currentlyHeldObjectServer is KnifeItem);
            bool heldIsShotgun = playerWhoHit != null && playerWhoHit.currentlyHeldObjectServer is ShotgunItem;
            bool recentExplosion = (now - _lastHitFromExplosionTime <= 0.15f)
                || ExplosionTrackingPatch.HasRecentExplosionNear(transform.position, now);

            AdaptationCategory category = MahoragaAdaptationSystem.ClassifyDamage(
                hitID,
                force,
                hasAttacker: playerWhoHit != null,
                heldIsShovelOrKnife: heldIsShovelOrKnife,
                heldIsShotgun: heldIsShotgun,
                recentExplosionNearby: recentExplosion);

            MahoragaConfig? cfg = MahoragaConfig.Current;
            int maxHp = cfg != null ? cfg.MaxHP.Value : 18;
            int threshold = cfg != null ? cfg.AdaptationThreshold.Value : 2;
            float resistance = cfg != null ? cfg.AdaptedDamageResistance.Value : 0.95f;
            float healPct = cfg != null ? cfg.AdaptationHealPercent.Value : 0.20f;
            bool isAdapting = currentBehaviourStateIndex == (int)MahoragaState.Adapting;

            DamageEvaluation eval = _adaptation.EvaluateDamage(
                category,
                force,
                enemyHP,
                maxHp,
                isAdapting,
                threshold,
                resistance,
                healPct,
                now);

            enemyHP = eval.FinalHP;

            // Lethal-blow precedence: kill immediately before adaptation can fire!
            if (eval.IsLethal)
            {
                KillEnemyOnOwnerClient();
                return;
            }

            // Aggro onto attacker if patrolling or investigating.
            if (playerWhoHit != null && PlayerIsTargetable(playerWhoHit))
            {
                if (currentBehaviourStateIndex == (int)MahoragaState.Patrolling ||
                    currentBehaviourStateIndex == (int)MahoragaState.Investigating)
                {
                    EnterChaseStateServer(playerWhoHit);
                }
                else if (targetPlayer == null)
                {
                    targetPlayer = playerWhoHit;
                }
            }

            if (eval.TriggeredAdaptation)
            {
                EnterAdaptationStateServer(category, eval.FinalHP);
                return;
            }

            if (eval.WasAlreadyAdapted)
            {
                if (_deflectRpcCooldownTimer <= 0f)
                {
                    _deflectRpcCooldownTimer = 0.25f;
                    PlayDeflectFeedbackClientRpc((byte)category);
                }
            }
            else if (playHitSFX)
            {
                PlayAttackImpactClientRpc(isSlam: false, transform.position + Vector3.up * 1.8f);
            }
        }

        public override void HitFromExplosion(float distance)
        {
            base.HitFromExplosion(distance);
            if (!IsServer || isEnemyDead)
            {
                return;
            }

            float now = Time.time;
            _lastHitFromExplosionTime = now;

            // Landmine.SpawnExplosion calls HitEnemyOnLocalClient(6) immediately before HitFromExplosion(distance).
            // Skip if HitEnemy already processed this blast within 0.15s.
            if (_adaptation.IsDuplicateExplosion(now))
            {
                return;
            }

            // Note: If Landmine's HitEnemyServerRpc executes on the same tick immediately after HitFromExplosion,
            // recording _lastHitFromExplosionTime guarantees HitEnemy classifies it deterministically as Explosion
            // without double-applying damage or exposure here.
        }

        public override void SetEnemyStunned(bool setToStunned, float setToStunTime = 1f, PlayerControllerB? setStunnedByPlayer = null)
        {
            if (isEnemyDead)
            {
                return;
            }

            if (!setToStunned)
            {
                base.SetEnemyStunned(false, setToStunTime, setStunnedByPlayer);
                return;
            }

            bool isZapGun = setStunnedByPlayer != null && setStunnedByPlayer.currentlyHeldObjectServer is PatcherTool;
            AdaptationCategory category = MahoragaAdaptationSystem.ClassifyStun(isZapGun);

            // Immediate stun negation on all clients when adapted or mid-adaptation.
            if (currentBehaviourStateIndex == (int)MahoragaState.Adapting || _adaptation.IsAdapted(category))
            {
                stunNormalizedTimer = 0f;
                stunnedIndefinitely = 0;

                if (IsServer && _deflectRpcCooldownTimer <= 0f)
                {
                    _deflectRpcCooldownTimer = 0.80f;
                    PlayDeflectFeedbackClientRpc((byte)category);
                }
                return;
            }

            if (IsServer)
            {
                MahoragaConfig? cfg = MahoragaConfig.Current;
                int maxHp = cfg != null ? cfg.MaxHP.Value : 18;
                int threshold = cfg != null ? cfg.AdaptationThreshold.Value : 2;
                float healPct = cfg != null ? cfg.AdaptationHealPercent.Value : 0.20f;

                StunEvaluation eval = _adaptation.EvaluateStun(
                    category,
                    enemyHP,
                    maxHp,
                    isCurrentlyAdapting: false,
                    threshold,
                    healPct,
                    Time.time);

                if (eval.TriggeredAdaptation)
                {
                    stunNormalizedTimer = 0f;
                    stunnedIndefinitely = 0;

                    if (setStunnedByPlayer != null && PlayerIsTargetable(setStunnedByPlayer))
                    {
                        targetPlayer = setStunnedByPlayer;
                    }

                    EnterAdaptationStateServer(category, eval.FinalHP);
                    return;
                }
            }

            base.SetEnemyStunned(true, setToStunTime, setStunnedByPlayer);
        }

        public override void OnCollideWithPlayer(Collider other)
        {
            base.OnCollideWithPlayer(other);
            if (_clientContactCooldown > 0f || isEnemyDead)
            {
                return;
            }

            if (currentBehaviourStateIndex == (int)MahoragaState.Adapting ||
                currentBehaviourStateIndex == (int)MahoragaState.Attacking)
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

        private void SwitchBehaviourAndSyncServer(MahoragaState newState, int targetPlayerClientId)
        {
            int stateIdx = (int)newState;
            bool wasChasing = currentBehaviourStateIndex == (int)MahoragaState.Chasing
                || currentBehaviourStateIndex == (int)MahoragaState.Attacking;

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

        private PlayerControllerB? FindBestVisibleOrNearbyPlayer(float viewHalfAngle, float sightRange, float proximityRadius)
        {
            if (StartOfRound.Instance == null)
            {
                return null;
            }

            Transform eyeTransform = eye != null ? eye : transform;
            Vector3 eyePos = eyeTransform.position;
            Vector3 eyeForward = eyeTransform.forward;

            PlayerControllerB? best = null;
            float bestDist = float.MaxValue;

            PlayerControllerB[] players = StartOfRound.Instance.allPlayerScripts;
            for (int i = 0; i < players.Length; i++)
            {
                PlayerControllerB p = players[i];
                if (p == null || !PlayerIsTargetable(p))
                {
                    continue;
                }

                Vector3 camPos = p.gameplayCamera != null
                    ? p.gameplayCamera.transform.position
                    : p.transform.position + Vector3.up * 2.2f;

                float dist = Vector3.Distance(eyePos, camPos);
                if (dist > sightRange)
                {
                    continue;
                }

                Vector3 toCam = camPos - eyePos;
                bool inCone = Vector3.Angle(eyeForward, toCam) <= viewHalfAngle;
                bool inProximity = Vector3.Distance(transform.position, p.transform.position) <= proximityRadius;
                if (!inCone && !inProximity)
                {
                    continue;
                }

                if (Physics.Linecast(eyePos, camPos, StartOfRound.Instance.collidersAndRoomMaskAndDefault, QueryTriggerInteraction.Ignore))
                {
                    continue;
                }

                if (dist < bestDist)
                {
                    bestDist = dist;
                    best = p;
                }
            }

            return best;
        }

        private PlayerControllerB? FindCloserVisiblePlayer(PlayerControllerB currentTarget, float maxDist)
        {
            if (StartOfRound.Instance == null)
            {
                return null;
            }

            PlayerControllerB? best = null;
            float bestDist = maxDist;
            PlayerControllerB[] players = StartOfRound.Instance.allPlayerScripts;

            for (int i = 0; i < players.Length; i++)
            {
                PlayerControllerB p = players[i];
                if (p == null || p == currentTarget || !PlayerIsTargetable(p))
                {
                    continue;
                }

                float d = Vector3.Distance(transform.position, p.transform.position);
                if (d < bestDist && HasLineOfSightToPlayer(p, maxDist + 2f))
                {
                    bestDist = d;
                    best = p;
                }
            }

            return best;
        }

        private bool HasLineOfSightToPlayer(PlayerControllerB player, float maxRange)
        {
            if (StartOfRound.Instance == null || player == null)
            {
                return false;
            }

            Vector3 eyePos = eye != null ? eye.position : transform.position + Vector3.up * 3.3f;
            Vector3 targetPos = player.gameplayCamera != null
                ? player.gameplayCamera.transform.position
                : player.transform.position + Vector3.up * 2.2f;

            if (Vector3.Distance(eyePos, targetPos) > maxRange)
            {
                return false;
            }

            return !Physics.Linecast(eyePos, targetPos, StartOfRound.Instance.collidersAndRoomMaskAndDefault, QueryTriggerInteraction.Ignore);
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
            stunNormalizedTimer = 0f;
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
            _wheelSpinTargetAngle = wheelStepCount * 45f;
            _wheelSpinElapsed = 0f;
            _wheelSpinActive = true;

            // Radiant golden HDR emissive pulse peaking at t = 0.35s and decaying over 1.8s.
            _emissiveDelayTimer = 0.35f;
            _emissivePeakIntensity = 60f;
            _emissiveFlashDuration = 1.8f;
            _emissiveFlashTimer = _emissiveFlashDuration;

            audioController?.PlayWheelTurn();
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

            if (Vector3.Distance(transform.position, player.transform.position) > 3.5f)
            {
                return;
            }

            targetPlayer = player;
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
                enemyHP,
                isEnemyDead,
                targetParams);
        }

        [ClientRpc]
        public void ApplyFullSyncClientRpc(int state, int adaptedMask, int wheelStepCount, int currentHP, bool dead, ClientRpcParams rpcParams = default)
        {
            if (IsServer)
            {
                return;
            }

            _adaptation.SyncFromServer(adaptedMask, wheelStepCount);
            enemyHP = currentHP;
            currentBehaviourStateIndex = state;

            _wheelSpinActive = false;
            _wheelDisplayAngle = wheelStepCount * 45f;

            if (dead && !isEnemyDead)
            {
                KillEnemy(destroy: false);
            }
        }
    }
}
