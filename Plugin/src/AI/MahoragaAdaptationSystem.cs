using System;

namespace MahoragaLC.AI
{
    public enum AdaptationCategory : byte
    {
        Melee = 0,
        Shotgun = 1,
        ZapGun = 2,
        StunGrenade = 3,
        Explosion = 4,
        Other = 5
    }

    public readonly struct DamageEvaluation
    {
        public readonly AdaptationCategory Category;
        public readonly int EffectiveDamage;
        public readonly int FinalHP;
        public readonly int HealAmount;
        public readonly bool WasAlreadyAdapted;
        public readonly bool TriggeredAdaptation;
        public readonly bool IsLethal;

        public DamageEvaluation(
            AdaptationCategory category,
            int effectiveDamage,
            int finalHP,
            int healAmount,
            bool wasAlreadyAdapted,
            bool triggeredAdaptation,
            bool isLethal)
        {
            Category = category;
            EffectiveDamage = effectiveDamage;
            FinalHP = finalHP;
            HealAmount = healAmount;
            WasAlreadyAdapted = wasAlreadyAdapted;
            TriggeredAdaptation = triggeredAdaptation;
            IsLethal = isLethal;
        }
    }

    public readonly struct StunEvaluation
    {
        public readonly AdaptationCategory Category;
        public readonly bool Negated;
        public readonly bool WasAlreadyAdapted;
        public readonly bool TriggeredAdaptation;
        public readonly bool ExposureIncremented;
        public readonly int FinalHP;
        public readonly int HealAmount;

        public StunEvaluation(
            AdaptationCategory category,
            bool negated,
            bool wasAlreadyAdapted,
            bool triggeredAdaptation,
            bool exposureIncremented,
            int finalHP,
            int healAmount)
        {
            Category = category;
            Negated = negated;
            WasAlreadyAdapted = wasAlreadyAdapted;
            TriggeredAdaptation = triggeredAdaptation;
            ExposureIncremented = exposureIncremented;
            FinalHP = finalHP;
            HealAmount = healAmount;
        }
    }

    /// <summary>
    /// Pure C# state machine for Mahoraga's Eight-Handled Dharma Wheel adaptation math.
    /// Zero UnityEngine / Netcode dependencies so all edge cases are directly unit-testable.
    /// </summary>
    public sealed class MahoragaAdaptationSystem
    {
        public const int CategoryCount = 6;
        public const float AdaptingArmorMultiplier = 0.25f;
        public const float ZapFreshEngagementGapSeconds = 1.5f;
        public const float ZapSustainedExposureSeconds = 3.0f;
        public const float ExplosionDedupWindowSeconds = 0.15f;

        private readonly int[] _exposureCounts = new int[CategoryCount];
        private int _adaptedMask;
        private int _wheelStepCount;
        private float _fractionalDamageBuffer;
        private float _lastZapEventTime = -999f;
        private float _lastZapExposureTime = -999f;
        private float _lastExplosionHitTime = -999f;

        public int AdaptedMask => _adaptedMask;
        public int WheelStepCount => _wheelStepCount;

        public int AdaptedCategoryCount
        {
            get
            {
                int count = 0;
                int mask = _adaptedMask;
                while (mask != 0)
                {
                    mask &= mask - 1;
                    count++;
                }
                return count;
            }
        }

        public bool IsAdapted(AdaptationCategory category)
        {
            return (_adaptedMask & (1 << (int)category)) != 0;
        }

        public int GetExposureCount(AdaptationCategory category)
        {
            int idx = (int)category;
            return idx >= 0 && idx < CategoryCount ? _exposureCounts[idx] : 0;
        }

        public void SyncFromServer(int adaptedMask, int wheelStepCount)
        {
            _adaptedMask = adaptedMask;
            _wheelStepCount = Math.Max(0, wheelStepCount);
        }

        public bool IsDuplicateExplosion(float now, float windowSeconds = ExplosionDedupWindowSeconds)
        {
            return (now - _lastExplosionHitTime) <= windowSeconds;
        }

        public static AdaptationCategory ClassifyDamage(
            int hitID,
            int force,
            bool hasAttacker,
            bool heldIsShovelOrKnife,
            bool heldIsShotgun,
            bool recentExplosionNearby)
        {
            if (recentExplosionNearby || (!hasAttacker && force == 6 && hitID == -1))
            {
                return AdaptationCategory.Explosion;
            }

            if (hitID == 1 || hitID == 5 || heldIsShovelOrKnife)
            {
                return AdaptationCategory.Melee;
            }

            if (hasAttacker && (heldIsShotgun || (hitID == -1 && force >= 2)))
            {
                return AdaptationCategory.Shotgun;
            }

            return AdaptationCategory.Other;
        }

        public static AdaptationCategory ClassifyStun(bool stunnerHoldingPatcherTool)
        {
            return stunnerHoldingPatcherTool ? AdaptationCategory.ZapGun : AdaptationCategory.StunGrenade;
        }

        public DamageEvaluation EvaluateDamage(
            AdaptationCategory category,
            int rawForce,
            int currentHP,
            int maxHP,
            bool isCurrentlyAdapting,
            int threshold,
            float adaptedResistance,
            float healPercent,
            float now)
        {
            if (category == AdaptationCategory.Explosion)
            {
                _lastExplosionHitTime = now;
            }

            bool alreadyAdapted = IsAdapted(category);
            float clampedResistance = Math.Clamp(adaptedResistance, 0f, 1f);
            int safeForce = Math.Max(0, rawForce);
            int effectiveDamage;

            if (alreadyAdapted && clampedResistance >= 0.999f)
            {
                effectiveDamage = 0;
            }
            else
            {
                float mult = 1f;
                if (alreadyAdapted)
                {
                    mult *= (1f - clampedResistance);
                }
                if (isCurrentlyAdapting)
                {
                    mult *= AdaptingArmorMultiplier;
                }

                if (mult >= 0.999f)
                {
                    effectiveDamage = safeForce;
                }
                else
                {
                    _fractionalDamageBuffer += safeForce * mult;
                    effectiveDamage = (int)Math.Floor(_fractionalDamageBuffer + 1e-5f);
                    _fractionalDamageBuffer = Math.Max(0f, _fractionalDamageBuffer - effectiveDamage);
                }
            }

            int hpAfterDamage = currentHP - effectiveDamage;

            // Lethal-blow precedence: a killing blow destroys Mahoraga before the Wheel can turn.
            if (hpAfterDamage <= 0)
            {
                return new DamageEvaluation(
                    category: category,
                    effectiveDamage: effectiveDamage,
                    finalHP: 0,
                    healAmount: 0,
                    wasAlreadyAdapted: alreadyAdapted,
                    triggeredAdaptation: false,
                    isLethal: true);
            }

            if (alreadyAdapted)
            {
                return new DamageEvaluation(
                    category: category,
                    effectiveDamage: effectiveDamage,
                    finalHP: hpAfterDamage,
                    healAmount: 0,
                    wasAlreadyAdapted: true,
                    triggeredAdaptation: false,
                    isLethal: false);
            }

            int idx = (int)category;
            _exposureCounts[idx]++;

            int safeThreshold = Math.Max(1, threshold);
            if (!isCurrentlyAdapting && _exposureCounts[idx] >= safeThreshold)
            {
                MarkAdapted(category);
                int heal = ComputeHeal(hpAfterDamage, maxHP, healPercent);
                return new DamageEvaluation(
                    category: category,
                    effectiveDamage: effectiveDamage,
                    finalHP: hpAfterDamage + heal,
                    healAmount: heal,
                    wasAlreadyAdapted: false,
                    triggeredAdaptation: true,
                    isLethal: false);
            }

            return new DamageEvaluation(
                category: category,
                effectiveDamage: effectiveDamage,
                finalHP: hpAfterDamage,
                healAmount: 0,
                wasAlreadyAdapted: false,
                triggeredAdaptation: false,
                isLethal: false);
        }

        public StunEvaluation EvaluateStun(
            AdaptationCategory category,
            int currentHP,
            int maxHP,
            bool isCurrentlyAdapting,
            int threshold,
            float healPercent,
            float now)
        {
            bool alreadyAdapted = IsAdapted(category);
            if (alreadyAdapted || isCurrentlyAdapting)
            {
                return new StunEvaluation(
                    category: category,
                    negated: true,
                    wasAlreadyAdapted: alreadyAdapted,
                    triggeredAdaptation: false,
                    exposureIncremented: false,
                    finalHP: currentHP,
                    healAmount: 0);
            }

            if (category == AdaptationCategory.ZapGun)
            {
                bool isFreshEngagement = (now - _lastZapEventTime) > ZapFreshEngagementGapSeconds;
                bool isSustainedTick = (now - _lastZapExposureTime) >= ZapSustainedExposureSeconds;
                _lastZapEventTime = now;

                if (!isFreshEngagement && !isSustainedTick)
                {
                    return new StunEvaluation(
                        category: category,
                        negated: false,
                        wasAlreadyAdapted: false,
                        triggeredAdaptation: false,
                        exposureIncremented: false,
                        finalHP: currentHP,
                        healAmount: 0);
                }

                _lastZapExposureTime = now;
            }

            int idx = (int)category;
            _exposureCounts[idx]++;

            int safeThreshold = Math.Max(1, threshold);
            if (_exposureCounts[idx] >= safeThreshold)
            {
                MarkAdapted(category);
                int heal = ComputeHeal(currentHP, maxHP, healPercent);
                return new StunEvaluation(
                    category: category,
                    negated: true,
                    wasAlreadyAdapted: false,
                    triggeredAdaptation: true,
                    exposureIncremented: true,
                    finalHP: currentHP + heal,
                    healAmount: heal);
            }

            return new StunEvaluation(
                category: category,
                negated: false,
                wasAlreadyAdapted: false,
                triggeredAdaptation: false,
                exposureIncremented: true,
                finalHP: currentHP,
                healAmount: 0);
        }

        /// <summary>
        /// Called every server Update while PatcherTool holds stunnedIndefinitely &gt; 0.
        /// Keeps the continuous-beam timestamp alive and emits an exposure tick every 3.0s.
        /// </summary>
        public StunEvaluation EvaluateSustainedZapBeam(
            int currentHP,
            int maxHP,
            bool isCurrentlyAdapting,
            int threshold,
            float healPercent,
            float now)
        {
            if (IsAdapted(AdaptationCategory.ZapGun) || isCurrentlyAdapting)
            {
                return new StunEvaluation(
                    AdaptationCategory.ZapGun,
                    negated: true,
                    wasAlreadyAdapted: IsAdapted(AdaptationCategory.ZapGun),
                    triggeredAdaptation: false,
                    exposureIncremented: false,
                    finalHP: currentHP,
                    healAmount: 0);
            }

            // Initialize exposure clock if beam started without passing through EvaluateStun.
            if (_lastZapExposureTime < -100f)
            {
                _lastZapEventTime = now;
                _lastZapExposureTime = now;
                return new StunEvaluation(
                    AdaptationCategory.ZapGun,
                    negated: false,
                    wasAlreadyAdapted: false,
                    triggeredAdaptation: false,
                    exposureIncremented: false,
                    finalHP: currentHP,
                    healAmount: 0);
            }

            _lastZapEventTime = now;
            if ((now - _lastZapExposureTime) >= ZapSustainedExposureSeconds)
            {
                return EvaluateStun(
                    AdaptationCategory.ZapGun,
                    currentHP,
                    maxHP,
                    isCurrentlyAdapting,
                    threshold,
                    healPercent,
                    now);
            }

            return new StunEvaluation(
                AdaptationCategory.ZapGun,
                negated: false,
                wasAlreadyAdapted: false,
                triggeredAdaptation: false,
                exposureIncremented: false,
                finalHP: currentHP,
                healAmount: 0);
        }

        private void MarkAdapted(AdaptationCategory category)
        {
            _adaptedMask |= 1 << (int)category;
            _wheelStepCount++;
        }

        private static int ComputeHeal(int currentHP, int maxHP, float healPercent)
        {
            float clampedHealPct = Math.Clamp(healPercent, 0f, 0.50f);
            int desiredHeal = (int)Math.Round(maxHP * clampedHealPct, MidpointRounding.AwayFromZero);
            int targetHP = Math.Min(maxHP, currentHP + Math.Max(0, desiredHeal));
            return Math.Max(0, targetHP - currentHP);
        }
    }
}
