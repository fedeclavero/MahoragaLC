using BepInEx.Configuration;
using MahoragaLC.AI;

namespace MahoragaLC.Config
{
    /// <summary>[Summoning] and [ShadowAmbush] sections (kept in their own partial to stay separate from [Combat]).</summary>
    public sealed partial class MahoragaConfig
    {
        // [Summoning]
        public ConfigEntry<bool> EnableSummonSequence { get; private set; } = null!;
        public ConfigEntry<float> SummonDuration { get; private set; } = null!;
        public ConfigEntry<bool> SummonCameraShake { get; private set; } = null!;

        // [Hunting]
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

            EnableShadowAmbush = cfg.Bind(
                "ShadowAmbush",
                "EnableShadowAmbush",
                true,
                "If true, late in the day Mahoraga can dive into a shadow pool and re-emerge at a recent reachable sound once it has lost contact and there are no Old Birds in range.");

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
                    "Seconds since contact with a player before Mahoraga considers diving into the shadows for an ambush.",
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
                    "Duration in seconds of the downward shadow-dive animation before emerging at the recorded sound.",
                    new AcceptableValueRange<float>(SubmergeTimeline.MinDuration, SubmergeTimeline.MaxDuration)));
        }
    }
}
