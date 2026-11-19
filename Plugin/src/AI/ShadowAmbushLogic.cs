using System;

namespace MahoragaLC.AI
{
    /// <summary>
    /// Pure (Unity-free) gating logic for Mahoraga's late-game Shadow Dive &amp; Ambush Re-Summon so all
    /// timing, cooldown, domain-priority, and state guards are directly unit-testable.
    /// </summary>
    public static class ShadowAmbushLogic
    {
        public static bool CanTriggerAmbush(
            bool enableShadowAmbush,
            float normalizedTimeOfDay,
            float minTimeOfDay,
            float timeSinceLostSight,
            float lostSightDelay,
            float cooldownRemaining,
            bool hasLivingOldBirdInDomain,
            bool isDead,
            bool isAttacking,
            bool isAdapting,
            bool isSummoning,
            bool isSubmerging,
            bool isEngagingEnemy = false,
            bool isStunned = false,
            bool isForcingDoor = false)
        {
            if (!enableShadowAmbush)
            {
                return false;
            }

            if (isDead || isAttacking || isAdapting || isSummoning || isSubmerging ||
                isEngagingEnemy || isStunned || isForcingDoor)
            {
                return false;
            }

            if (hasLivingOldBirdInDomain)
            {
                return false;
            }

            if (float.IsNaN(normalizedTimeOfDay) || float.IsInfinity(normalizedTimeOfDay) ||
                float.IsNaN(minTimeOfDay) || float.IsInfinity(minTimeOfDay) ||
                normalizedTimeOfDay < minTimeOfDay)
            {
                return false;
            }

            if (float.IsNaN(timeSinceLostSight) || float.IsInfinity(timeSinceLostSight) ||
                float.IsNaN(lostSightDelay) || float.IsInfinity(lostSightDelay) ||
                timeSinceLostSight < lostSightDelay)
            {
                return false;
            }

            if (float.IsNaN(cooldownRemaining) || float.IsInfinity(cooldownRemaining) ||
                cooldownRemaining > 0f)
            {
                return false;
            }

            return true;
        }
    }

    /// <summary>
    /// Pure (Unity-free) curve set for the downward shadow dive (<c>Summon_Submerge</c>, 78 frames @ 30 fps = 2.60 s).
    /// Inverse of <see cref="SummonTimeline"/>: shadow pool opens on the floor, Mahoraga coils downward and sinks
    /// from 0 m -&gt; -4.4 m while fading into shadow ink, then the pool closes shut right before warping to ambush.
    /// </summary>
    public readonly struct SubmergeTimeline
    {
        public const float ReferenceDuration = 2.6f;
        public const float MinDuration = 1.0f;
        public const float MaxDuration = 6.0f;

        public const float SubmergedOffset = SummonTimeline.SubmergedOffset;

        public const float PoolOpenEndRef = 0.55f;
        public const float SinkStartRef = 0.45f;
        public const float SinkEndRef = 2.15f;
        public const float DissolveStartRef = 2.10f;

        public readonly float Duration;
        public readonly float Scale;

        public SubmergeTimeline(float duration)
        {
            if (float.IsNaN(duration) || float.IsInfinity(duration))
            {
                duration = ReferenceDuration;
            }

            Duration = Math.Min(MaxDuration, Math.Max(MinDuration, duration));
            Scale = Duration / ReferenceDuration;
        }

        public float PoolOpenEnd => PoolOpenEndRef * Scale;
        public float SinkStart => SinkStartRef * Scale;
        public float SinkEnd => SinkEndRef * Scale;
        public float DissolveStart => DissolveStartRef * Scale;

        /// <summary>0..1 pool radius factor: bursts open, holds during the sink, then closes shut.</summary>
        public float PoolScale(float t)
        {
            if (t <= 0f)
            {
                return 0f;
            }

            if (t < PoolOpenEnd)
            {
                float u = t / PoolOpenEnd;
                return 1f - (float)Math.Pow(1f - u, 3.0);
            }

            if (t < DissolveStart)
            {
                return 1f;
            }

            if (t < Duration)
            {
                float u = (t - DissolveStart) / (Duration - DissolveStart);
                return 1f - u * u * (3f - 2f * u);
            }

            return 0f;
        }

        /// <summary>Vertical visual offset of SummonOffset: 0 m -&gt; -4.4 m as Mahoraga sinks into the pool.</summary>
        public float ModelOffset(float t)
        {
            if (t <= SinkStart)
            {
                return 0f;
            }

            if (t >= SinkEnd)
            {
                return SubmergedOffset;
            }

            float u = (t - SinkStart) / (SinkEnd - SinkStart);
            float eased = u * u * u * (u * (u * 6f - 15f) + 10f);
            return SubmergedOffset * eased;
        }

        /// <summary>True until Mahoraga is completely submerged below floor level.</summary>
        public bool IsModelVisible(float t) => t < SinkEnd;

        /// <summary>Base colour multiplier (1.0 normal skin -&gt; 0.06 shadow ink) as he sinks into the pool.</summary>
        public float ShadowCoat(float t)
        {
            float u = Clamp01((t - SinkStart) / (SinkEnd - SinkStart));
            float eased = u * u * (3f - 2f * u);
            return 1f - 0.94f * eased;
        }

        /// <summary>Droplet particles per second while diving into the shadow pool.</summary>
        public float DropletRate(float t)
        {
            if (t < PoolOpenEnd * 0.5f || t >= DissolveStart)
            {
                return 0f;
            }

            if (t < SinkEnd)
            {
                return 45f;
            }

            return 18f;
        }

        public bool IsFinished(float t) => t >= Duration;

        private static float Clamp01(float v) => v < 0f ? 0f : (v > 1f ? 1f : v);
    }
}
