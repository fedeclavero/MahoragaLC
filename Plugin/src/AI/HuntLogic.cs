using System;
using System.Collections.Generic;

namespace MahoragaLC.AI
{
    /// <summary>Pure (Unity-free) helpers for the hunt behaviour so they can be unit tested.</summary>
    public static class HuntLogic
    {
        /// <summary>
        /// Lethal Company's own inside/outside split: the interior lives around y = -200 and the game treats
        /// y &gt; -80 as "not inside" for inside enemies and y &lt; -100 as "not outside" for outside enemies
        /// (EnemyAI.CheckLineOfSight*). The 20 m dead band gives hysteresis so a position near the border
        /// never flips the domain back and forth. The old fixed "y &gt; -40" threshold was wrong for moons whose
        /// terrain sits below -40 and misclassified anything in the -80..-40 range.
        /// </summary>
        public static bool IsOutsideAt(float y, bool currentlyOutside)
        {
            return currentlyOutside ? y >= -100f : y > -80f;
        }

        public readonly struct Candidate
        {
            public readonly int Id;
            public readonly float Distance;

            public Candidate(int id, float distance)
            {
                Id = id;
                Distance = distance;
            }
        }

        /// <summary>
        /// Orders candidates nearest-first and drops the ones currently blocked (unreachable / stuck).
        /// Returns at most <paramref name="maxCount"/> entries.
        /// </summary>
        public static List<Candidate> OrderCandidates(
            IList<Candidate> candidates,
            Func<int, bool> isBlocked,
            int maxCount)
        {
            var result = new List<Candidate>(candidates.Count);
            for (int i = 0; i < candidates.Count; i++)
            {
                if (!isBlocked(candidates[i].Id))
                {
                    result.Add(candidates[i]);
                }
            }

            result.Sort((a, b) => a.Distance.CompareTo(b.Distance));
            if (result.Count > maxCount)
            {
                result.RemoveRange(maxCount, result.Count - maxCount);
            }

            return result;
        }

        /// <summary>
        /// True when the agent has made (almost) no progress over the observation window although it still has
        /// a long way to go, i.e. it is wedged against geometry.
        /// </summary>
        public static bool IsStuck(float movedMeters, float windowSeconds, float remainingDistance)
        {
            return windowSeconds >= 2.5f && movedMeters < 0.6f && remainingDistance > 3.0f;
        }
    }
}
