using System;

namespace MahoragaLC.AI
{
    /// <summary>
    /// Pure (Unity-free) description of the Ten-Shadows style summoning sequence.
    /// Every client evaluates this same curve set from its own local elapsed time, so the
    /// sequence only needs one "start" message (plus the elapsed time for late joiners).
    ///
    /// Reference timeline (4.8 s, scaled linearly by duration / 4.8):
    ///   0.00 - 0.90  shadow pool spreads on the floor, rumble starts, model fully submerged
    ///   0.80 - 2.60  Mahoraga climbs out of the shadow (model visual offset -4.4 m -> 0 m)
    ///   2.60         emergence boom (camera shake)
    ///   2.65 - 3.75  Dharma wheel turns one spoke (+45 deg, 1 ball -> next ball) + gong + golden flash
    ///   3.20         roar
    ///   3.70 - 4.80  shadow dissolves (pool shrinks), droplets stop
    ///   4.80         sequence ends, +45 deg spoke advance persists seamlessly, AI takes over
    /// </summary>
    public readonly struct SummonTimeline
    {
        public const float ReferenceDuration = 4.8f;
        public const float MinDuration = 3.0f;
        public const float MaxDuration = 12.0f;

        /// <summary>Exact angular advance per Dharma Wheel click/summon turn (1 spoke = 360 / 8 = +45 deg).</summary>
        public const float WheelTurnDegrees = 45f;

        /// <summary>Visual offset (metres) that hides the 3.9 m tall model completely below the floor.</summary>
        public const float SubmergedOffset = -4.4f;

        /// <summary>Radius (metres) of the fully open shadow pool.</summary>
        public const float PoolRadius = 2.7f;

        public const float PoolOpenEndRef = 0.90f;
        public const float EmergeStartRef = 0.80f;
        public const float EmergeEndRef = 2.60f;
        public const float WheelStartRef = 2.65f;
        public const float WheelEndRef = 3.75f;
        public const float RoarRef = 3.20f;
        public const float DissolveStartRef = 3.70f;

        public readonly float Duration;
        public readonly float Scale;

        public SummonTimeline(float duration)
        {
            if (float.IsNaN(duration) || float.IsInfinity(duration))
            {
                duration = ReferenceDuration;
            }

            Duration = Math.Min(MaxDuration, Math.Max(MinDuration, duration));
            Scale = Duration / ReferenceDuration;
        }

        public float PoolOpenEnd => PoolOpenEndRef * Scale;
        public float EmergeStart => EmergeStartRef * Scale;
        public float EmergeEnd => EmergeEndRef * Scale;
        public float WheelStart => WheelStartRef * Scale;
        public float WheelEnd => WheelEndRef * Scale;
        public float RoarTime => RoarRef * Scale;
        public float DissolveStart => DissolveStartRef * Scale;

        /// <summary>0..1 pool radius factor: spreads fast (ease-out), holds, then shrinks (ease-in).</summary>
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

        /// <summary>Vertical visual offset of the model container: SubmergedOffset -> 0 while climbing out.</summary>
        public float ModelOffset(float t)
        {
            if (t <= EmergeStart)
            {
                return SubmergedOffset;
            }

            if (t >= EmergeEnd)
            {
                return 0f;
            }

            float u = (t - EmergeStart) / (EmergeEnd - EmergeStart);
            // Smootherstep: slow heave out of the liquid, steady climb, soft settle.
            float eased = u * u * u * (u * (u * 6f - 15f) + 10f);
            return SubmergedOffset * (1f - eased);
        }

        /// <summary>True while any part of the model could be above the floor (renderers on).</summary>
        public bool IsModelVisible(float t) => t >= EmergeStart;

        /// <summary>
        /// Extra degrees added on top of the current wheel yaw during summon: 0 -> +45 deg (one spoke).
        /// Holds at +45 deg after WheelEnd so EndSummonPresentation folds it into the persistent spoke offset
        /// with zero snap-back.
        /// </summary>
        public float WheelExtraAngle(float t)
        {
            if (t <= WheelStart)
            {
                return 0f;
            }

            if (t >= WheelEnd)
            {
                return WheelTurnDegrees;
            }

            float u = (t - WheelStart) / (WheelEnd - WheelStart);
            float eased = 1f - (float)Math.Pow(1f - u, 3.0);
            return WheelTurnDegrees * eased;
        }

        /// <summary>Base colour multiplier (0.06 black shadow coat -> 1 normal skin) as he climbs out.</summary>
        public float ShadowCoat(float t)
        {
            float u = Clamp01((t - EmergeStart) / (WheelEnd - EmergeStart));
            float eased = u * u * (3f - 2f * u);
            return 0.06f + 0.94f * eased;
        }

        /// <summary>Droplet particles per second.</summary>
        public float DropletRate(float t)
        {
            if (t < PoolOpenEnd * 0.6f || t >= DissolveStart)
            {
                return 0f;
            }

            if (t < EmergeEnd)
            {
                return 60f;
            }

            return 22f;
        }

        public bool IsFinished(float t) => t >= Duration;

        private static float Clamp01(float v) => v < 0f ? 0f : (v > 1f ? 1f : v);
    }
}
