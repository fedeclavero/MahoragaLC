using System;

namespace MahoragaLC.AI
{
    /// <summary>
    /// Damage / stun families Mahoraga adapts to independently. Values are stable (they travel in RPCs and masks):
    /// new categories must only ever be appended.
    /// </summary>
    public enum AdaptationCategory : byte
    {
        Melee = 0,
        Shotgun = 1,
        ZapGun = 2,
        StunGrenade = 3,
        Explosion = 4,
        Other = 5,
        OldBirdMissile = 6,
        EnemyMelee = 7
    }

    /// <summary>
    /// Progressive adaptation tuning: after <c>StageExposureThresholds[i]</c> total exposures of a category the
    /// Wheel reaches stage i+1 and the category is resisted by <c>StageResistances[i]</c>. The last stage is full
    /// adaptation (heal + category locked in). Stage 0 (never exposed) always has 0 % resistance.
    /// </summary>
    public sealed class AdaptationSettings
    {
        public const int MaxStages = 4;

        public int[] StageExposureThresholds { get; }
        public float[] StageResistances { get; }
        public float HealPercent { get; }
        /// <summary>Resistance of the Old Bird missile category once fully adapted (lower than the general one so a coordinated flock can still kill).</summary>
        public float OldBirdMissileFullResistance { get; }

        public int StageCount => StageExposureThresholds.Length;
        public float FullResistance => StageResistances[StageResistances.Length - 1];

        public AdaptationSettings(int[] stageExposureThresholds, float[] stageResistances, float healPercent, float oldBirdMissileFullResistance = -1f)
        {
            if (stageExposureThresholds == null || stageExposureThresholds.Length == 0)
            {
                throw new ArgumentException("At least one adaptation stage is required.", nameof(stageExposureThresholds));
            }

            int count = Math.Min(MaxStages, stageExposureThresholds.Length);
            var thresholds = new int[count];
            var resistances = new float[count];
            int prevThreshold = 0;
            float prevResistance = 0f;
            for (int i = 0; i < count; i++)
            {
                thresholds[i] = Math.Max(prevThreshold, Math.Max(1, stageExposureThresholds[i]));
                prevThreshold = thresholds[i];

                float r = (stageResistances != null && i < stageResistances.Length) ? stageResistances[i] : prevResistance;
                resistances[i] = Math.Max(prevResistance, Math.Clamp(r, 0f, 1f));
                prevResistance = resistances[i];
            }

            StageExposureThresholds = thresholds;
            StageResistances = resistances;
            HealPercent = Math.Clamp(healPercent, 0f, 0.50f);
            OldBirdMissileFullResistance = oldBirdMissileFullResistance < 0f
                ? resistances[count - 1]
                : Math.Clamp(oldBirdMissileFullResistance, resistances[Math.Max(0, count - 2)], 1f);
        }

        /// <summary>Default anime-style 3-stage ramp (e.g. 0 % -> 40 % -> 70 % -> 95 % with heal).</summary>
        public static AdaptationSettings CreateProgressive(
            int hitsPerStage = 1,
            float stage1Resistance = 0.40f,
            float stage2Resistance = 0.70f,
            float fullResistance = 0.95f,
            float healPercent = 0.20f,
            float oldBirdMissileFullResistance = -1f)
        {
            int h = Math.Max(1, hitsPerStage);
            return new AdaptationSettings(
                new[] { h, h * 2, h * 3 },
                new[] { stage1Resistance, stage2Resistance, fullResistance },
                healPercent,
                oldBirdMissileFullResistance);
        }

        /// <summary>Single-step behaviour of the 1.0.0 alpha: N exposures and the category is adapted outright.</summary>
        public static AdaptationSettings CreateInstant(int threshold, float resistance, float healPercent)
        {
            return new AdaptationSettings(new[] { Math.Max(1, threshold) }, new[] { resistance }, healPercent);
        }

        /// <summary>Stage (0..StageCount) reached after the given number of exposures.</summary>
        public int StageForExposures(int exposures)
        {
            int stage = 0;
            for (int i = 0; i < StageExposureThresholds.Length; i++)
            {
                if (exposures >= StageExposureThresholds[i])
                {
                    stage = i + 1;
                }
            }
            return stage;
        }

        /// <summary>Resistance for a category at a stage; the final stage of Old Bird missiles uses its own (lower) ceiling.</summary>
        public float ResistanceFor(AdaptationCategory category, int stage)
        {
            if (stage >= StageCount && category == AdaptationCategory.OldBirdMissile)
            {
                return OldBirdMissileFullResistance;
            }
            return ResistanceForStage(stage);
        }

        /// <summary>Resistance (0..1) granted while a category sits at <paramref name="stage"/>.</summary>
        public float ResistanceForStage(int stage)
        {
            if (stage <= 0)
            {
                return 0f;
            }
            int idx = Math.Min(stage, StageResistances.Length) - 1;
            return StageResistances[idx];
        }
    }

    public readonly struct DamageEvaluation
    {
        public readonly AdaptationCategory Category;
        public readonly int EffectiveDamage;
        public readonly int FinalHP;
        public readonly int HealAmount;
        public readonly bool WasAlreadyAdapted;
        /// <summary>True only when this hit completed the FINAL stage (full adaptation: heal + Adapting stance).</summary>
        public readonly bool TriggeredAdaptation;
        public readonly bool IsLethal;
        /// <summary>True when this hit advanced the Wheel by at least one stage but not to full adaptation (quick "click").</summary>
        public readonly bool TriggeredWheelClick;
        public readonly int StageBefore;
        public readonly int StageAfter;
        public readonly float ResistanceApplied;

        public int WheelStepsGained => StageAfter - StageBefore;

        public DamageEvaluation(
            AdaptationCategory category,
            int effectiveDamage,
            int finalHP,
            int healAmount,
            bool wasAlreadyAdapted,
            bool triggeredAdaptation,
            bool isLethal,
            bool triggeredWheelClick = false,
            int stageBefore = 0,
            int stageAfter = 0,
            float resistanceApplied = 0f)
        {
            Category = category;
            EffectiveDamage = effectiveDamage;
            FinalHP = finalHP;
            HealAmount = healAmount;
            WasAlreadyAdapted = wasAlreadyAdapted;
            TriggeredAdaptation = triggeredAdaptation;
            IsLethal = isLethal;
            TriggeredWheelClick = triggeredWheelClick;
            StageBefore = stageBefore;
            StageAfter = stageAfter;
            ResistanceApplied = resistanceApplied;
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
        public readonly bool TriggeredWheelClick;
        public readonly int StageBefore;
        public readonly int StageAfter;
        /// <summary>Multiplier (0..1) to apply to the stun duration for this exposure (1 = full stun, 0 = none).</summary>
        public readonly float StunTimeMultiplier;

        public int WheelStepsGained => StageAfter - StageBefore;

        public StunEvaluation(
            AdaptationCategory category,
            bool negated,
            bool wasAlreadyAdapted,
            bool triggeredAdaptation,
            bool exposureIncremented,
            int finalHP,
            int healAmount,
            bool triggeredWheelClick = false,
            int stageBefore = 0,
            int stageAfter = 0,
            float stunTimeMultiplier = 1f)
        {
            Category = category;
            Negated = negated;
            WasAlreadyAdapted = wasAlreadyAdapted;
            TriggeredAdaptation = triggeredAdaptation;
            ExposureIncremented = exposureIncremented;
            FinalHP = finalHP;
            HealAmount = healAmount;
            TriggeredWheelClick = triggeredWheelClick;
            StageBefore = stageBefore;
            StageAfter = stageAfter;
            StunTimeMultiplier = stunTimeMultiplier;
        }
    }

    /// <summary>
    /// Pure C# state machine for Mahoraga's Eight-Handled Dharma Wheel adaptation math.
    /// Zero UnityEngine / Netcode dependencies so all edge cases are directly unit-testable.
    ///
    /// Each category advances through stages as it is exposed (see <see cref="AdaptationSettings"/>). Every stage
    /// advance turns the Wheel one spoke (<see cref="WheelStepCount"/>); only the last stage is "full adaptation"
    /// (heal + category blocked + <see cref="AdaptedMask"/> bit). Damage / stun of an exposure is always evaluated
    /// with the resistance the category had BEFORE that exposure, like the anime: the wheel turns after being hit.
    /// </summary>
    public sealed class MahoragaAdaptationSystem
    {
        public const int CategoryCount = 8;
        public const float AdaptingArmorMultiplier = 0.25f;
        public const float ZapFreshEngagementGapSeconds = 1.5f;
        public const float ZapSustainedExposureSeconds = 3.0f;
        public const float ExplosionDedupWindowSeconds = 0.15f;

        private readonly int[] _exposureCounts = new int[CategoryCount];
        private readonly int[] _stages = new int[CategoryCount];
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

        /// <summary>Current stage of a category (server-side bookkeeping; clients only know the adapted mask).</summary>
        public int GetStage(AdaptationCategory category)
        {
            int idx = (int)category;
            return idx >= 0 && idx < CategoryCount ? _stages[idx] : 0;
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
            bool recentExplosionNearby,
            bool recentOldBirdMissileNearby = false,
            bool enemyAttackerSuspected = false)
        {
            bool playerWeaponEvidence = heldIsShovelOrKnife || heldIsShotgun;

            // A tracked Old Bird missile blast wins over generic explosions (they are a separate category).
            if (recentOldBirdMissileNearby && (!hasAttacker || !playerWeaponEvidence))
            {
                return AdaptationCategory.OldBirdMissile;
            }

            // A player visibly wielding a melee weapon / shotgun keeps their category even if a blast happens nearby.
            if ((recentExplosionNearby && (!hasAttacker || !playerWeaponEvidence)) ||
                (!hasAttacker && force == 6 && hitID == -1))
            {
                return AdaptationCategory.Explosion;
            }

            if (hitID == 1 || hitID == 5 || heldIsShovelOrKnife)
            {
                return AdaptationCategory.Melee;
            }

            // Shotgun blasts deal 1..5 force; anything bigger without a shotgun (e.g. Cruiser, force 12) is "Other".
            if (hasAttacker && (heldIsShotgun || (hitID == -1 && force >= 2 && force <= 5)))
            {
                return AdaptationCategory.Shotgun;
            }

            if (!hasAttacker && enemyAttackerSuspected)
            {
                return AdaptationCategory.EnemyMelee;
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
            AdaptationSettings settings,
            float now,
            int hpFloor = 0)
        {
            if (category == AdaptationCategory.Explosion || category == AdaptationCategory.OldBirdMissile)
            {
                _lastExplosionHitTime = now;
            }

            int idx = (int)category;
            bool alreadyAdapted = IsAdapted(category);
            int stageBefore = _stages[idx];
            float resistance = alreadyAdapted ? settings.ResistanceFor(category, settings.StageCount) : settings.ResistanceFor(category, stageBefore);
            int safeForce = Math.Max(0, rawForce);
            int effectiveDamage;

            if (resistance >= 0.999f)
            {
                effectiveDamage = 0;
            }
            else
            {
                float mult = 1f - resistance;
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

            // Optional protection floor (e.g. a single Old Bird can never finish Mahoraga): clips the damage, never heals.
            if (hpFloor > 0 && currentHP - effectiveDamage < hpFloor)
            {
                effectiveDamage = Math.Max(0, currentHP - hpFloor);
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
                    isLethal: true,
                    stageBefore: stageBefore,
                    stageAfter: stageBefore,
                    resistanceApplied: resistance);
            }

            // Already fully adapted, or bracing mid-stance: no further Wheel progress.
            if (alreadyAdapted || isCurrentlyAdapting)
            {
                return new DamageEvaluation(
                    category: category,
                    effectiveDamage: effectiveDamage,
                    finalHP: hpAfterDamage,
                    healAmount: 0,
                    wasAlreadyAdapted: alreadyAdapted,
                    triggeredAdaptation: false,
                    isLethal: false,
                    stageBefore: stageBefore,
                    stageAfter: stageBefore,
                    resistanceApplied: resistance);
            }

            _exposureCounts[idx]++;
            int stageAfter = settings.StageForExposures(_exposureCounts[idx]);
            if (stageAfter <= stageBefore)
            {
                return new DamageEvaluation(
                    category: category,
                    effectiveDamage: effectiveDamage,
                    finalHP: hpAfterDamage,
                    healAmount: 0,
                    wasAlreadyAdapted: false,
                    triggeredAdaptation: false,
                    isLethal: false,
                    stageBefore: stageBefore,
                    stageAfter: stageBefore,
                    resistanceApplied: resistance);
            }

            AdvanceStage(category, stageBefore, stageAfter, settings, out bool reachedFull);
            if (reachedFull)
            {
                int heal = ComputeHeal(hpAfterDamage, maxHP, settings.HealPercent);
                return new DamageEvaluation(
                    category: category,
                    effectiveDamage: effectiveDamage,
                    finalHP: hpAfterDamage + heal,
                    healAmount: heal,
                    wasAlreadyAdapted: false,
                    triggeredAdaptation: true,
                    isLethal: false,
                    stageBefore: stageBefore,
                    stageAfter: stageAfter,
                    resistanceApplied: resistance);
            }

            return new DamageEvaluation(
                category: category,
                effectiveDamage: effectiveDamage,
                finalHP: hpAfterDamage,
                healAmount: 0,
                wasAlreadyAdapted: false,
                triggeredAdaptation: false,
                isLethal: false,
                triggeredWheelClick: true,
                stageBefore: stageBefore,
                stageAfter: stageAfter,
                resistanceApplied: resistance);
        }

        public StunEvaluation EvaluateStun(
            AdaptationCategory category,
            int currentHP,
            int maxHP,
            bool isCurrentlyAdapting,
            AdaptationSettings settings,
            float now)
        {
            int idx = (int)category;
            bool alreadyAdapted = IsAdapted(category);
            int stageBefore = _stages[idx];
            if (alreadyAdapted || isCurrentlyAdapting)
            {
                return new StunEvaluation(
                    category: category,
                    negated: true,
                    wasAlreadyAdapted: alreadyAdapted,
                    triggeredAdaptation: false,
                    exposureIncremented: false,
                    finalHP: currentHP,
                    healAmount: 0,
                    stageBefore: stageBefore,
                    stageAfter: stageBefore,
                    stunTimeMultiplier: 0f);
            }

            float stunMultiplier = 1f - settings.ResistanceForStage(stageBefore);

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
                        healAmount: 0,
                        stageBefore: stageBefore,
                        stageAfter: stageBefore,
                        stunTimeMultiplier: stunMultiplier);
                }

                _lastZapExposureTime = now;
            }

            _exposureCounts[idx]++;
            int stageAfter = settings.StageForExposures(_exposureCounts[idx]);
            if (stageAfter <= stageBefore)
            {
                return new StunEvaluation(
                    category: category,
                    negated: false,
                    wasAlreadyAdapted: false,
                    triggeredAdaptation: false,
                    exposureIncremented: true,
                    finalHP: currentHP,
                    healAmount: 0,
                    stageBefore: stageBefore,
                    stageAfter: stageBefore,
                    stunTimeMultiplier: stunMultiplier);
            }

            AdvanceStage(category, stageBefore, stageAfter, settings, out bool reachedFull);
            if (reachedFull)
            {
                int heal = ComputeHeal(currentHP, maxHP, settings.HealPercent);
                return new StunEvaluation(
                    category: category,
                    negated: true,
                    wasAlreadyAdapted: false,
                    triggeredAdaptation: true,
                    exposureIncremented: true,
                    finalHP: currentHP + heal,
                    healAmount: heal,
                    stageBefore: stageBefore,
                    stageAfter: stageAfter,
                    stunTimeMultiplier: 0f);
            }

            return new StunEvaluation(
                category: category,
                negated: false,
                wasAlreadyAdapted: false,
                triggeredAdaptation: false,
                exposureIncremented: true,
                finalHP: currentHP,
                healAmount: 0,
                triggeredWheelClick: true,
                stageBefore: stageBefore,
                stageAfter: stageAfter,
                stunTimeMultiplier: stunMultiplier);
        }

        /// <summary>
        /// Called every server Update while PatcherTool holds stunnedIndefinitely &gt; 0.
        /// Keeps the continuous-beam timestamp alive and emits an exposure tick every 3.0s.
        /// </summary>
        public StunEvaluation EvaluateSustainedZapBeam(
            int currentHP,
            int maxHP,
            bool isCurrentlyAdapting,
            AdaptationSettings settings,
            float now)
        {
            int stage = _stages[(int)AdaptationCategory.ZapGun];
            if (IsAdapted(AdaptationCategory.ZapGun) || isCurrentlyAdapting)
            {
                return new StunEvaluation(
                    AdaptationCategory.ZapGun,
                    negated: true,
                    wasAlreadyAdapted: IsAdapted(AdaptationCategory.ZapGun),
                    triggeredAdaptation: false,
                    exposureIncremented: false,
                    finalHP: currentHP,
                    healAmount: 0,
                    stageBefore: stage,
                    stageAfter: stage,
                    stunTimeMultiplier: 0f);
            }

            float stunMultiplier = 1f - settings.ResistanceForStage(stage);

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
                    healAmount: 0,
                    stageBefore: stage,
                    stageAfter: stage,
                    stunTimeMultiplier: stunMultiplier);
            }

            _lastZapEventTime = now;
            if ((now - _lastZapExposureTime) >= ZapSustainedExposureSeconds)
            {
                return EvaluateStun(
                    AdaptationCategory.ZapGun,
                    currentHP,
                    maxHP,
                    isCurrentlyAdapting,
                    settings,
                    now);
            }

            return new StunEvaluation(
                AdaptationCategory.ZapGun,
                negated: false,
                wasAlreadyAdapted: false,
                triggeredAdaptation: false,
                exposureIncremented: false,
                finalHP: currentHP,
                healAmount: 0,
                stageBefore: stage,
                stageAfter: stage,
                stunTimeMultiplier: stunMultiplier);
        }

        private void AdvanceStage(AdaptationCategory category, int stageBefore, int stageAfter, AdaptationSettings settings, out bool reachedFull)
        {
            int idx = (int)category;
            _stages[idx] = stageAfter;
            _wheelStepCount += stageAfter - stageBefore;
            reachedFull = stageAfter >= settings.StageCount;
            if (reachedFull)
            {
                _adaptedMask |= 1 << idx;
            }
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
