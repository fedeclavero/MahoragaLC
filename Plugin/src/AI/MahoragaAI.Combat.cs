using System;
using System.Collections.Generic;
using GameNetcodeStuff;
using MahoragaLC.Config;
using MahoragaLC.Patches;
using Unity.Netcode;
using UnityEngine;

namespace MahoragaLC.AI
{
    /// <summary>
    /// Retaliation (players and enemies), the EngagingEnemy state, attack resolution against enemies,
    /// knockback and the damage/adaptation pipeline. Everything here is server-authoritative; clients only
    /// receive effects through the RPCs declared in MahoragaAI.cs / MahoragaAI.OldBird.cs.
    ///
    /// Network paths used (verified in Assembly-CSharp):
    ///  - Damage to another enemy: EnemyAI.HitEnemyOnLocalClient(force, dir, null, sfx) -> HitEnemyServerRpc ->
    ///    HitEnemyClientRpc -> EnemyAI.HitEnemy on every machine. It is exactly what Landmine.SpawnExplosion uses.
    ///    Enemies with enemyType.canDie == false are never damaged (only shoved).
    ///  - Damage to a player: ApplyPlayerDamageClientRpc -> PlayerControllerB.DamagePlayer on the owning client
    ///    (DamagePlayer has no knockback for living players, so the shove goes through externalForceAutoFade like Landmine does).
    /// </summary>
    public partial class MahoragaAI
    {
        private const float EngageEntryRangeBase = 2.6f;
        private const float EnemyAttackedRecentlySeconds = 2.5f;
        private const float EnemyAggressorMaxDistance = 40f;
        private const float EnemyMinEngageBeforePlayerPreempt = 2.0f;
        private const float EnemyUnreachableLimitSeconds = 8.0f;
        private const float EnemyMaxChaseDistance = 120f;
        private const float EnemyPushDuration = 0.6f;
        private const float EnemyPushDecay = 4.0f;

        private struct PendingPush
        {
            public EnemyAI Enemy;
            public Vector3 Direction;
            public float Speed;
            public float Elapsed;
        }

        private static AdaptationSettings _fallbackAdaptation = AdaptationSettings.CreateProgressive();

        private readonly AggroTable _playerAggro = new AggroTable(6f);
        private readonly AggroTable _enemyAggro = new AggroTable(5f);
        private readonly CooldownSet _ignoredEnemies = new CooldownSet();
        private readonly DistinctAttackerWindow _oldBirdAttackers = new DistinctAttackerWindow();
        private readonly RegenerationTracker _regen = new RegenerationTracker();
        private readonly Dictionary<int, EnemyAI> _aggressorLookup = new Dictionary<int, EnemyAI>();
        private readonly Dictionary<int, float> _enemyLastHitTime = new Dictionary<int, float>();
        private readonly List<PendingPush> _pushes = new List<PendingPush>();
        private Func<int, bool>? _acceptEnemyAggressorCache;
        private Func<int, bool>? _acceptPlayerAggressorCache;

        private EnemyAI? _enemyTarget;
        private EnemyAICollisionDetect[]? _enemyTargetDetectors;
        private bool _engageTargetIsOldBird;
        private float _engageStartTime;
        private float _engageUnreachableTimer;
        private int _engageRepathCounter;
        private float _engageLockoutUntil;
        private PlayerControllerB? _resumePlayerAfterEngage;
        private EnemyAI? _attackEnemyTarget;

        private float _lastRetargetTime = -999f;
        private float _blockedTimer;
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

        // =====================================================================
        // Per-tick server upkeep
        // =====================================================================

        private void TickCombatServer(float dt)
        {
            float now = Time.time;

            MahoragaConfig? cfg = MahoragaConfig.Current;
            _playerAggro.HalfLifeSeconds = cfg != null ? cfg.RetaliationHalfLifeSeconds.Value : 6f;
            _enemyAggro.HalfLifeSeconds = cfg != null ? cfg.EnemyGrudgeHalfLifeSeconds.Value : 5f;

            TickEnemyPushes(dt);
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
                || ExplosionTrackingPatch.HasRecentExplosionNear(transform.position, now);
            bool recentMissile = ExplosionTrackingPatch.HasRecentMissileNear(transform.position, now, out EnemyAI? missileSource);

            // No attacker + not an explosion: probably a creature touching/biting/stomping us (Baboon, Kiwi, dog, ...).
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

            // ---- Retaliation bookkeeping -----------------------------------------------------------
            if (playerWhoHit != null && PlayerIsTargetable(playerWhoHit))
            {
                RegisterPlayerAggression(playerWhoHit, force, now);
                if (currentBehaviourStateIndex == (int)MahoragaState.Patrolling ||
                    currentBehaviourStateIndex == (int)MahoragaState.Investigating)
                {
                    EnterChaseStateServer(playerWhoHit);
                }
                else if (currentBehaviourStateIndex == (int)MahoragaState.EngagingEnemy && targetPlayer == null)
                {
                    _resumePlayerAfterEngage = playerWhoHit;
                }
                else if (targetPlayer == null)
                {
                    targetPlayer = playerWhoHit;
                }
            }

            EnemyAI? enemyAttacker = missileSource != null && recentMissile ? missileSource : suspectedEnemy;
            if (enemyAttacker != null && (cfg == null || cfg.FightEnemies.Value))
            {
                RegisterEnemyAggression(enemyAttacker, recentMissile ? 3f : 1f, now);
            }

            if (category == AdaptationCategory.OldBirdMissile && missileSource is RadMechAI oldBird)
            {
                OnOldBirdDamagedMe(oldBird, now);
            }

            // ---- Wheel ------------------------------------------------------------------------------
            if (eval.TriggeredAdaptation)
            {
                EnterAdaptationStateServer(category, eval.FinalHP);
                return;
            }

            if (eval.TriggeredWheelClick)
            {
                TriggerWheelClickClientRpc((byte)category, (byte)eval.StageAfter, _adaptation.AdaptedMask, _adaptation.WheelStepCount, enemyHP);
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
            int id = enemy.GetInstanceID();
            _enemyAggro.Add(id, weight, now);
            _aggressorLookup[id] = enemy;
            _enemyLastHitTime[id] = now;
        }

        /// <summary>Nearest living, non-Mahoraga enemy whose body is within a bite's reach of us.</summary>
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
        // Priorities: who do we fight right now?
        // =====================================================================

        private bool IsEnemyEngageable(EnemyAI? e)
        {
            return e != null && e != this && !(e is MahoragaAI) && !e.isEnemyDead
                && e.isOutside == isOutside && !_ignoredEnemies.IsBlocked(e.GetInstanceID(), Time.time);
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

        private bool OldBirdPriorityAllows(MahoragaConfig? cfg, bool playersPresent, float distToOldBird, bool oldBirdAttackingUs, bool playerRetaliating, bool playerVeryClose)
        {
            string mode = cfg != null ? cfg.OldBirdPriorityMode.Value : "WhenClose";
            float priorityDist = cfg != null ? cfg.OldBirdPriorityDistance.Value : 20f;
            if (oldBirdAttackingUs && !playerRetaliating)
            {
                return true;
            }

            switch (mode)
            {
                case "Always":
                    return !playerRetaliating && !playerVeryClose;
                case "PlayersFirst":
                    return !playersPresent;
                default: // WhenClose
                    return !playersPresent || (distToOldBird <= priorityDist && !playerRetaliating && !playerVeryClose);
            }
        }

        /// <summary>
        /// Called every AI interval while Patrolling / Investigating / Chasing. Decides whether to start fighting an enemy.
        /// Returns true if the state changed (caller must stop its own interval logic).
        /// </summary>
        private bool EvaluateEnemyEngagementServer(MahoragaConfig? cfg)
        {
            if (cfg != null && !cfg.FightEnemies.Value && !cfg.FightOldBirds.Value)
            {
                return false;
            }

            float now = Time.time;
            if (now < _engageLockoutUntil)
            {
                return false;
            }

            bool fightEnemies = cfg == null || cfg.FightEnemies.Value;
            bool fightOldBirds = cfg == null || cfg.FightOldBirds.Value;
            bool chasing = currentBehaviourStateIndex == (int)MahoragaState.Chasing;
            Vector3 chest = transform.position + Vector3.up * 1.4f * ScaleFactor;

            // --- Who is a player worth caring about right now? ---------------------------------------
            PlayerControllerB? playerInPerception = null;
            bool playerRetaliating = GetRetaliationPlayer(0.5f) != null;
            bool playerVeryClose = CountTargetablePlayersWithin(4f) > 0;
            if (chasing && targetPlayer != null && PlayerIsTargetable(targetPlayer))
            {
                playerInPerception = targetPlayer;
            }
            else
            {
                playerInPerception = FindBestVisibleOrNearbyPlayer(viewHalfAngle: 90f, sightRange: 28f, proximityRadius: 5f);
            }
            bool playersPresent = playerInPerception != null || playerRetaliating;

            // --- 1. Active aggressors (anything that hurt us recently) -------------------------------------
            if (fightEnemies || fightOldBirds)
            {
                if (_enemyAggro.TryGetTop(now, out int aggId, out _, _acceptEnemyAggressor) &&
                    _aggressorLookup.TryGetValue(aggId, out EnemyAI? aggressor) && aggressor != null)
                {
                    bool isOldBird = aggressor is RadMechAI;
                    if ((isOldBird && fightOldBirds) || (!isOldBird && fightEnemies))
                    {
                        float lastHit = _enemyLastHitTime.TryGetValue(aggId, out float t) ? t : -999f;
                        bool activeNow = now - lastHit <= EnemyAttackedRecentlySeconds;
                        float dist = DistanceToEnemySurface(aggressor, chest);
                        if (dist <= EnemyAggressorMaxDistance)
                        {
                            bool engage;
                            if (isOldBird)
                            {
                                engage = OldBirdPriorityAllows(cfg, playersPresent, dist, activeNow, playerRetaliating, playerVeryClose);
                            }
                            else
                            {
                                // Players first, unless this creature is hurting us right now at close quarters.
                                engage = !playersPresent || (activeNow && dist <= 8f && !playerRetaliating);
                            }

                            if (engage)
                            {
                                EnterEngageEnemyStateServer(aggressor, playerInPerception);
                                return true;
                            }
                        }
                    }
                }
            }

            // --- 2. Hunt Old Birds even if nobody attacked first ---------------------------------------------
            if (fightOldBirds)
            {
                RadMechAI? hunted = FindNearestOldBirdServer(cfg, chest, out float oldBirdDist);
                if (hunted != null &&
                    OldBirdPriorityAllows(cfg, playersPresent, oldBirdDist, oldBirdAttackingUs: false, playerRetaliating, playerVeryClose))
                {
                    EnterEngageEnemyStateServer(hunted, playerInPerception);
                    return true;
                }
            }

            if (!fightEnemies)
            {
                return false;
            }

            // --- 3. Something physically blocks the way while chasing a player ------------------------------
            if (chasing)
            {
                bool slowed = agent == null || !agent.enabled || agent.velocity.sqrMagnitude < 0.36f;
                EnemyAI? blocker = slowed ? FindBlockingEnemyServer(chest) : null;
                if (blocker != null)
                {
                    _blockedTimer += AIIntervalTime;
                    if (_blockedTimer >= 1.0f)
                    {
                        _blockedTimer = 0f;
                        EnterEngageEnemyStateServer(blocker, targetPlayer);
                        return true;
                    }
                }
                else
                {
                    _blockedTimer = 0f;
                }
            }

            // --- 4. Optional: pick fights while idle -----------------------------------------------------------
            if (cfg != null && cfg.EngageEnemiesWhenIdle.Value && !playersPresent &&
                currentBehaviourStateIndex == (int)MahoragaState.Patrolling)
            {
                EnemyAI? idleTarget = FindNearestKillableEnemyInSightServer(chest, 16f);
                if (idleTarget != null)
                {
                    EnterEngageEnemyStateServer(idleTarget, null);
                    return true;
                }
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

            float range = cfg != null ? cfg.OldBirdSeekRange.Value : 70f;
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

        private EnemyAI? FindBlockingEnemyServer(Vector3 chest)
        {
            if (RoundManager.Instance == null)
            {
                return null;
            }

            List<EnemyAI> all = RoundManager.Instance.SpawnedEnemies;
            EnemyAI? best = null;
            float bestDist = 2.4f * ScaleFactor;
            for (int i = 0; i < all.Count; i++)
            {
                EnemyAI e = all[i];
                if (!IsEnemyEngageable(e) || !e.enemyType.canDie)
                {
                    continue;
                }

                if (!TryGetEnemySurfacePoint(e, chest, null, out Vector3 p))
                {
                    continue;
                }

                Vector3 flat = p - transform.position;
                flat.y = 0f;
                float d = flat.magnitude;
                if (d < bestDist && (d < 0.4f || Vector3.Dot(transform.forward, flat.normalized) > 0.35f))
                {
                    bestDist = d;
                    best = e;
                }
            }

            return best;
        }

        private EnemyAI? FindNearestKillableEnemyInSightServer(Vector3 chest, float range)
        {
            if (RoundManager.Instance == null)
            {
                return null;
            }

            List<EnemyAI> all = RoundManager.Instance.SpawnedEnemies;
            EnemyAI? best = null;
            float bestDist = range;
            for (int i = 0; i < all.Count; i++)
            {
                EnemyAI e = all[i];
                if (!IsEnemyEngageable(e) || !e.enemyType.canDie)
                {
                    continue;
                }

                if (!TryGetEnemySurfacePoint(e, chest, null, out Vector3 p))
                {
                    continue;
                }

                float d = Vector3.Distance(chest, p);
                if (d < bestDist && HasClearStrikePath(chest, p, e))
                {
                    bestDist = d;
                    best = e;
                }
            }

            return best;
        }

        // =====================================================================
        // EngagingEnemy state (6)
        // =====================================================================

        private void EnterEngageEnemyStateServer(EnemyAI enemy, PlayerControllerB? resumePlayer)
        {
            _forcingDoor = null;
            StopSearch(currentSearch);
            _enemyTarget = enemy;
            _enemyTargetDetectors = enemy.GetComponentsInChildren<EnemyAICollisionDetect>(includeInactive: false);
            _engageTargetIsOldBird = enemy is RadMechAI;
            _engageStartTime = Time.time;
            _engageUnreachableTimer = 0f;
            _engageRepathCounter = 0;
            _resumePlayerAfterEngage = resumePlayer;
            targetPlayer = null;
            movingTowardsTargetPlayer = false;

            SwitchBehaviourAndSyncServer(MahoragaState.EngagingEnemy, -1);
            SetDestinationToPosition(enemy.transform.position, checkForPath: false);
        }

        private void RunEngageEnemyInterval(MahoragaConfig? cfg)
        {
            float now = Time.time;
            EnemyAI? target = _enemyTarget;
            float baseChase = cfg != null ? cfg.ChaseSpeed.Value : 6.2f;
            float bonusPerAdapt = cfg != null ? cfg.ChaseSpeedPerAdaptation.Value : 0.35f;
            if (agent != null && agent.enabled)
            {
                agent.speed = baseChase + Mathf.Min(4, _adaptation.AdaptedCategoryCount) * bonusPerAdapt;
            }
            openDoorSpeedMultiplier = 1.65f;

            bool valid = target != null && !target.isEnemyDead && target.isOutside == isOutside;
            Vector3 chest = transform.position + Vector3.up * 1.4f * ScaleFactor;
            float dist = valid ? DistanceToEnemySurface(target!, chest) : 0f;

            bool targetCanDie = valid && (_engageTargetIsOldBird || (target!.enemyType != null && target.enemyType.canDie));
            bool playerWants = valid && PlayerWantsAttentionServer(cfg, dist, chest);
            bool attackedRecently = valid && ((_enemyLastHitTime.TryGetValue(target!.GetInstanceID(), out float lastHit) && now - lastHit <= EnemyAttackedRecentlySeconds) || dist <= 3.0f * ScaleFactor);
            float maxSeconds = _engageTargetIsOldBird
                ? (cfg != null ? cfg.OldBirdEngageMaxSeconds.Value : 45f)
                : (cfg != null ? cfg.EnemyEngageMaxSeconds.Value : 20f);
            float immortalSeconds = cfg != null ? cfg.ImmortalEnemyEngageSeconds.Value : 6f;

            EngageVerdict verdict = EnemyEngagementPolicy.Evaluate(
                valid,
                targetCanDie,
                now - _engageStartTime,
                maxSeconds,
                immortalSeconds,
                _engageUnreachableTimer,
                EnemyUnreachableLimitSeconds,
                dist,
                EnemyMaxChaseDistance,
                playerWants,
                attackedRecently,
                EnemyMinEngageBeforePlayerPreempt);

            if (verdict != EngageVerdict.Continue)
            {
                ExitEngageEnemyStateServer(verdict, cfg);
                return;
            }

            if (TryScanForLockedDoorServer(cfg))
            {
                return;
            }

            // Walk to it. The (costly) path validation only runs every 3rd interval, so the unreachable timer only moves then.
            bool validatePath = (_engageRepathCounter++ % 3) == 0;
            bool pathOk = SetDestinationToPosition(target!.transform.position, checkForPath: validatePath);
            if (validatePath)
            {
                _engageUnreachableTimer = pathOk || dist <= EngageEntryRangeBase * ScaleFactor * 1.6f
                    ? 0f
                    : _engageUnreachableTimer + AIIntervalTime * 3f;
            }

            // Swing when in reach.
            if (_attackCooldownTimer <= 0f && dist <= EngageEntryRangeBase * ScaleFactor)
            {
                TryGetEnemySurfacePoint(target, chest, _enemyTargetDetectors, out Vector3 surface);
                if (HasClearStrikePath(transform.position + Vector3.up * 1.8f * ScaleFactor, surface, target))
                {
                    EnterAttackStateServer(ShouldUseSlamAttack(forceSlam: false), target);
                }
            }
        }

        private bool PlayerWantsAttentionServer(MahoragaConfig? cfg, float distToEnemy, Vector3 chest)
        {
            bool retaliating = GetRetaliationPlayer(0.5f) != null;
            bool veryClose = CountTargetablePlayersWithin(4f) > 0;
            bool visible = FindBestVisibleOrNearbyPlayer(viewHalfAngle: 90f, sightRange: 28f, proximityRadius: 5f) != null;
            bool playersPresent = visible || retaliating;

            if (_engageTargetIsOldBird)
            {
                // Same rule that started the hunt, inverted (never flips the moment it starts).
                // distance*0.7 = built-in hysteresis: it must be clearly out of the priority radius before players take over.
                return playersPresent && !OldBirdPriorityAllows(cfg, playersPresent, distToEnemy * 0.7f, oldBirdAttackingUs: false, retaliating, veryClose);
            }

            return playersPresent;
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
                    case EngageVerdict.ImmortalTimedOut:
                    case EngageVerdict.Unreachable:
                    case EngageVerdict.TooFar:
                        _ignoredEnemies.Block(id, now + (cfg != null ? cfg.DisengageCooldownSeconds.Value : 30f));
                        _enemyAggro.Remove(id);
                        break;
                    case EngageVerdict.TargetGone:
                        _enemyAggro.Remove(id);
                        break;
                    case EngageVerdict.PlayerTakesPriority:
                        _engageLockoutUntil = now + 3f;
                        break;
                }
            }

            PlayerControllerB? resume = _resumePlayerAfterEngage;
            _enemyTarget = null;
            _enemyTargetDetectors = null;
            _attackEnemyTarget = null;
            _resumePlayerAfterEngage = null;

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

        /// <summary>After an attack that targeted an enemy: go back to fighting it if it is still worth it.</summary>
        private bool TryReturnToEnemyEngagementAfterAttack()
        {
            EnemyAI? e = _attackEnemyTarget;
            _attackEnemyTarget = null;
            if (e == null || e.isEnemyDead || _enemyTarget != e || e.isOutside != isOutside)
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
        // Hit resolution against enemies (called from ResolveAttackHitServer)
        // =====================================================================

        private bool IsHostileForStrike(EnemyAI e)
        {
            if (e == _attackEnemyTarget)
            {
                return true;
            }
            return _enemyAggro.Has(e.GetInstanceID(), Time.time);
        }

        /// <summary>Returns true if at least one enemy was hit. Wall line-of-sight is checked per enemy.</summary>
        private bool ResolveEnemyHitsServer(bool isSlam, float reach, Vector3 rayOrigin, MahoragaConfig? cfg)
        {
            if (RoundManager.Instance == null || (cfg != null && !cfg.FightEnemies.Value && !cfg.FightOldBirds.Value))
            {
                return false;
            }

            int force = isSlam
                ? (cfg != null ? cfg.EnemySlamDamage.Value : 10)
                : (cfg != null ? cfg.EnemyCleaveDamage.Value : 6);
            float knockMult = cfg != null ? cfg.EnemyKnockbackMultiplier.Value : 1f;
            float scale = ScaleFactor;
            Vector3 chest = transform.position + Vector3.up * 1.4f * scale;

            bool anyHit = false;
            List<EnemyAI> all = RoundManager.Instance.SpawnedEnemies;
            for (int i = 0; i < all.Count; i++)
            {
                EnemyAI e = all[i];
                if (e == null || e == this || e is MahoragaAI || e.isEnemyDead || e.isOutside != isOutside || !IsHostileForStrike(e))
                {
                    continue;
                }

                EnemyAICollisionDetect[]? dets = e == _attackEnemyTarget ? _enemyTargetDetectors : null;
                if (!TryGetEnemySurfacePoint(e, chest, dets, out Vector3 p))
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

                if (!HasClearStrikePath(rayOrigin, p, e))
                {
                    continue;
                }

                Vector3 dir = horiz > 0.05f ? flat.normalized : transform.forward;
                if (e is RadMechAI oldBird)
                {
                    StrikeOldBirdServer(oldBird, isSlam, cfg);
                }
                else
                {
                    DamageEnemyServer(e, force, dir, knockMult, isSlam);
                }

                anyHit = true;
            }

            return anyHit;
        }

        private void DamageEnemyServer(EnemyAI e, int force, Vector3 dir, float knockMult, bool isSlam)
        {
            if (e.enemyType != null && e.enemyType.canDie)
            {
                try
                {
                    // Same call Landmine.SpawnExplosion uses: server RPC -> client RPC -> HitEnemy on every machine.
                    e.HitEnemyOnLocalClient(force, dir, null, playHitSFX: true, -1);
                }
                catch (Exception ex)
                {
                    Plugin.Logger.LogWarning($"[Combat] HitEnemyOnLocalClient failed on '{e.name}': {ex.Message}");
                }
            }

            if (knockMult > 0f)
            {
                float sizeFactor = KnockbackMath.EnemySizeFactor(e.enemyType != null ? (int)e.enemyType.EnemySize : 2);
                float speed = (isSlam ? 14f : 9f) * sizeFactor * knockMult * ScaleFactor;
                QueuePush(e, dir, speed);
            }
        }

        private void QueuePush(EnemyAI e, Vector3 dir, float speed)
        {
            if (speed < 0.3f)
            {
                return;
            }

            _pushes.Add(new PendingPush { Enemy = e, Direction = new Vector3(dir.x, 0f, dir.z).normalized, Speed = speed, Elapsed = 0f });
        }

        private void TickEnemyPushes(float dt)
        {
            for (int i = _pushes.Count - 1; i >= 0; i--)
            {
                PendingPush p = _pushes[i];
                EnemyAI e = p.Enemy;
                p.Elapsed += dt;
                if (e == null || e.isEnemyDead || p.Elapsed > EnemyPushDuration)
                {
                    _pushes.RemoveAt(i);
                    continue;
                }

                // Only the owner drives an enemy's NavMeshAgent (positions are then synced by the enemy itself).
                if (e.IsOwner && !e.inSpecialAnimation && e.agent != null && e.agent.enabled && e.agent.isOnNavMesh)
                {
                    float v = KnockbackMath.DecayingSpeed(p.Speed, p.Elapsed, EnemyPushDecay);
                    e.agent.Move(p.Direction * v * dt);
                }

                _pushes[i] = p;
            }
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
