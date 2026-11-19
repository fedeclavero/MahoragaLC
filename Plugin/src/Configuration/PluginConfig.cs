using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Configuration;
using LethalLib.Modules;
using MahoragaLC.AI;

namespace MahoragaLC.Config
{
    public sealed class MahoragaConfig
    {
        public static MahoragaConfig? Current { get; private set; }

        // [Spawning]
        public ConfigEntry<bool> EnableMod { get; }
        public ConfigEntry<int> DefaultInteriorRarity { get; }
        public ConfigEntry<string> MoonRarityOverrides { get; }
        public ConfigEntry<bool> AllowOutdoorSpawn { get; }
        public ConfigEntry<int> OutdoorRarity { get; }
        public ConfigEntry<float> PowerLevel { get; }
        public ConfigEntry<int> MaxCount { get; }

        // [Combat]
        public ConfigEntry<int> MaxHP { get; }
        public ConfigEntry<int> CleaveDamage { get; }
        public ConfigEntry<int> SlamDamage { get; }
        public ConfigEntry<bool> CanForceLockedDoors { get; }
        public ConfigEntry<float> PlayerKnockbackMultiplier { get; }
        public ConfigEntry<float> RetaliationHalfLifeSeconds { get; }
        public ConfigEntry<float> RetaliationSwitchLockoutSeconds { get; }
        public ConfigEntry<float> RegenDelaySeconds { get; }
        public ConfigEntry<float> RegenPercentPerSecond { get; }

        // [Adaptation]
        public ConfigEntry<int> HitsPerAdaptationStage { get; }
        public ConfigEntry<float> Stage1Resistance { get; }
        public ConfigEntry<float> Stage2Resistance { get; }
        public ConfigEntry<float> AdaptedDamageResistance { get; }
        public ConfigEntry<float> AdaptationHealPercent { get; }

        // [EnemyInteraction]
        public ConfigEntry<bool> VisibleToOtherEnemies { get; }
        public ConfigEntry<bool> FightEnemies { get; }
        public ConfigEntry<bool> EngageEnemiesWhenIdle { get; }
        public ConfigEntry<int> EnemyCleaveDamage { get; }
        public ConfigEntry<int> EnemySlamDamage { get; }
        public ConfigEntry<float> EnemyKnockbackMultiplier { get; }
        public ConfigEntry<float> EnemyGrudgeHalfLifeSeconds { get; }
        public ConfigEntry<float> EnemyEngageMaxSeconds { get; }
        public ConfigEntry<float> ImmortalEnemyEngageSeconds { get; }
        public ConfigEntry<float> DisengageCooldownSeconds { get; }

        // [OldBird]
        public ConfigEntry<bool> FightOldBirds { get; }
        public ConfigEntry<string> OldBirdPriorityMode { get; }
        public ConfigEntry<float> OldBirdPriorityDistance { get; }
        public ConfigEntry<float> OldBirdSeekRange { get; }
        public ConfigEntry<float> OldBirdHitsToDestroy { get; }
        public ConfigEntry<float> OldBirdEngageMaxSeconds { get; }
        public ConfigEntry<int> OldBirdsRequiredToKill { get; }
        public ConfigEntry<float> OldBirdKillWindowSeconds { get; }
        public ConfigEntry<float> OldBirdSoloHpFloorFraction { get; }
        public ConfigEntry<float> OldBirdMissileFullResistance { get; }

        // [Growth]
        public ConfigEntry<bool> GrowthEnabled { get; }
        public ConfigEntry<float> GrowthMaxScale { get; }
        public ConfigEntry<int> GrowthStepsToMatch { get; }
        public ConfigEntry<float> GrowthTransitionSeconds { get; }

        /// <summary>Progressive adaptation tuning derived from [Adaptation]; rebuilt whenever one of those entries changes.</summary>
        public AdaptationSettings Adaptation { get; private set; }

        // [Movement]
        public ConfigEntry<float> PatrolSpeed { get; }
        public ConfigEntry<float> InvestigateSpeed { get; }
        public ConfigEntry<float> ChaseSpeed { get; }
        public ConfigEntry<float> ChaseSpeedPerAdaptation { get; }

        // [Audio]
        public ConfigEntry<bool> MusicEnabled { get; }
        public ConfigEntry<float> MusicMasterVolume { get; }
        public ConfigEntry<float> MusicOuterRange { get; }
        public ConfigEntry<float> MusicInnerRange { get; }
        public ConfigEntry<bool> EnableWallMuffling { get; }
        public ConfigEntry<bool> UseCustomMusicOverride { get; }

        private MahoragaConfig(ConfigFile cfg)
        {
            // [Spawning]
            EnableMod = cfg.Bind(
                "Spawning",
                "EnableMod",
                true,
                "Master toggle for registering Mahoraga in Lethal Company.");

            DefaultInteriorRarity = cfg.Bind(
                "Spawning",
                "DefaultInteriorRarity",
                20,
                new ConfigDescription(
                    "Default interior spawn weight across all vanilla and modded moons (0 disables interior spawns unless overridden).",
                    new AcceptableValueRange<int>(0, 100)));

            MoonRarityOverrides = cfg.Bind(
                "Spawning",
                "MoonRarityOverrides",
                "",
                "Comma-separated per-moon spawn weight overrides. Examples: \"Experimentation:10,Titan:35,Artifice:40\" or modded moon names like \"46 Infernis:25\".");

            AllowOutdoorSpawn = cfg.Bind(
                "Spawning",
                "AllowOutdoorSpawn",
                false,
                "If true, also registers an outdoor spawn profile for Mahoraga across all moons.");

            OutdoorRarity = cfg.Bind(
                "Spawning",
                "OutdoorRarity",
                12,
                new ConfigDescription(
                    "Outdoor spawn weight used when AllowOutdoorSpawn is enabled.",
                    new AcceptableValueRange<int>(0, 100)));

            PowerLevel = cfg.Bind(
                "Spawning",
                "PowerLevel",
                3.0f,
                new ConfigDescription(
                    "Moon enemy power budget consumed by each spawned Mahoraga.",
                    new AcceptableValueRange<float>(1.0f, 10.0f)));

            MaxCount = cfg.Bind(
                "Spawning",
                "MaxCount",
                1,
                new ConfigDescription(
                    "Maximum concurrent Mahoraga instances allowed on a moon.",
                    new AcceptableValueRange<int>(1, 5)));

            // [Combat]
            MaxHP = cfg.Bind(
                "Combat",
                "MaxHP",
                18,
                new ConfigDescription(
                    "Base HP of Mahoraga in Lethal Company hit-force units (Shovel=1, Shotgun=2-5, Landmine=6).",
                    new AcceptableValueRange<int>(6, 100)));

            CleaveDamage = cfg.Bind(
                "Combat",
                "CleaveDamage",
                55,
                new ConfigDescription(
                    "Player HP damage dealt by Mahoraga's Sword Cleave attack.",
                    new AcceptableValueRange<int>(10, 100)));

            SlamDamage = cfg.Bind(
                "Combat",
                "SlamDamage",
                65,
                new ConfigDescription(
                    "Player HP damage dealt by Mahoraga's 360-degree Divergent Slam attack.",
                    new AcceptableValueRange<int>(10, 100)));

            CanForceLockedDoors = cfg.Bind(
                "Combat",
                "CanForceLockedDoors",
                true,
                "If true, Mahoraga forces key-locked interior DoorLock doors open after 2.2 seconds of pounding.");

            PlayerKnockbackMultiplier = cfg.Bind(
                "Combat",
                "PlayerKnockbackMultiplier",
                1.0f,
                new ConfigDescription(
                    "Scales the shove players receive from Sword Cleave / Divergent Slam (0 disables knockback).",
                    new AcceptableValueRange<float>(0.0f, 3.0f)));

            RetaliationHalfLifeSeconds = cfg.Bind(
                "Combat",
                "RetaliationHalfLifeSeconds",
                6.0f,
                new ConfigDescription(
                    "How long a player who hurts Mahoraga stays its priority target (aggro weight halves every this many seconds).",
                    new AcceptableValueRange<float>(2.0f, 30.0f)));

            RetaliationSwitchLockoutSeconds = cfg.Bind(
                "Combat",
                "RetaliationSwitchLockoutSeconds",
                2.5f,
                new ConfigDescription(
                    "Minimum time between two retaliation target switches (anti-oscillation hysteresis).",
                    new AcceptableValueRange<float>(0.5f, 10.0f)));

            RegenDelaySeconds = cfg.Bind(
                "Combat",
                "RegenDelaySeconds",
                8.0f,
                new ConfigDescription(
                    "Seconds without taking damage before Mahoraga starts regenerating slowly.",
                    new AcceptableValueRange<float>(1.0f, 60.0f)));

            RegenPercentPerSecond = cfg.Bind(
                "Combat",
                "RegenPercentPerSecond",
                0.01f,
                new ConfigDescription(
                    "Out-of-combat regeneration as a fraction of MaxHP per second (0.01 = 1%/s, 0 disables).",
                    new AcceptableValueRange<float>(0.0f, 0.10f)));

            // [Adaptation]
            HitsPerAdaptationStage = cfg.Bind(
                "Adaptation",
                "HitsPerAdaptationStage",
                1,
                new ConfigDescription(
                    "Exposures of one damage/stun category needed to turn the Wheel one stage. There are 3 stages: the 3rd completes the adaptation (heal + category blocked).",
                    new AcceptableValueRange<int>(1, 5)));

            Stage1Resistance = cfg.Bind(
                "Adaptation",
                "Stage1Resistance",
                0.40f,
                new ConfigDescription(
                    "Resistance to a category after its 1st Wheel turn (0.40 = 40% less damage / stun time). The turn itself is a quick click and does not stop Mahoraga.",
                    new AcceptableValueRange<float>(0.0f, 0.90f)));

            Stage2Resistance = cfg.Bind(
                "Adaptation",
                "Stage2Resistance",
                0.70f,
                new ConfigDescription(
                    "Resistance to a category after its 2nd Wheel turn. Raised to at least Stage1Resistance.",
                    new AcceptableValueRange<float>(0.0f, 0.95f)));

            AdaptedDamageResistance = cfg.Bind(
                "Adaptation",
                "AdaptedDamageResistance",
                0.95f,
                new ConfigDescription(
                    "Resistance once a category is fully adapted (0.95 = 95% resistance, 1.00 = full immunity). Raised to at least Stage2Resistance.",
                    new AcceptableValueRange<float>(0.50f, 1.00f)));

            AdaptationHealPercent = cfg.Bind(
                "Adaptation",
                "AdaptationHealPercent",
                0.20f,
                new ConfigDescription(
                    "Fraction of MaxHP regenerated when a category reaches FULL adaptation (intermediate Wheel turns do not heal).",
                    new AcceptableValueRange<float>(0.00f, 0.50f)));

            // [OldBird]
            FightOldBirds = cfg.Bind(
                "OldBird",
                "FightOldBirds",
                true,
                "If true, Mahoraga hunts living Old Birds (RadMechAI) in its own area (outside/inside) even if they never attacked first, and can destroy them.");

            OldBirdPriorityMode = cfg.Bind(
                "OldBird",
                "OldBirdPriorityMode",
                "WhenClose",
                new ConfigDescription(
                    "Who comes first when both an Old Bird and players are around. WhenClose = the Old Bird wins while it is within OldBirdPriorityDistance or attacking; Always = Old Birds first (except players who hurt Mahoraga); PlayersFirst = only when no player is in sight.",
                    new AcceptableValueList<string>("WhenClose", "Always", "PlayersFirst")));

            OldBirdPriorityDistance = cfg.Bind(
                "OldBird",
                "OldBirdPriorityDistance",
                20.0f,
                new ConfigDescription(
                    "Fighting distance (meters) inside which an Old Bird outranks players in WhenClose mode.",
                    new AcceptableValueRange<float>(5.0f, 80.0f)));

            OldBirdSeekRange = cfg.Bind(
                "OldBird",
                "OldBirdSeekRange",
                70.0f,
                new ConfigDescription(
                    "Maximum distance (meters) at which Mahoraga goes looking for an Old Bird.",
                    new AcceptableValueRange<float>(10.0f, 200.0f)));

            OldBirdHitsToDestroy = cfg.Bind(
                "OldBird",
                "OldBirdHitsToDestroy",
                8.0f,
                new ConfigDescription(
                    "Cleave hits needed to destroy an Old Bird at Mahoraga's base size (a Slam counts double). A grown Mahoraga hits proportionally harder, so it needs fewer.",
                    new AcceptableValueRange<float>(1.0f, 100.0f)));

            OldBirdEngageMaxSeconds = cfg.Bind(
                "OldBird",
                "OldBirdEngageMaxSeconds",
                45.0f,
                new ConfigDescription(
                    "Hard cap for one hunt against a single Old Bird before Mahoraga gives up (see DisengageCooldownSeconds).",
                    new AcceptableValueRange<float>(10.0f, 300.0f)));

            OldBirdsRequiredToKill = cfg.Bind(
                "OldBird",
                "OldBirdsRequiredToKill",
                5,
                new ConfigDescription(
                    "Old Birds that must be hitting Mahoraga within OldBirdKillWindowSeconds before their missiles may push it below the HP floor (fewer Old Birds can never kill it).",
                    new AcceptableValueRange<int>(1, 10)));

            OldBirdKillWindowSeconds = cfg.Bind(
                "OldBird",
                "OldBirdKillWindowSeconds",
                6.0f,
                new ConfigDescription(
                    "Sliding window used to count how many different Old Birds are attacking Mahoraga at the same time.",
                    new AcceptableValueRange<float>(1.0f, 30.0f)));

            OldBirdSoloHpFloorFraction = cfg.Bind(
                "OldBird",
                "OldBirdSoloHpFloorFraction",
                0.30f,
                new ConfigDescription(
                    "Fraction of MaxHP that Old Bird missiles cannot take Mahoraga below unless OldBirdsRequiredToKill of them attack together.",
                    new AcceptableValueRange<float>(0.05f, 0.90f)));

            OldBirdMissileFullResistance = cfg.Bind(
                "OldBird",
                "OldBirdMissileFullResistance",
                0.90f,
                new ConfigDescription(
                    "Resistance to Old Bird missiles once that category is fully adapted (kept below 1.0 so a big flock can still wear it down).",
                    new AcceptableValueRange<float>(0.50f, 1.00f)));

            // [Growth]
            GrowthEnabled = cfg.Bind(
                "Growth",
                "GrowthEnabled",
                true,
                "If true, Mahoraga grows (outdoors only) every time an Old Bird hurts it, up to the Old Bird's own height.");

            GrowthMaxScale = cfg.Bind(
                "Growth",
                "GrowthMaxScale",
                2.0f,
                new ConfigDescription(
                    "Maximum size multiplier (1.0 = never grows). The real target is the measured Old Bird height, capped by this.",
                    new AcceptableValueRange<float>(1.0f, 3.0f)));

            GrowthStepsToMatch = cfg.Bind(
                "Growth",
                "GrowthStepsToMatch",
                6,
                new ConfigDescription(
                    "Old Bird damage events needed to grow from base size all the way to the Old Bird's height.",
                    new AcceptableValueRange<int>(1, 20)));

            GrowthTransitionSeconds = cfg.Bind(
                "Growth",
                "GrowthTransitionSeconds",
                2.0f,
                new ConfigDescription(
                    "Approximate seconds each growth step takes to animate.",
                    new AcceptableValueRange<float>(0.2f, 10.0f)));

            // [EnemyInteraction]
            VisibleToOtherEnemies = cfg.Bind(
                "EnemyInteraction",
                "VisibleToOtherEnemies",
                true,
                "If true, other enemies that watch for threats (Old Birds, Baboon Hawks, Giant Kiwis...) can detect Mahoraga and react to it. Takes effect for new sightings immediately.");

            FightEnemies = cfg.Bind(
                "EnemyInteraction",
                "FightEnemies",
                true,
                "If true, Mahoraga retaliates against enemies that hurt it or block its way (players always come first).");

            EngageEnemiesWhenIdle = cfg.Bind(
                "EnemyInteraction",
                "EngageEnemiesWhenIdle",
                false,
                "If true, Mahoraga also attacks any killable enemy it sees while patrolling with no player around, without waiting to be provoked.");

            EnemyCleaveDamage = cfg.Bind(
                "EnemyInteraction",
                "EnemyCleaveDamage",
                6,
                new ConfigDescription(
                    "Hit force dealt to an enemy by Sword Cleave (same units as a Shovel = 1, Shotgun = 2-5, Landmine = 6 against enemy HP).",
                    new AcceptableValueRange<int>(1, 50)));

            EnemySlamDamage = cfg.Bind(
                "EnemyInteraction",
                "EnemySlamDamage",
                10,
                new ConfigDescription(
                    "Hit force dealt to an enemy by the Divergent Slam.",
                    new AcceptableValueRange<int>(1, 50)));

            EnemyKnockbackMultiplier = cfg.Bind(
                "EnemyInteraction",
                "EnemyKnockbackMultiplier",
                1.0f,
                new ConfigDescription(
                    "Scales the shove enemies receive from Mahoraga's attacks (0 disables; giants are always pushed much less).",
                    new AcceptableValueRange<float>(0.0f, 3.0f)));

            EnemyGrudgeHalfLifeSeconds = cfg.Bind(
                "EnemyInteraction",
                "EnemyGrudgeHalfLifeSeconds",
                5.0f,
                new ConfigDescription(
                    "How long an enemy that hurt Mahoraga stays a retaliation target (aggro weight halves every this many seconds).",
                    new AcceptableValueRange<float>(1.0f, 30.0f)));

            EnemyEngageMaxSeconds = cfg.Bind(
                "EnemyInteraction",
                "EnemyEngageMaxSeconds",
                20.0f,
                new ConfigDescription(
                    "Hard cap on a single fight against one enemy; afterwards Mahoraga disengages and ignores that enemy for DisengageCooldownSeconds.",
                    new AcceptableValueRange<float>(5.0f, 120.0f)));

            ImmortalEnemyEngageSeconds = cfg.Bind(
                "EnemyInteraction",
                "ImmortalEnemyEngageSeconds",
                6.0f,
                new ConfigDescription(
                    "Cap for fights against enemies that cannot die (canDie = false): Mahoraga only swats them briefly, never loops forever.",
                    new AcceptableValueRange<float>(1.0f, 60.0f)));

            DisengageCooldownSeconds = cfg.Bind(
                "EnemyInteraction",
                "DisengageCooldownSeconds",
                30.0f,
                new ConfigDescription(
                    "After giving up on an enemy (timeout, unreachable, immortal) it is ignored for this long.",
                    new AcceptableValueRange<float>(5.0f, 300.0f)));

            // [Movement]
            PatrolSpeed = cfg.Bind(
                "Movement",
                "PatrolSpeed",
                3.2f,
                new ConfigDescription(
                    "Movement speed (m/s) while patrolling.",
                    new AcceptableValueRange<float>(1.0f, 10.0f)));

            InvestigateSpeed = cfg.Bind(
                "Movement",
                "InvestigateSpeed",
                4.5f,
                new ConfigDescription(
                    "Movement speed (m/s) while investigating noises or last-known positions.",
                    new AcceptableValueRange<float>(1.0f, 12.0f)));

            ChaseSpeed = cfg.Bind(
                "Movement",
                "ChaseSpeed",
                6.2f,
                new ConfigDescription(
                    "Base chase speed (m/s) before adaptation escalation.",
                    new AcceptableValueRange<float>(2.0f, 14.0f)));

            ChaseSpeedPerAdaptation = cfg.Bind(
                "Movement",
                "ChaseSpeedPerAdaptation",
                0.35f,
                new ConfigDescription(
                    "Additional chase speed (m/s) gained per completed adaptation (capped at 4 stacks).",
                    new AcceptableValueRange<float>(0.0f, 1.5f)));

            // [Audio]
            MusicEnabled = cfg.Bind(
                "Audio",
                "MusicEnabled",
                true,
                "Enable dynamic proximity and encounter music when Mahoraga is nearby.");

            MusicMasterVolume = cfg.Bind(
                "Audio",
                "MusicMasterVolume",
                0.75f,
                new ConfigDescription(
                    "Peak volume multiplier for Mahoraga's proximity music.",
                    new AcceptableValueRange<float>(0.0f, 1.0f)));

            MusicOuterRange = cfg.Bind(
                "Audio",
                "MusicOuterRange",
                35.0f,
                new ConfigDescription(
                    "Distance (meters) at which Mahoraga's theme begins fading in.",
                    new AcceptableValueRange<float>(10.0f, 80.0f)));

            MusicInnerRange = cfg.Bind(
                "Audio",
                "MusicInnerRange",
                16.0f,
                new ConfigDescription(
                    "Distance (meters) at which Mahoraga's theme reaches full intensity.",
                    new AcceptableValueRange<float>(5.0f, 40.0f)));

            EnableWallMuffling = cfg.Bind(
                "Audio",
                "EnableWallMuffling",
                true,
                "Apply AudioLowPassFilter occlusion when facility walls block line of sight to Mahoraga.");

            UseCustomMusicOverride = cfg.Bind(
                "Audio",
                "UseCustomMusicOverride",
                false,
                "If true, loads the first .ogg, .wav, or .mp3 audio file placed in the CustomMusic/ folder beside Fede.MahoragaLC.dll instead of the bundled theme.");

            Adaptation = BuildAdaptationSettings();
            HitsPerAdaptationStage.SettingChanged += (_, __) => Adaptation = BuildAdaptationSettings();
            Stage1Resistance.SettingChanged += (_, __) => Adaptation = BuildAdaptationSettings();
            Stage2Resistance.SettingChanged += (_, __) => Adaptation = BuildAdaptationSettings();
            AdaptedDamageResistance.SettingChanged += (_, __) => Adaptation = BuildAdaptationSettings();
            AdaptationHealPercent.SettingChanged += (_, __) => Adaptation = BuildAdaptationSettings();
            OldBirdMissileFullResistance.SettingChanged += (_, __) => Adaptation = BuildAdaptationSettings();

            ClearOrphanedEntries(cfg);
        }

        private AdaptationSettings BuildAdaptationSettings()
        {
            return AdaptationSettings.CreateProgressive(
                HitsPerAdaptationStage.Value,
                Stage1Resistance.Value,
                Stage2Resistance.Value,
                AdaptedDamageResistance.Value,
                AdaptationHealPercent.Value,
                OldBirdMissileFullResistance.Value);
        }

        public static MahoragaConfig Bind(ConfigFile cfg)
        {
            Current = new MahoragaConfig(cfg);
            return Current;
        }

        public void ApplyMoonOverrides(
            Dictionary<Levels.LevelTypes, int> levelRarities,
            Dictionary<string, int> customLevelRarities)
        {
            var parsed = MoonRarityParser.Parse(MoonRarityOverrides.Value);
            foreach (var entry in parsed)
            {
                if (TryResolveVanillaLevelType(entry.Key, out var levelType))
                {
                    levelRarities[levelType] = entry.Value;
                }
                else
                {
                    customLevelRarities[entry.Key] = entry.Value;
                }
            }
        }

        private static bool TryResolveVanillaLevelType(string moonName, out Levels.LevelTypes levelType)
        {
            string normalized = moonName.Trim();
            if (Enum.TryParse(normalized, ignoreCase: true, out levelType))
            {
                return true;
            }

            if (!normalized.EndsWith("Level", StringComparison.OrdinalIgnoreCase) &&
                Enum.TryParse(normalized + "Level", ignoreCase: true, out levelType))
            {
                return true;
            }

            levelType = Levels.LevelTypes.None;
            return false;
        }

        private static void ClearOrphanedEntries(ConfigFile cfg)
        {
            try
            {
                PropertyInfo? orphanedProp = cfg.GetType().GetProperty(
                    "OrphanedEntries",
                    BindingFlags.NonPublic | BindingFlags.Instance);

                if (orphanedProp?.GetValue(cfg, null) is Dictionary<ConfigDefinition, string> orphaned && orphaned.Count > 0)
                {
                    orphaned.Clear();
                    cfg.Save();
                }
            }
            catch
            {
                // Ignore if BepInEx version changes internal OrphanedEntries implementation.
            }
        }
    }
}
