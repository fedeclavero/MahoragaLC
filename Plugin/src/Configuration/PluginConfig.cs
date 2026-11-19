using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Configuration;
using LethalLib.Modules;

namespace MahoragaLC.Config
{
    public sealed partial class MahoragaConfig
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
        public ConfigEntry<int> AdaptationThreshold { get; }
        public ConfigEntry<float> AdaptedDamageResistance { get; }
        public ConfigEntry<float> AdaptationHealPercent { get; }
        public ConfigEntry<int> CleaveDamage { get; }
        public ConfigEntry<int> SlamDamage { get; }
        public ConfigEntry<bool> CanForceLockedDoors { get; }

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

            AdaptationThreshold = cfg.Bind(
                "Combat",
                "AdaptationThreshold",
                2,
                new ConfigDescription(
                    "Number of hits or stun exposures from a category required before the Wheel of Adaptation turns.",
                    new AcceptableValueRange<int>(1, 5)));

            AdaptedDamageResistance = cfg.Bind(
                "Combat",
                "AdaptedDamageResistance",
                0.95f,
                new ConfigDescription(
                    "Damage reduction applied to already-adapted weapon categories (0.95 = 95% resistance, 1.00 = full immunity).",
                    new AcceptableValueRange<float>(0.50f, 1.00f)));

            AdaptationHealPercent = cfg.Bind(
                "Combat",
                "AdaptationHealPercent",
                0.20f,
                new ConfigDescription(
                    "Fraction of MaxHP regenerated each time the Wheel of Adaptation completes a turn.",
                    new AcceptableValueRange<float>(0.00f, 0.50f)));

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

            BindSummonAndHunt(cfg);

            ClearOrphanedEntries(cfg);
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
