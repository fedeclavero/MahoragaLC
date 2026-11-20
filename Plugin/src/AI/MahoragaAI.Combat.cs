using System;
using System.Collections.Generic;
using GameNetcodeStuff;
using MahoragaLC.Config;
using MahoragaLC.Patches;
using UnityEngine;
using UnityEngine.AI;

namespace MahoragaLC.AI
{
    /// <summary>Server-side damage and enemy engagement, including reactive fights and Old Bird hunts.</summary>
    public partial class MahoragaAI
    {
        private const float EngageEntryRangeBase = 2.6f;
        private const float EnemyAggressorMaxDistance = 40f;
        private const float EnemyUnreachableLimitSeconds = 8.0f;
        private const float EnemyMaxChaseDistance = 120f;

        private static AdaptationSettings _fallbackAdaptation = AdaptationSettings.CreateProgressive();

        private readonly AggroTable _playerAggro = new AggroTable(6f);
        private readonly AggroTable _enemyAggro = new AggroTable(5f);
        private readonly CooldownSet _ignoredEnemies = new CooldownSet();
        private readonly DistinctAttackerWindow _oldBirdAttackers = new DistinctAttackerWindow();
        private readonly Dictionary<Type, AdaptationCategory> _customWeaponCategories = new Dictionary<Type, AdaptationCategory>();
        private readonly CoordinatedShovelStrike _shovelStrike = new CoordinatedShovelStrike();
        private int _oldBirdVolleyStage;
        private uint _lastOldBirdDamageBlast;
        private float _oldBirdVolleyStartedAt = float.NegativeInfinity;
        private readonly RegenerationTracker _regen = new RegenerationTracker();
        private readonly Dictionary<int, EnemyAI> _aggressorLookup = new Dictionary<int, EnemyAI>();
        private readonly List<EnemyAI> _pushes = new List<EnemyAI>();
        private Func<int, bool>? _acceptEnemyAggressorCache;
        private Func<int, bool>? _acceptPlayerAggressorCache;

        private EnemyAI? _enemyTarget;
        private EnemyAICollisionDetect[]? _enemyTargetDetectors;
        private float _engageStartTime;
        private float _engageUnreachableTimer;
        private int _engageRepathCounter;
        private PlayerControllerB? _resumePlayerAfterEngage;
        private EnemyAI? _attackEnemyTarget;

        private float _lastRetargetTime = -999f;
        private float _lastDamageTakenTime = -999f;
        private float _lastPruneTime;

        private Func<int, bool> _acceptEnemyAggressor => _acceptEnemyAggressorCache ??= AcceptEnemyAggressor;

        private Func<int, bool> _acceptPlayerAggressor => _acceptPlayerAggressorCache ??= AcceptPlayerAggressor;

        private bool AcceptEnemyAggressor(int id)
        {
            return _aggressorLookup.TryGetValue(id, out EnemyAI? e) && IsEnemyEngageable(e);
        }

        private bool AcceptPlayerAggressor(int id)
        {
            PlayerControllerB? p = ResolvePlayerByClientId(id);
            return p != null && PlayerIsTargetable(p);
        }

        private void ClearCombatState()
        {
            _oldBirdDurability.Clear();
            _oldBirdHeights.Clear();
            _pushes.Clear();
            _playerAggro.Clear();
            _enemyAggro.Clear();
            _ignoredEnemies.Clear();
            _oldBirdAttackers.Clear();
            _shovelStrike.Clear();
            _customWeaponCategories.Clear();
            _aggressorLookup.Clear();
            _regen.Reset();

            _enemyTarget = null;
            _enemyTargetDetectors = null;
            _attackEnemyTarget = null;
            _resumePlayerAfterEngage = null;
            _forcingDoor = null;
        }

        // =====================================================================
        // Per-tick server upkeep
        // =====================================================================

        private void TickCombatServer(float dt)
        {
            float now = Time.time;

            MahoragaConfig? cfg = MahoragaConfig.Current;
            _playerAggro.HalfLifeSeconds = cfg != null ? cfg.RetaliationHalfLifeSeconds.Value : 6f;
            _enemyAggro.HalfLifeSeconds = cfg != null ? cfg.EnemyGrudgeHalfLifeSeconds.Value : 5f;

            TickPendingOldBirdDestructions(now);
            TickGrowthServer(cfg);

            // Slow out-of-combat regeneration (never while the stance is locked or after death).
            if (!isEnemyDead && currentBehaviourStateIndex != (int)MahoragaState.Adapting)
            {
                int maxHp = cfg != null ? cfg.MaxHP.Value : 18;
                float delay = cfg != null ? cfg.RegenDelaySeconds.Value : 8f;
                float pct = cfg != null ? cfg.RegenPercentPerSecond.Value : 0.01f;
                int heal = _regen.Tick(dt, now, _lastDamageTakenTime, enemyHP, maxHp, delay, pct);
                if (heal > 0)
                {
                    enemyHP = Mathf.Min(maxHp, enemyHP + heal);
                }
            }

            if (now - _lastPruneTime > 2f)
            {
                _lastPruneTime = now;
                _ignoredEnemies.Prune(now);
                PruneAggressorLookup(now);
            }
        }

        private void PruneAggressorLookup(float now)
        {
            if (_aggressorLookup.Count == 0)
            {
                return;
            }

            List<int>? stale = null;
            foreach (KeyValuePair<int, EnemyAI> kv in _aggressorLookup)
            {
                if (kv.Value == null || kv.Value.isEnemyDead || !_enemyAggro.Has(kv.Key, now))
                {
                    (stale ??= new List<int>()).Add(kv.Key);
                }
            }

            if (stale != null)
            {
                for (int i = 0; i < stale.Count; i++)
                {
                    _aggressorLookup.Remove(stale[i]);
                }
            }
        }

        // =====================================================================
        // Damage pipeline (called from HitEnemy on the server)
        // =====================================================================

        internal void ReceiveDirectOldBirdImpactServer(RadMechAI shooter, uint blast)
        {
            if (IsServer && !isEnemyDead && !IsSummonInvulnerable)
            {
                HandleDamageServer(6, null, false, -1, shooter, blast);
            }
        }

        private void HandleDamageServer(int force, PlayerControllerB? playerWhoHit, bool playHitSFX, int hitID,
            RadMechAI? directMissileSource = null, uint directBlast = 0)
        {
            float now = Time.time;
            MahoragaConfig? cfg = MahoragaConfig.Current;
            AdaptationSettings settings = cfg != null ? cfg.Adaptation : _fallbackAdaptation;
            int maxHp = cfg != null ? cfg.MaxHP.Value : 18;

            if (force <= 0)
            {
                return;
            }

            RadMechAI? missileSource = playerWhoHit == null
                ? directMissileSource ?? ExplosionTrackingPatch.MissileSource
                : null;
            uint blast = directMissileSource != null ? directBlast : ExplosionTrackingPatch.MissileBlastId;
            if (missileSource != null && blast != 0)
            {
                if (blast == _lastOldBirdDamageBlast)
                {
                    return;
                }
                _lastOldBirdDamageBlast = blast;
            }

            EnemyAI? contactSource = playerWhoHit == null ? EnemyHitSourcePatch.GetAttacker(this) : null;
            bool isExplosion = playerWhoHit == null && ExplosionTrackingPatch.InExplosion;
            AdaptationCategory category = ResolveDamageCategory(playerWhoHit, hitID, force,
                isExplosion, missileSource != null, contactSource != null);

            int hpFloor = 0;
            if (category == AdaptationCategory.Shovel && playerWhoHit != null)
            {
                if (playerWhoHit.isPlayerControlled && !playerWhoHit.isPlayerDead &&
                    _shovelStrike.Register(playerWhoHit.playerClientId, now, _adaptation.GetStage(category)))
                {
                    enemyHP = 0;
                    KillEnemyOnOwnerClient();
                    return;
                }
                hpFloor = 1;
            }

            bool coordinatedOldBirds = false;
            if (category == AdaptationCategory.OldBirdMissile && missileSource != null)
            {
                float window = cfg != null ? cfg.OldBirdKillWindowSeconds.Value : 6f;
                if (now - _oldBirdVolleyStartedAt > window)
                {
                    _oldBirdAttackers.Clear();
                    _oldBirdVolleyStartedAt = now;
                    _oldBirdVolleyStage = _adaptation.GetStage(category);
                }
                _oldBirdAttackers.Register(missileSource.GetInstanceID(), now);
                int required = (cfg != null ? cfg.OldBirdVolleyBaseCount.Value : 3) + _oldBirdVolleyStage;
                coordinatedOldBirds = _oldBirdAttackers.Count(now, window) >= required;
                if (!coordinatedOldBirds)
                {
                    hpFloor = Mathf.Max(1, Mathf.RoundToInt(maxHp * (cfg != null ? cfg.OldBirdSoloHpFloorFraction.Value : 0.30f)));
                }
            }

            bool isAdapting = currentBehaviourStateIndex == (int)MahoragaState.Adapting;
            DamageEvaluation eval = _adaptation.EvaluateDamage(category, force, enemyHP, maxHp, isAdapting, settings, now, hpFloor, minimumDamage: coordinatedOldBirds ? force : 0);
            enemyHP = eval.FinalHP;
            _lastDamageTakenTime = now;

            if (eval.IsLethal)
            {
                KillEnemyOnOwnerClient();
                return;
            }

            EnemyAI? enemyAttacker = missileSource != null ? missileSource : contactSource;
            if (enemyAttacker != null && (enemyAttacker is not RadMechAI || cfg == null || cfg.FightOldBirds.Value))
            {
                RegisterEnemyAggression(enemyAttacker, missileSource != null ? 3f : 1f, now);
            }

            if (playerWhoHit != null && PlayerIsTargetable(playerWhoHit))
            {
                RegisterPlayerAggression(playerWhoHit, force, now);
                if (currentBehaviourStateIndex == (int)MahoragaState.EngagingEnemy)
                {
                    _resumePlayerAfterEngage ??= playerWhoHit;
                }
                else if (currentBehaviourStateIndex == (int)MahoragaState.Patrolling ||
                         currentBehaviourStateIndex == (int)MahoragaState.Investigating)
                {
                    if (!EvaluateEnemyEngagementServer(cfg))
                    {
                        EnterChaseStateServer(playerWhoHit);
                    }
                    else
                    {
                        _resumePlayerAfterEngage ??= playerWhoHit;
                    }
                }
                else if (targetPlayer == null)
                {
                    targetPlayer = playerWhoHit;
                }
            }

            if (category == AdaptationCategory.OldBirdMissile && missileSource is RadMechAI oldBird)
            {
                OnOldBirdDamagedMe(oldBird, blast);
            }

            // ---- Wheel (+45 deg on every adaptation stage AND every Old Bird hit / growth step) ----
            if (eval.TriggeredAdaptation)
            {
                EnterAdaptationStateServer(category, eval.FinalHP);
                return;
            }

            if (eval.TriggeredWheelClick)
            {
                TriggerWheelClickClientRpc((byte)category, (byte)eval.StageAfter, _adaptation.AdaptedMask, _adaptation.WheelStepCount, enemyHP, playSpinAnim: category == AdaptationCategory.OldBirdMissile);
            }
            else if (category == AdaptationCategory.OldBirdMissile && !isAdapting)
            {
                int newWheelSteps = _adaptation.AdvanceWheelStep();
                byte stage = (byte)_adaptation.GetStage(category);
                TriggerWheelClickClientRpc((byte)category, stage, _adaptation.AdaptedMask, newWheelSteps, enemyHP, playSpinAnim: true);
            }
            else if (eval.WasAlreadyAdapted)
            {
                if (_deflectRpcCooldownTimer <= 0f)
                {
                    _deflectRpcCooldownTimer = 0.25f;
                    PlayDeflectFeedbackClientRpc((byte)category);
                }
            }
            else if (playHitSFX)
            {
                PlayAttackImpactClientRpc(isSlam: false, transform.position + Vector3.up * 1.8f * ScaleFactor);
            }
        }

        private AdaptationCategory ResolveDamageCategory(PlayerControllerB? player, int hitId, int force,
            bool isExplosion, bool isOldBirdMissile, bool isEnemyMelee)
        {
            GrabbableObject? held = player?.currentlyHeldObjectServer;
            if (hitId == 1 && held is Shovel)
            {
                return AdaptationCategory.Shovel;
            }
            if (hitId == 5 && held is KnifeItem)
            {
                return AdaptationCategory.Knife;
            }

            AdaptationCategory category = MahoragaAdaptationSystem.ClassifyDamage(hitId, force, player != null,
                heldIsShovelOrKnife: false, heldIsShotgun: held is ShotgunItem,
                recentExplosionNearby: isExplosion, recentOldBirdMissileNearby: isOldBirdMissile,
                enemyAttackerSuspected: isEnemyMelee, allowUnidentifiedShotgun: false);
            if (category != AdaptationCategory.Other || held == null || held.GetType().Assembly == typeof(GrabbableObject).Assembly)
            {
                return category;
            }

            Type itemType = held.GetType();
            if (_customWeaponCategories.TryGetValue(itemType, out AdaptationCategory custom))
            {
                return custom;
            }

            int next = MahoragaAdaptationSystem.FirstCustomWeaponCategory + _customWeaponCategories.Count;
            if (next >= MahoragaAdaptationSystem.CategoryCount)
            {
                return category;
            }

            custom = (AdaptationCategory)next;
            _customWeaponCategories.Add(itemType, custom);
            return custom;
        }

        private void RegisterPlayerAggression(PlayerControllerB player, int force, float now)
        {
            _playerAggro.Add((int)player.playerClientId, 1f + Mathf.Min(Mathf.Max(force, 0), 6) * 0.25f, now);
            if (targetPlayer == player)
            {
                RememberPlayerContact(player.transform.position);
            }
        }

        private void RegisterEnemyAggression(EnemyAI enemy, float weight, float now)
        {
            if (enemy == this || enemy is MahoragaAI || enemy.isEnemyDead || enemy.isOutside != isOutside)
            {
                return;
            }

            int id = enemy.GetInstanceID();
            _enemyAggro.Add(id, weight, now);
            _aggressorLookup[id] = enemy;
        }

        private bool IsEnemyEngageable(EnemyAI? e)
        {
            return e != null && e is not MahoragaAI && !e.isEnemyDead && e.isOutside == isOutside
                && (e is not RadMechAI rad || !IsDestructionPending(rad))
                && !_ignoredEnemies.IsBlocked(e.GetInstanceID(), Time.time);
        }

        private PlayerControllerB? GetRetaliationPlayer(float minWeight = 0f)
        {
            float now = Time.time;
            if (_playerAggro.TryGetTop(now, out int id, out float weight, _acceptPlayerAggressor) && weight >= minWeight)
            {
                return ResolvePlayerByClientId(id);
            }
            return null;
        }

        /// <summary>Player to chase once an attack/adaptation/fight ends: the retaliation target wins over the old one.</summary>
        private PlayerControllerB? ChoosePlayerAfterAction(PlayerControllerB? fallback)
        {
            PlayerControllerB? retaliation = GetRetaliationPlayer();
            if (retaliation != null)
            {
                return retaliation;
            }
            return fallback != null && CanSenseByProximity(fallback) ? fallback : null;
        }

        /// <summary>
        /// Chase-time hook: if a different player is hurting us more than the current target (with hysteresis) switch to them.
        /// Returns true when the target changed.
        /// </summary>
        private bool TryRetaliationRetargetServer(MahoragaConfig? cfg)
        {
            float now = Time.time;
            if (!_playerAggro.TryGetTop(now, out int topId, out _, _acceptPlayerAggressor))
            {
                return false;
            }

            int currentId = targetPlayer != null ? (int)targetPlayer.playerClientId : -1;
            if (topId == currentId)
            {
                return false;
            }

            float lockout = cfg != null ? cfg.RetaliationSwitchLockoutSeconds.Value : 2.5f;
            if (now - _lastRetargetTime < lockout || !_playerAggro.ShouldSwitch(currentId, topId, now))
            {
                return false;
            }

            PlayerControllerB? p = ResolvePlayerByClientId(topId);
            if (p == null)
            {
                return false;
            }

            _lastRetargetTime = now;
            SetChaseTargetServer(p);
            return true;
        }

        /// <summary>
        /// Called every AI interval while Patrolling / Investigating / Chasing.
        /// Old Bird Priority #1: if FightOldBirds is enabled and any living engageable RadMechAI exists in Mahoraga's domain
        /// within OldBirdSeekRange (default 250m), Mahoraga immediately engages it, overriding player chases or patrols.
        /// Returns true if the state changed (caller must stop its own interval logic).
        /// </summary>
        private bool EvaluateEnemyEngagementServer(MahoragaConfig? cfg)
        {
            float now = Time.time;
            Vector3 chest = transform.position + Vector3.up * 1.4f * ScaleFactor;
            PlayerControllerB? resumePlayer = ChoosePlayerAfterAction(targetPlayer);

            if (cfg == null || cfg.FightOldBirds.Value)
            {
                RadMechAI? hunted = FindNearestOldBirdServer(cfg, chest, out _);
                if (hunted != null)
                {
                    EnterEngageEnemyStateServer(hunted, resumePlayer);
                    return true;
                }
            }

            if (_enemyAggro.TryGetTop(now, out int id, out _, _acceptEnemyAggressor) &&
                _aggressorLookup.TryGetValue(id, out EnemyAI? aggressor) && aggressor is not RadMechAI &&
                DistanceToEnemySurface(aggressor, chest) <= EnemyAggressorMaxDistance)
            {
                EnterEngageEnemyStateServer(aggressor, resumePlayer);
                return true;
            }

            return false;
        }

        private RadMechAI? FindNearestOldBirdServer(MahoragaConfig? cfg, Vector3 chest, out float distance)
        {
            distance = float.MaxValue;
            if (RoundManager.Instance == null)
            {
                return null;
            }

            float range = cfg != null ? cfg.OldBirdSeekRange.Value : 250f;
            RadMechAI? best = null;
            List<EnemyAI> all = RoundManager.Instance.SpawnedEnemies;
            for (int i = 0; i < all.Count; i++)
            {
                if (!(all[i] is RadMechAI rad) || !IsEnemyEngageable(rad))
                {
                    continue;
                }

                float d = DistanceToEnemySurface(rad, chest);
                if (d < distance && d <= range)
                {
                    distance = d;
                    best = rad;
                }
            }

            return best;
        }

        // =====================================================================
        // EngagingEnemy state (6) — Old Birds only
        // =====================================================================

        private Vector3 GetOldBirdApproachPosition(RadMechAI oldBird, out float sampleRadius)
        {
            if (oldBird.inFlyingMode)
            {
                sampleRadius = 20f;
                int mask = StartOfRound.Instance != null ? StartOfRound.Instance.collidersAndRoomMaskAndDefault : ~0;
                if (Physics.Raycast(oldBird.transform.position + Vector3.up * 1.0f, Vector3.down, out RaycastHit groundHit, 60f, mask, QueryTriggerInteraction.Ignore))
                {
                    return groundHit.point;
                }
                return oldBird.transform.position;
            }

            sampleRadius = 2.5f;
            Vector3 offset = Vector3.ProjectOnPlane(transform.position - oldBird.transform.position, Vector3.up);
            float standoff = Mathf.Min(CleaveReach * ScaleFactor * 0.6f,
                (agent?.radius ?? 0.85f) + (oldBird.agent?.radius ?? 1f));
            return oldBird.transform.position + offset.normalized * standoff;
        }

        private void EnterEngageEnemyStateServer(EnemyAI enemy, PlayerControllerB? resumePlayer)
        {
            _forcingDoor = null;
            StopSearch(currentSearch);
            _enemyTarget = enemy;
            _enemyTargetDetectors = enemy.GetComponentsInChildren<EnemyAICollisionDetect>(includeInactive: false);
            _engageStartTime = Time.time;
            _engageUnreachableTimer = 0f;
            _engageRepathCounter = 0;
            _resumePlayerAfterEngage = resumePlayer;
            targetPlayer = null;
            movingTowardsTargetPlayer = false;

            SwitchBehaviourAndSyncServer(MahoragaState.EngagingEnemy, -1);
            Vector3 approachPos = GetEnemyApproach(enemy, out float sampleRadius);
            SetSafeDestinationServer(approachPos, sampleRadius);
        }

        private Vector3 GetEnemyApproach(EnemyAI enemy, out float sampleRadius)
        {
            if (enemy is RadMechAI bird)
            {
                return GetOldBirdApproachPosition(bird, out sampleRadius);
            }
            sampleRadius = 2f;
            return enemy.transform.position;
        }

        private void RunEngageEnemyInterval(MahoragaConfig? cfg)
        {
            float now = Time.time;
            EnemyAI? enemy = _enemyTarget;
            RadMechAI? oldBird = enemy as RadMechAI;
            float baseChase = cfg != null ? cfg.ChaseSpeed.Value : 6.2f;
            float bonusPerAdapt = cfg != null ? cfg.ChaseSpeedPerAdaptation.Value : 0.35f;
            if (agent != null && agent.enabled)
            {
                agent.speed = baseChase + Mathf.Min(4, _adaptation.AdaptedCategoryCount) * bonusPerAdapt;
            }
            openDoorSpeedMultiplier = 1.65f;

            bool valid = IsEnemyEngageable(enemy) && (oldBird != null ||
                (enemy != null && _enemyAggro.Has(enemy.GetInstanceID(), now)));
            Vector3 chest = transform.position + Vector3.up * 1.4f * ScaleFactor;
            float dist = valid ? DistanceToEnemySurface(enemy!, chest) : 0f;

            float seekRange = cfg != null ? cfg.OldBirdSeekRange.Value : 250f;
            float maxSeconds = oldBird != null ? (cfg != null ? cfg.OldBirdEngageMaxSeconds.Value : 120f) : 15f;
            float maxDistance = oldBird != null ? Mathf.Max(EnemyMaxChaseDistance, seekRange + 40f) : EnemyAggressorMaxDistance;

            EngageVerdict verdict = EnemyEngagementPolicy.EvaluateOldBird(
                valid,
                now - _engageStartTime,
                maxSeconds,
                _engageUnreachableTimer,
                EnemyUnreachableLimitSeconds,
                dist,
                maxDistance);

            if (verdict != EngageVerdict.Continue)
            {
                ExitEngageEnemyStateServer(verdict, cfg);
                return;
            }

            if (TryScanForLockedDoorServer(cfg))
            {
                return;
            }

            if (oldBird != null)
            {
                bool slam = ShouldUseSlamAttack(forceSlam: false);
                if (!_jumping && _attackCooldownTimer <= 0f &&
                    TryGetOldBirdStrikePoint(oldBird, slam, requireFacing: false, out _))
                {
                    EnterAttackStateServer(slam, oldBird);
                    return;
                }
            }

            Vector3 approachPos = GetEnemyApproach(enemy!, out float sampleRadius);
            bool pathOk = SetSafeDestinationServer(approachPos, sampleRadius);
            if (!pathOk && oldBird != null && !oldBird.inFlyingMode)
            {
                pathOk = SetSafeDestinationServer(oldBird.transform.position, sampleRadius);
            }

            bool validatePath = (_engageRepathCounter++ % 3) == 0;
            if (validatePath)
            {
                bool stuckOnPartial = agent != null
                    && agent.enabled
                    && agent.isOnNavMesh
                    && agent.pathStatus == NavMeshPathStatus.PathPartial
                    && !agent.pathPending
                    && agent.remainingDistance <= 1.2f
                    && dist > EngageEntryRangeBase * ScaleFactor * 1.6f;

                bool reachable = (pathOk && !stuckOnPartial) || dist <= EngageEntryRangeBase * ScaleFactor * 1.6f;
                _engageUnreachableTimer = reachable
                    ? 0f
                    : _engageUnreachableTimer + AIIntervalTime * 3f;
            }

            if (oldBird == null && !_jumping && _attackCooldownTimer <= 0f && dist <= EngageEntryRangeBase * ScaleFactor)
            {
                TryGetEnemySurfacePoint(enemy!, chest, _enemyTargetDetectors, out Vector3 surface);
                if (Mathf.Abs(surface.y - chest.y) <= VerticalHitTolerance * ScaleFactor * 1.4f
                    && HasClearStrikePath(transform.position + Vector3.up * 1.8f * ScaleFactor, surface, enemy))
                {
                    EnterAttackStateServer(ShouldUseSlamAttack(forceSlam: false), enemy);
                }
            }
        }

        private void ExitEngageEnemyStateServer(EngageVerdict verdict, MahoragaConfig? cfg)
        {
            float now = Time.time;
            EnemyAI? e = _enemyTarget;
            if (e != null)
            {
                int id = e.GetInstanceID();
                switch (verdict)
                {
                    case EngageVerdict.TimedOut:
                    case EngageVerdict.Unreachable:
                    case EngageVerdict.TooFar:
                        _ignoredEnemies.Block(id, now + (cfg != null ? cfg.DisengageCooldownSeconds.Value : 30f));
                        _enemyAggro.Remove(id);
                        break;
                    case EngageVerdict.TargetGone:
                        _enemyAggro.Remove(id);
                        break;
                }
            }

            PlayerControllerB? resume = _resumePlayerAfterEngage;
            _enemyTarget = null;
            _enemyTargetDetectors = null;
            _attackEnemyTarget = null;
            _resumePlayerAfterEngage = null;

            // If another living engageable Old Bird is present, engage it before falling back to players.
            if (EvaluateEnemyEngagementServer(cfg))
            {
                return;
            }

            PlayerControllerB? next = ChoosePlayerAfterAction(resume)
                ?? FindNearbyPlayer();
            if (next != null)
            {
                EnterChaseStateServer(next);
            }
            else
            {
                EnterPatrolStateServer();
            }
        }

        /// <summary>After an attack that targeted an Old Bird: go back to fighting it if it is still valid.</summary>
        private bool TryReturnToEnemyEngagementAfterAttack()
        {
            EnemyAI? e = _attackEnemyTarget;
            _attackEnemyTarget = null;
            if (!IsEnemyEngageable(e) || _enemyTarget != e)
            {
                if (_enemyTarget != null)
                {
                    ExitEngageEnemyStateServer(EngageVerdict.TargetGone, MahoragaConfig.Current);
                    return true;
                }
                return false;
            }

            SwitchBehaviourAndSyncServer(MahoragaState.EngagingEnemy, -1);
            return true;
        }

        // =====================================================================
        // Hit resolution against enemies (Old Birds ONLY)
        // =====================================================================

        /// <summary>Returns true if at least one Old Bird was hit. Wall line-of-sight is checked per target.</summary>
        private bool ResolveEnemyHitsServer(bool isSlam, float reach, Vector3 rayOrigin, MahoragaConfig? cfg)
        {
            if (_attackEnemyTarget is EnemyAI aggressor && aggressor is not RadMechAI)
            {
                if (!IsEnemyEngageable(aggressor) || !_enemyAggro.Has(aggressor.GetInstanceID(), Time.time) ||
                    !TryGetEnemySurfacePoint(aggressor, rayOrigin, _enemyTargetDetectors, out Vector3 surface) ||
                    (surface - rayOrigin).sqrMagnitude > reach * reach ||
                    !HasClearStrikePath(rayOrigin, surface, aggressor))
                {
                    return false;
                }

                aggressor.HitEnemyOnLocalClient(isSlam ? 3 : 2, transform.forward, null, playHitSFX: true);
                return true;
            }

            if (RoundManager.Instance == null || (cfg != null && !cfg.FightOldBirds.Value))
            {
                return false;
            }

            bool anyHit = false;
            List<EnemyAI> all = RoundManager.Instance.SpawnedEnemies;
            for (int i = 0; i < all.Count; i++)
            {
                if (!(all[i] is RadMechAI oldBird) || oldBird.isEnemyDead || oldBird.isOutside != isOutside ||
                    IsDestructionPending(oldBird) || !TryGetOldBirdStrikePoint(oldBird, isSlam, requireFacing: true, out _))
                {
                    continue;
                }

                StrikeOldBirdServer(oldBird, isSlam, cfg);
                anyHit = true;
            }

            return anyHit;
        }

        // =====================================================================
        // Geometry helpers
        // =====================================================================

        /// <summary>Strike line of sight: blocked only by level geometry, never by the target's (or our own) body.</summary>
        private bool HasClearStrikePath(Vector3 from, Vector3 to, EnemyAI? target)
        {
            if (StartOfRound.Instance == null)
            {
                return true;
            }

            if (!Physics.Linecast(from, to, out RaycastHit hit, StartOfRound.Instance.collidersAndRoomMaskAndDefault, QueryTriggerInteraction.Ignore))
            {
                return true;
            }

            EnemyAI? owner = hit.collider != null ? hit.collider.GetComponentInParent<EnemyAI>() : null;
            return owner != null && (owner == target || owner == this);
        }

        private bool HasClearStrikePathToPlayer(PlayerControllerB player)
        {
            if (player == null)
            {
                return false;
            }

            Vector3 rayOrigin = transform.position + Vector3.up * (1.8f * ScaleFactor);
            return HasClearStrikePathToPlayer(rayOrigin, player);
        }

        /// <summary>Wall check against a player: chest, head or feet must be reachable (so legs behind a railing still count).</summary>
        private bool HasClearStrikePathToPlayer(Vector3 from, PlayerControllerB p)
        {
            if (StartOfRound.Instance == null)
            {
                return true;
            }

            int mask = StartOfRound.Instance.collidersAndRoomMaskAndDefault;
            Vector3 feet = p.transform.position;
            Vector3 head = p.gameplayCamera != null ? p.gameplayCamera.transform.position : feet + Vector3.up * 2.2f;
            return !Physics.Linecast(from, feet + Vector3.up * 1.3f, mask, QueryTriggerInteraction.Ignore)
                || !Physics.Linecast(from, head, mask, QueryTriggerInteraction.Ignore)
                || !Physics.Linecast(from, feet + Vector3.up * 0.4f, mask, QueryTriggerInteraction.Ignore);
        }

        private bool TryGetOldBirdStrikePoint(RadMechAI bird, bool isSlam, bool requireFacing, out Vector3 point)
        {
            float scale = ScaleFactor;
            float standHeight = Mathf.Min(1.4f * scale, 2.6f);
            Vector3 strikeOrigin = transform.position + Vector3.up * standHeight;

            // The vanilla EnemyAICollisionDetect is ClawTrigger. Its position depends on the attacking arm.
            // The bird's own collider tracks its body regardless of the claw or flight animation.
            if (bird.ownCollider != null && bird.ownCollider.enabled)
            {
                var collider = bird.ownCollider;
                point = collider is MeshCollider mesh && !mesh.convex
                    ? collider.bounds.ClosestPoint(strikeOrigin)
                    : collider.ClosestPoint(strikeOrigin);
                if (CanStrikeOldBirdPoint(bird, point, strikeOrigin, isSlam, requireFacing))
                {
                    return true;
                }

                point = collider.bounds.center;
                if (CanStrikeOldBirdPoint(bird, point, strikeOrigin, isSlam, requireFacing))
                {
                    return true;
                }
            }

            point = bird.centerPosition != null ? bird.centerPosition.position : bird.transform.position + Vector3.up * 1.6f;
            return CanStrikeOldBirdPoint(bird, point, strikeOrigin, isSlam, requireFacing);
        }

        private bool CanStrikeOldBirdPoint(RadMechAI bird, Vector3 point, Vector3 from, bool isSlam, bool requireFacing)
        {
            Vector3 delta = point - from;
            if (Mathf.Abs(delta.y) > VerticalHitTolerance * ScaleFactor * 1.4f)
            {
                return false;
            }

            delta.y = 0f;
            float reach = (isSlam ? SlamReach : CleaveReach) * ScaleFactor;
            if (delta.sqrMagnitude > reach * reach ||
                (requireFacing && !isSlam && delta.sqrMagnitude > 0.25f &&
                    Vector3.Angle(transform.forward, delta) > CleaveHalfArcDeg + 20f))
            {
                return false;
            }
            return HasClearStrikePath(from, point, bird);
        }

        private static EnemyAICollisionDetect[]? GetDetectors(EnemyAI e, EnemyAICollisionDetect[]? cached)
        {
            if (cached != null && cached.Length > 0)
            {
                return cached;
            }
            return e.GetComponentsInChildren<EnemyAICollisionDetect>(includeInactive: false);
        }

        /// <summary>Closest point of an enemy's hit colliders (AABB) to <paramref name="from"/>; falls back to its transform.</summary>
        private static bool TryGetEnemySurfacePoint(EnemyAI e, Vector3 from, EnemyAICollisionDetect[]? cached, out Vector3 point)
        {
            point = e.transform.position;
            EnemyAICollisionDetect[]? dets = GetDetectors(e, cached);
            bool found = false;
            float best = float.MaxValue;
            if (dets != null)
            {
                for (int i = 0; i < dets.Length; i++)
                {
                    if (dets[i] == null)
                    {
                        continue;
                    }

                    Collider? c = dets[i].GetComponent<Collider>();
                    if (c == null || !c.enabled)
                    {
                        continue;
                    }

                    Vector3 p = c.bounds.ClosestPoint(from);
                    float d = (p - from).sqrMagnitude;
                    if (d < best)
                    {
                        best = d;
                        point = p;
                        found = true;
                    }
                }
            }

            if (!found)
            {
                point = e.transform.position + Vector3.up * 0.8f;
            }

            return true;
        }

        private float DistanceToEnemySurface(EnemyAI e, Vector3 from)
        {
            Vector3 point;
            if (e is RadMechAI bird && bird.ownCollider != null && bird.ownCollider.enabled)
            {
                point = bird.ownCollider.bounds.ClosestPoint(from);
            }
            else
            {
                TryGetEnemySurfacePoint(e, from, e == _enemyTarget ? _enemyTargetDetectors : null, out point);
            }
            Vector3 d = point - from;
            d.y = 0f;
            return d.magnitude;
        }
    }
}
