using System;
using System.Collections.Generic;
using GameNetcodeStuff;
using MahoragaLC.Config;
using MahoragaLC.Patches;
using UnityEngine;
using UnityEngine.AI;

namespace MahoragaLC.AI
{
    /// <summary>
    /// Retaliation (players and Old Birds), the EngagingEnemy state (exclusively for RadMechAI Old Birds),
    /// attack resolution against Old Birds, and the damage/adaptation pipeline. Everything here is server-authoritative;
    /// clients only receive effects through RPCs.
    ///
    /// Target exclusivity: Mahoraga attacks ONLY Players and RadMechAI (Old Birds) — never any other enemy,
    /// not even in self-defense, while blocked, or while idling.
    /// </summary>
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
        private readonly RegenerationTracker _regen = new RegenerationTracker();
        private readonly Dictionary<int, EnemyAI> _aggressorLookup = new Dictionary<int, EnemyAI>();
        private readonly Dictionary<int, float> _enemyLastHitTime = new Dictionary<int, float>();
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
            _pendingDestructions.Clear();
            _oldBirdDurability.Clear();
            _oldBirdHeights.Clear();
            _pushes.Clear();
            _playerAggro.Clear();
            _enemyAggro.Clear();
            _ignoredEnemies.Clear();
            _oldBirdAttackers.Clear();
            _aggressorLookup.Clear();
            _enemyLastHitTime.Clear();
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
                    _enemyLastHitTime.Remove(stale[i]);
                }
            }
        }

        // =====================================================================
        // Damage pipeline (called from HitEnemy on the server)
        // =====================================================================

        private void HandleDamageServer(int force, PlayerControllerB? playerWhoHit, bool playHitSFX, int hitID)
        {
            float now = Time.time;
            MahoragaConfig? cfg = MahoragaConfig.Current;
            AdaptationSettings settings = cfg != null ? cfg.Adaptation : _fallbackAdaptation;
            int maxHp = cfg != null ? cfg.MaxHP.Value : 18;

            bool heldIsShovelOrKnife = playerWhoHit != null &&
                (playerWhoHit.currentlyHeldObjectServer is Shovel || playerWhoHit.currentlyHeldObjectServer is KnifeItem);
            bool heldIsShotgun = playerWhoHit != null && playerWhoHit.currentlyHeldObjectServer is ShotgunItem;
            bool recentExplosion = (now - _lastHitFromExplosionTime <= 0.15f)
                || ExplosionTrackingPatch.HasRecentExplosionNear(transform.position, now, extraMargin: 5.0f + 1.5f * ScaleFactor);
            bool recentMissile = ExplosionTrackingPatch.HasRecentMissileNear(transform.position, now, out EnemyAI? missileSource, extraMargin: 2.0f + 2.0f * ScaleFactor);

            // Used ONLY to classify AdaptationCategory.EnemyMelee for Dharma Wheel adaptation; never triggers retaliation against non-Old-Bird enemies.
            EnemyAI? suspectedEnemy = null;
            if (playerWhoHit == null && !recentMissile && !recentExplosion && !(force == 6 && hitID == -1))
            {
                suspectedEnemy = FindSuspectedEnemyAttackerServer();
            }

            AdaptationCategory category = MahoragaAdaptationSystem.ClassifyDamage(
                hitID,
                force,
                hasAttacker: playerWhoHit != null,
                heldIsShovelOrKnife: heldIsShovelOrKnife,
                heldIsShotgun: heldIsShotgun,
                recentExplosionNearby: recentExplosion,
                recentOldBirdMissileNearby: recentMissile,
                enemyAttackerSuspected: suspectedEnemy != null);

            // A single Old Bird can never finish Mahoraga: its missiles stop at an HP floor unless a whole flock is shooting.
            int hpFloor = 0;
            if (category == AdaptationCategory.OldBirdMissile)
            {
                _oldBirdAttackers.Register(missileSource != null ? missileSource.GetInstanceID() : 0, now);
                int required = cfg != null ? cfg.OldBirdsRequiredToKill.Value : 5;
                float window = cfg != null ? cfg.OldBirdKillWindowSeconds.Value : 6f;
                if (_oldBirdAttackers.Count(now, window) < required)
                {
                    float frac = cfg != null ? cfg.OldBirdSoloHpFloorFraction.Value : 0.30f;
                    hpFloor = Mathf.Max(1, Mathf.RoundToInt(maxHp * frac));
                }
            }

            bool isAdapting = currentBehaviourStateIndex == (int)MahoragaState.Adapting;
            DamageEvaluation eval = _adaptation.EvaluateDamage(category, force, enemyHP, maxHp, isAdapting, settings, now, hpFloor);
            enemyHP = eval.FinalHP;
            _lastDamageTakenTime = now;

            if (eval.IsLethal)
            {
                KillEnemyOnOwnerClient();
                return;
            }

            // ---- Retaliation bookkeeping (Players and RadMechAI Old Birds only) --------------------
            EnemyAI? enemyAttacker = missileSource != null && recentMissile ? missileSource : suspectedEnemy;
            if (enemyAttacker is RadMechAI && (cfg == null || cfg.FightOldBirds.Value))
            {
                RegisterEnemyAggression(enemyAttacker, recentMissile ? 3f : 1f, now);
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
                OnOldBirdDamagedMe(oldBird, now);
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

        private void RegisterPlayerAggression(PlayerControllerB player, int force, float now)
        {
            _playerAggro.Add((int)player.playerClientId, 1f + Mathf.Min(Mathf.Max(force, 0), 6) * 0.25f, now);
        }

        private void RegisterEnemyAggression(EnemyAI enemy, float weight, float now)
        {
            if (!(enemy is RadMechAI))
            {
                return;
            }

            int id = enemy.GetInstanceID();
            _enemyAggro.Add(id, weight, now);
            _aggressorLookup[id] = enemy;
            _enemyLastHitTime[id] = now;
        }

        /// <summary>Nearest living, non-Mahoraga enemy whose body is within reach (used strictly for EnemyMelee adaptation classification).</summary>
        private EnemyAI? FindSuspectedEnemyAttackerServer()
        {
            if (RoundManager.Instance == null)
            {
                return null;
            }

            List<EnemyAI> all = RoundManager.Instance.SpawnedEnemies;
            EnemyAI? best = null;
            float bestDist = 6.5f * ScaleFactor;
            Vector3 chest = transform.position + Vector3.up * 1.4f * ScaleFactor;
            for (int i = 0; i < all.Count; i++)
            {
                EnemyAI e = all[i];
                if (e == null || e == this || e is MahoragaAI || e.isEnemyDead)
                {
                    continue;
                }

                if (!TryGetEnemySurfacePoint(e, chest, null, out Vector3 p))
                {
                    continue;
                }

                float d = Vector3.Distance(chest, p);
                if (d < bestDist)
                {
                    bestDist = d;
                    best = e;
                }
            }

            return best;
        }

        // =====================================================================
        // Priorities: Old Birds (#1) -> Players (#2)
        // =====================================================================

        private bool IsEnemyEngageable(EnemyAI? e)
        {
            return e is RadMechAI rad
                && rad != this
                && !rad.isEnemyDead
                && rad.isOutside == isOutside
                && !IsDestructionPending(rad)
                && !_ignoredEnemies.IsBlocked(rad.GetInstanceID(), Time.time);
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
            return fallback != null && PlayerIsTargetable(fallback) ? fallback : null;
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

        /// <summary>True while the current chase target is an active aggressor: keeps the generic "closer player" pivot from stealing it.</summary>
        private bool CurrentTargetIsAggressor()
        {
            return targetPlayer != null && _playerAggro.Has((int)targetPlayer.playerClientId, Time.time);
        }

        /// <summary>
        /// Called every AI interval while Patrolling / Investigating / Chasing.
        /// Old Bird Priority #1: if FightOldBirds is enabled and any living engageable RadMechAI exists in Mahoraga's domain
        /// within OldBirdSeekRange (default 250m), Mahoraga immediately engages it, overriding player chases or patrols.
        /// Returns true if the state changed (caller must stop its own interval logic).
        /// </summary>
        private bool EvaluateEnemyEngagementServer(MahoragaConfig? cfg)
        {
            if (cfg != null && !cfg.FightOldBirds.Value)
            {
                return false;
            }

            float now = Time.time;
            float seekRange = cfg != null ? cfg.OldBirdSeekRange.Value : 250f;
            float aggressorRange = Mathf.Max(EnemyAggressorMaxDistance, seekRange);
            Vector3 chest = transform.position + Vector3.up * 1.4f * ScaleFactor;

            PlayerControllerB? resumePlayer = currentBehaviourStateIndex == (int)MahoragaState.Chasing && targetPlayer != null && PlayerIsTargetable(targetPlayer)
                ? targetPlayer
                : ChoosePlayerAfterAction(null);

            // 1. Prefer an Old Bird that recently attacked Mahoraga.
            if (_enemyAggro.TryGetTop(now, out int aggId, out _, _acceptEnemyAggressor) &&
                _aggressorLookup.TryGetValue(aggId, out EnemyAI? aggressor) &&
                aggressor is RadMechAI aggOldBird &&
                DistanceToEnemySurface(aggOldBird, chest) <= aggressorRange)
            {
                EnterEngageEnemyStateServer(aggOldBird, resumePlayer);
                return true;
            }

            // 2. Otherwise immediately hunt the nearest living Old Bird in our domain within OldBirdSeekRange.
            RadMechAI? hunted = FindNearestOldBirdServer(cfg, chest, out _);
            if (hunted != null)
            {
                EnterEngageEnemyStateServer(hunted, resumePlayer);
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

            sampleRadius = 4f;
            return oldBird.transform.position;
        }

        private void EnterEngageEnemyStateServer(RadMechAI oldBird, PlayerControllerB? resumePlayer)
        {
            _forcingDoor = null;
            StopSearch(currentSearch);
            _enemyTarget = oldBird;
            _enemyTargetDetectors = oldBird.GetComponentsInChildren<EnemyAICollisionDetect>(includeInactive: false);
            _engageStartTime = Time.time;
            _engageUnreachableTimer = 0f;
            _engageRepathCounter = 0;
            _resumePlayerAfterEngage = resumePlayer;
            targetPlayer = null;
            movingTowardsTargetPlayer = false;

            SwitchBehaviourAndSyncServer(MahoragaState.EngagingEnemy, -1);
            Vector3 approachPos = GetOldBirdApproachPosition(oldBird, out float sampleRadius);
            SetSafeDestinationServer(approachPos, sampleRadius);
        }

        private void RunEngageEnemyInterval(MahoragaConfig? cfg)
        {
            float now = Time.time;
            RadMechAI? oldBird = _enemyTarget as RadMechAI;
            float baseChase = cfg != null ? cfg.ChaseSpeed.Value : 6.2f;
            float bonusPerAdapt = cfg != null ? cfg.ChaseSpeedPerAdaptation.Value : 0.35f;
            if (agent != null && agent.enabled)
            {
                agent.speed = baseChase + Mathf.Min(4, _adaptation.AdaptedCategoryCount) * bonusPerAdapt;
            }
            openDoorSpeedMultiplier = 1.65f;

            bool valid = oldBird != null
                && !oldBird.isEnemyDead
                && oldBird.isOutside == isOutside
                && !IsDestructionPending(oldBird);
            Vector3 chest = transform.position + Vector3.up * 1.4f * ScaleFactor;
            float dist = valid ? DistanceToEnemySurface(oldBird!, chest) : 0f;

            float seekRange = cfg != null ? cfg.OldBirdSeekRange.Value : 250f;
            float maxSeconds = cfg != null ? cfg.OldBirdEngageMaxSeconds.Value : 120f;
            float maxDistance = Mathf.Max(EnemyMaxChaseDistance, seekRange + 40f);

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

            Vector3 approachPos = GetOldBirdApproachPosition(oldBird!, out float sampleRadius);
            bool pathOk = SetSafeDestinationServer(approachPos, sampleRadius);

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

            // Swing when in horizontal and vertical reach (never swing at a flying Old Bird high in the sky).
            if (_attackCooldownTimer <= 0f && !oldBird!.inFlyingMode && dist <= EngageEntryRangeBase * ScaleFactor)
            {
                TryGetEnemySurfacePoint(oldBird, chest, _enemyTargetDetectors, out Vector3 surface);
                if (Mathf.Abs(surface.y - chest.y) <= VerticalHitTolerance * ScaleFactor * 1.4f
                    && HasClearStrikePath(transform.position + Vector3.up * 1.8f * ScaleFactor, surface, oldBird))
                {
                    EnterAttackStateServer(ShouldUseSlamAttack(forceSlam: false), oldBird);
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
                ?? FindBestVisibleOrNearbyPlayer(viewHalfAngle: 120f, sightRange: 30f, proximityRadius: 8f);
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
            if (!(e is RadMechAI rad) || rad.isEnemyDead || _enemyTarget != rad || rad.isOutside != isOutside || IsDestructionPending(rad))
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
            if (RoundManager.Instance == null || (cfg != null && !cfg.FightOldBirds.Value))
            {
                return false;
            }

            float scale = ScaleFactor;
            Vector3 chest = transform.position + Vector3.up * 1.4f * scale;

            bool anyHit = false;
            List<EnemyAI> all = RoundManager.Instance.SpawnedEnemies;
            for (int i = 0; i < all.Count; i++)
            {
                if (!(all[i] is RadMechAI oldBird)
                    || oldBird == this
                    || oldBird.isEnemyDead
                    || oldBird.isOutside != isOutside
                    || IsDestructionPending(oldBird))
                {
                    continue;
                }

                EnemyAICollisionDetect[]? dets = oldBird == _attackEnemyTarget ? _enemyTargetDetectors : null;
                if (!TryGetEnemySurfacePoint(oldBird, chest, dets, out Vector3 p))
                {
                    continue;
                }

                Vector3 flat = p - transform.position;
                flat.y = 0f;
                float horiz = flat.magnitude;
                if (horiz > reach || Mathf.Abs(p.y - chest.y) > VerticalHitTolerance * scale * 1.4f)
                {
                    continue;
                }

                if (!isSlam && horiz > 0.5f && Vector3.Angle(transform.forward, flat) > CleaveHalfArcDeg + 20f)
                {
                    continue;
                }

                if (!HasClearStrikePath(rayOrigin, p, oldBird))
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
            TryGetEnemySurfacePoint(e, from, e == _enemyTarget ? _enemyTargetDetectors : null, out Vector3 p);
            Vector3 d = p - from;
            d.y = 0f;
            return d.magnitude;
        }
    }
}
