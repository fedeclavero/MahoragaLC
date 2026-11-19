using BepInEx.Configuration;
using MahoragaLC.AI;

namespace MahoragaLC.Config
{
    /// <summary>[Summoning], [Hunting], and [ShadowAmbush] sections (kept in their own partial to stay separate from [Combat]).</summary>
    public sealed partial class MahoragaConfig
    {
        // [Summoning]
        public ConfigEntry<bool> EnableSummonSequence { get; private set; } = null!;
        public ConfigEntry<float> SummonDuration { get; private set; } = null!;
        public ConfigEntry<bool> SummonCameraShake { get; private set; } = null!;

        // [Hunting]
        public ConfigEntry<bool> HuntPlayers { get; private set; } = null!;
        public ConfigEntry<bool> HuntPlayersOutdoors { get; private set; } = null!;
        public ConfigEntry<float> HuntRepathSeconds { get; private set; } = null!;

        // [ShadowAmbush]
        public ConfigEntry<bool> EnableShadowAmbush { get; private set; } = null!;
        public ConfigEntry<float> ShadowAmbushMinTime { get; private set; } = null!;
        public ConfigEntry<float> ShadowAmbushLostSightDelay { get; private set; } = null!;
        public ConfigEntry<float> ShadowAmbushCooldown { get; private set; } = null!;
        public ConfigEntry<float> ShadowAmbushSubmergeDuration { get; private set; } = null!;

        private void BindSummonAndHunt(ConfigFile cfg)
        {
            EnableSummonSequence = cfg.Bind(
                "Summoning",
                "EnableSummonSequence",
                true,
                "If true, Mahoraga is summoned from a pool of black shadow (Ten Shadows style) every time it spawns: he is invulnerable and motionless while he rises, the Dharma wheel turns one spoke (+45 deg) and he roars. If false he appears instantly.");

            SummonDuration = cfg.Bind(
                "Summoning",
                "SummonDuration",
                SummonTimeline.ReferenceDuration,
                new ConfigDescription(
                    "Total length in seconds of the summoning sequence (shadow pool, rise, wheel turn, roar, dissolve). The animation and effects stretch to fit.",
                    new AcceptableValueRange<float>(SummonTimeline.MinDuration, 10.0f)));

            SummonCameraShake = cfg.Bind(
                "Summoning",
                "SummonCameraShake",
                true,
                "Shake the camera of nearby players during the emergence boom and the roar of the summoning.");

            HuntPlayers = cfg.Bind(
                "Hunting",
                "HuntPlayers",
                true,
                "If true, indoors while it has no line of sight Mahoraga walks toward the closest reachable living player instead of wandering between AI nodes. If false it only patrols indoors.");

            HuntPlayersOutdoors = cfg.Bind(
                "Hunting",
                "HuntPlayersOutdoors",
                false,
                "If false (default), outdoors Mahoraga wanders naturally and only chases players it genuinely sees (line of sight) or hears (noise investigation). If true, outdoors Mahoraga also hunts players without line of sight like indoors.");

            HuntRepathSeconds = cfg.Bind(
                "Hunting",
                "HuntRepathSeconds",
                2.5f,
                new ConfigDescription(
                    "How often (seconds) the hunt re-selects the closest reachable player and recomputes its destination.",
                    new AcceptableValueRange<float>(1.0f, 10.0f)));

            EnableShadowAmbush = cfg.Bind(
                "ShadowAmbush",
                "EnableShadowAmbush",
                true,
                "If true, late in the day when Mahoraga has not seen any player for a while (and no living Old Bird is in its domain), it dives into a shadow pool and re-emerges directly in front of a living player.");

            ShadowAmbushMinTime = cfg.Bind(
                "ShadowAmbush",
                "ShadowAmbushMinTime",
                0.62f,
                new ConfigDescription(
                    "Minimum normalized TimeOfDay (0.0 = 6 AM, 1.0 = midnight; 0.62 = late afternoon) before Mahoraga can trigger a Shadow Ambush.",
                    new AcceptableValueRange<float>(0.0f, 0.95f)));

            ShadowAmbushLostSightDelay = cfg.Bind(
                "ShadowAmbush",
                "ShadowAmbushLostSightDelay",
                12.0f,
                new ConfigDescription(
                    "Seconds without line of sight to any player before Mahoraga considers diving into the shadows for an ambush.",
                    new AcceptableValueRange<float>(2.0f, 120.0f)));

            ShadowAmbushCooldown = cfg.Bind(
                "ShadowAmbush",
                "ShadowAmbushCooldown",
                45.0f,
                new ConfigDescription(
                    "Minimum seconds between two Shadow Ambush dives.",
                    new AcceptableValueRange<float>(10.0f, 300.0f)));

            ShadowAmbushSubmergeDuration = cfg.Bind(
                "ShadowAmbush",
                "ShadowAmbushSubmergeDuration",
                SubmergeTimeline.ReferenceDuration,
                new ConfigDescription(
                    "Duration in seconds of the downward shadow-dive animation before warping to emerge in front of a player.",
                    new AcceptableValueRange<float>(SubmergeTimeline.MinDuration, SubmergeTimeline.MaxDuration)));
        }
    }
}
