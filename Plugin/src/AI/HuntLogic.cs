namespace MahoragaLC.AI
{
    /// <summary>Domain checks for game positions.</summary>
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
    }
}
