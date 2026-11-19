using System;
using System.Collections.Generic;

namespace MahoragaLC.AI
{
    /// <summary>
    /// Decaying aggro ledger used for retaliation. Each hit adds weight; weights halve every
    /// <c>halfLifeSeconds</c>. A rival only steals the top spot when its weight beats the current target's by
    /// <see cref="SwitchMargin"/> (hysteresis), so Mahoraga never flip-flops between two attackers.
    /// Pure C# (no Unity types) so it is unit-tested directly. Ids are arbitrary ints chosen by the caller
    /// (player client id, enemy instance id, ...).
    /// </summary>
    public sealed class AggroTable
    {
        public const float MinRelevantWeight = 0.15f;
        public const float SwitchMargin = 1.35f;
        public const float SwitchAbsoluteBonus = 0.4f;

        private struct Entry
        {
            public float Weight;
            public float Time;
        }

        private readonly Dictionary<int, Entry> _entries = new Dictionary<int, Entry>();
        private readonly List<int> _scratch = new List<int>();
        private float _halfLife;

        public AggroTable(float halfLifeSeconds)
        {
            _halfLife = Math.Max(0.25f, halfLifeSeconds);
        }

        /// <summary>Half-life used from now on (config can change at runtime).</summary>
        public float HalfLifeSeconds
        {
            get => _halfLife;
            set => _halfLife = Math.Max(0.25f, value);
        }

        public int Count => _entries.Count;

        public void Add(int id, float amount, float now)
        {
            float current = Get(id, now);
            _entries[id] = new Entry { Weight = current + Math.Max(0f, amount), Time = now };
        }

        public float Get(int id, float now)
        {
            if (!_entries.TryGetValue(id, out Entry e))
            {
                return 0f;
            }

            float dt = Math.Max(0f, now - e.Time);
            return e.Weight * (float)Math.Pow(0.5, dt / _halfLife);
        }

        public bool Has(int id, float now) => Get(id, now) >= MinRelevantWeight;

        public void Remove(int id) => _entries.Remove(id);

        public void Clear() => _entries.Clear();

        /// <summary>Highest current weight above <see cref="MinRelevantWeight"/>; false when nobody is relevant.</summary>
        public bool TryGetTop(float now, out int topId, out float topWeight, Func<int, bool>? accept = null)
        {
            topId = -1;
            topWeight = 0f;
            _scratch.Clear();
            foreach (KeyValuePair<int, Entry> kv in _entries)
            {
                float w = Get(kv.Key, now);
                if (w < MinRelevantWeight)
                {
                    _scratch.Add(kv.Key);
                    continue;
                }

                if (accept != null && !accept(kv.Key))
                {
                    continue;
                }

                if (w > topWeight)
                {
                    topWeight = w;
                    topId = kv.Key;
                }
            }

            for (int i = 0; i < _scratch.Count; i++)
            {
                _entries.Remove(_scratch[i]);
            }

            return topId != -1;
        }

        /// <summary>
        /// True when <paramref name="rivalId"/> should take over from <paramref name="currentId"/>
        /// (currentId may be -1 = nothing). Applies the hysteresis margin.
        /// </summary>
        public bool ShouldSwitch(int currentId, int rivalId, float now)
        {
            if (rivalId == -1 || rivalId == currentId)
            {
                return false;
            }

            float rival = Get(rivalId, now);
            if (rival < MinRelevantWeight)
            {
                return false;
            }

            if (currentId == -1)
            {
                return true;
            }

            float current = Get(currentId, now);
            return rival > current * SwitchMargin + SwitchAbsoluteBonus;
        }
    }

    /// <summary>Timed "do not target" list (enemies we gave up on, immortal punching bags, ...).</summary>
    public sealed class CooldownSet
    {
        private readonly Dictionary<int, float> _until = new Dictionary<int, float>();
        private readonly List<int> _scratch = new List<int>();

        public void Block(int id, float until)
        {
            _until[id] = until;
        }

        public bool IsBlocked(int id, float now)
        {
            if (!_until.TryGetValue(id, out float until))
            {
                return false;
            }

            if (now >= until)
            {
                _until.Remove(id);
                return false;
            }

            return true;
        }

        public void Prune(float now)
        {
            _scratch.Clear();
            foreach (KeyValuePair<int, float> kv in _until)
            {
                if (now >= kv.Value)
                {
                    _scratch.Add(kv.Key);
                }
            }

            for (int i = 0; i < _scratch.Count; i++)
            {
                _until.Remove(_scratch[i]);
            }
        }

        public void Clear() => _until.Clear();
    }

    public enum EngageVerdict
    {
        Continue = 0,
        TargetGone = 1,
        TimedOut = 2,
        ImmortalTimedOut = 3,
        Unreachable = 4,
        TooFar = 5,
        PlayerTakesPriority = 6
    }

    /// <summary>
    /// Pure decision rules for "should Mahoraga keep fighting this enemy". Guarantees every engagement
    /// terminates (hard time cap, immortal cap, unreachable cap, distance cap).
    /// </summary>
    public static class EnemyEngagementPolicy
    {
        public static EngageVerdict Evaluate(
            bool targetValid,
            bool targetCanDie,
            float engagedSeconds,
            float maxEngageSeconds,
            float immortalEngageSeconds,
            float unreachableSeconds,
            float unreachableLimitSeconds,
            float distanceToTarget,
            float maxDistance,
            bool playerWantsAttention,
            bool enemyAttackedRecently,
            float minEngageSecondsBeforePlayerPreempt)
        {
            if (!targetValid)
            {
                return EngageVerdict.TargetGone;
            }

            if (!targetCanDie && engagedSeconds >= immortalEngageSeconds)
            {
                return EngageVerdict.ImmortalTimedOut;
            }

            if (engagedSeconds >= maxEngageSeconds)
            {
                return EngageVerdict.TimedOut;
            }

            if (unreachableSeconds >= unreachableLimitSeconds)
            {
                return EngageVerdict.Unreachable;
            }

            if (distanceToTarget > maxDistance)
            {
                return EngageVerdict.TooFar;
            }

            // Players come first, unless the enemy is actively hurting Mahoraga right now.
            if (playerWantsAttention && !enemyAttackedRecently && engagedSeconds >= minEngageSecondsBeforePlayerPreempt)
            {
                return EngageVerdict.PlayerTakesPriority;
            }

            return EngageVerdict.Continue;
        }

        /// <summary>
        /// Old Bird engagement evaluation: Old Birds are #1 priority (never preempted by players and always destroyable),
        /// terminating only on target disappearance, hard timeout, unreachable pathing, or extreme range.
        /// </summary>
        public static EngageVerdict EvaluateOldBird(
            bool targetValid,
            float engagedSeconds,
            float maxEngageSeconds,
            float unreachableSeconds,
            float unreachableLimitSeconds,
            float distanceToTarget,
            float maxDistance)
        {
            if (!targetValid)
            {
                return EngageVerdict.TargetGone;
            }

            if (engagedSeconds >= maxEngageSeconds)
            {
                return EngageVerdict.TimedOut;
            }

            if (unreachableSeconds >= unreachableLimitSeconds)
            {
                return EngageVerdict.Unreachable;
            }

            if (distanceToTarget > maxDistance)
            {
                return EngageVerdict.TooFar;
            }

            return EngageVerdict.Continue;
        }
    }

    /// <summary>Knockback sizing helpers (pure so scaling is testable).</summary>
    public static class KnockbackMath
    {
        /// <summary>Impulse multiplier by enemy size, using the game's EnemySize enum order (0 = Tiny, 1 = Giant, 2 = Medium).</summary>
        public static float EnemySizeFactor(int enemySizeIndex)
        {
            switch (enemySizeIndex)
            {
                case 0: return 1.25f;
                case 1: return 0.35f;
                default: return 0.85f;
            }
        }

        /// <summary>Per-frame displacement of a decaying push: speed * exp(-decay * t) integrated by the caller.</summary>
        public static float DecayingSpeed(float initialSpeed, float elapsed, float decayPerSecond)
        {
            return initialSpeed * (float)Math.Exp(-Math.Max(0f, decayPerSecond) * Math.Max(0f, elapsed));
        }

        /// <summary>Total distance travelled by a push of <paramref name="initialSpeed"/> decaying at <paramref name="decayPerSecond"/>.</summary>
        public static float TotalDistance(float initialSpeed, float decayPerSecond)
        {
            return decayPerSecond <= 0f ? 0f : initialSpeed / decayPerSecond;
        }
    }

    /// <summary>Counts distinct attacker ids seen inside a sliding time window (how many Old Birds are shooting right now).</summary>
    public sealed class DistinctAttackerWindow
    {
        private readonly Dictionary<int, float> _lastSeen = new Dictionary<int, float>();
        private readonly List<int> _scratch = new List<int>();

        public void Register(int id, float now)
        {
            _lastSeen[id] = now;
        }

        public int Count(float now, float windowSeconds)
        {
            _scratch.Clear();
            int count = 0;
            foreach (KeyValuePair<int, float> kv in _lastSeen)
            {
                if (now - kv.Value <= windowSeconds)
                {
                    count++;
                }
                else
                {
                    _scratch.Add(kv.Key);
                }
            }

            for (int i = 0; i < _scratch.Count; i++)
            {
                _lastSeen.Remove(_scratch[i]);
            }

            return count;
        }

        public void Clear() => _lastSeen.Clear();
    }

    /// <summary>Slow out-of-combat regeneration: starts after a quiet period, accumulates fractions so tiny rates still work.</summary>
    public sealed class RegenerationTracker
    {
        private float _fraction;

        /// <summary>Returns whole HP to add this tick (0 while recently damaged or already full).</summary>
        public int Tick(float dt, float now, float lastDamageTime, int currentHP, int maxHP, float quietDelaySeconds, float percentOfMaxPerSecond)
        {
            if (currentHP >= maxHP || currentHP <= 0 || percentOfMaxPerSecond <= 0f || now - lastDamageTime < quietDelaySeconds)
            {
                _fraction = 0f;
                return 0;
            }

            _fraction += maxHP * percentOfMaxPerSecond * Math.Max(0f, dt);
            int whole = (int)Math.Floor(_fraction);
            if (whole <= 0)
            {
                return 0;
            }

            _fraction -= whole;
            return Math.Min(whole, maxHP - currentHP);
        }

        public void Reset() => _fraction = 0f;
    }

    /// <summary>Old Bird growth maths: Mahoraga grows in equal steps toward the Old Bird's height, capped.</summary>
    public static class GrowthMath
    {
        /// <summary>Scale needed to match <paramref name="targetHeight"/> given Mahoraga's unscaled height (never below 1, never above cap).</summary>
        public static float ScaleToMatch(float targetHeight, float baseHeight, float maxScale)
        {
            if (baseHeight <= 0.01f)
            {
                return 1f;
            }

            return Math.Clamp(targetHeight / baseHeight, 1f, Math.Max(1f, maxScale));
        }

        /// <summary>One growth step: advances <c>1 / stepsToMatch</c> of the way from 1 to <paramref name="matchScale"/>, never past it.</summary>
        public static float NextEarnedScale(float currentEarned, float matchScale, int stepsToMatch)
        {
            float cur = Math.Max(1f, currentEarned);
            if (matchScale <= cur)
            {
                return cur;
            }

            float step = (matchScale - 1f) / Math.Max(1, stepsToMatch);
            return Math.Min(matchScale, cur + step);
        }

        /// <summary>Scale actually applied: earned growth outdoors, base size inside the facility.</summary>
        public static float EffectiveScale(float earned, bool outside, float maxScale)
        {
            return outside ? Math.Clamp(earned, 1f, Math.Max(1f, maxScale)) : 1f;
        }

        /// <summary>Exponential approach used identically on every machine (frame-rate independent).</summary>
        public static float Approach(float current, float target, float dt, float transitionSeconds)
        {
            float k = 1f - (float)Math.Exp(-4.0 * Math.Max(0f, dt) / Math.Max(0.1f, transitionSeconds));
            float next = current + (target - current) * k;
            return Math.Abs(target - next) < 0.002f ? target : next;
        }
    }

    /// <summary>Mahoraga-side durability of an Old Bird (the vanilla one ignores HitEnemy).</summary>
    public static class OldBirdDurability
    {
        /// <summary>Damage dealt to the Old Bird's counter by one strike; a bigger Mahoraga hits proportionally harder.</summary>
        public static float StrikeDamage(bool isSlam, float mahoragaScale)
        {
            return (isSlam ? 2f : 1f) * Math.Max(1f, mahoragaScale);
        }
    }
}
