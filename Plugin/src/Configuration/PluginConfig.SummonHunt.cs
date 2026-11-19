using BepInEx.Configuration;
using MahoragaLC.AI;

namespace MahoragaLC.Config
{
    /// <summary>[Summoning] and [Hunting] sections (kept in their own partial to stay separate from [Combat]).</summary>
    public sealed partial class MahoragaConfig
    {
        // [Summoning]
        public ConfigEntry<bool> EnableSummonSequence { get; private set; } = null!;
        public ConfigEntry<float> SummonDuration { get; private set; } = null!;
        public ConfigEntry<bool> SummonCameraShake { get; private set; } = null!;

        // [Hunting]
        public ConfigEntry<bool> HuntPlayers { get; private set; } = null!;
        public ConfigEntry<float> HuntRepathSeconds { get; private set; } = null!;

        private void BindSummonAndHunt(ConfigFile cfg)
        {
            EnableSummonSequence = cfg.Bind(
                "Summoning",
                "EnableSummonSequence",
                true,
                "If true, Mahoraga is summoned from a pool of black shadow (Ten Shadows style) every time it spawns: he is invulnerable and motionless while he rises, the Dharma wheel turns once and he roars. If false he appears instantly like before.");

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
                "If true, while it has no line of sight Mahoraga walks toward the closest living player in its own domain (inside or outside the facility) instead of wandering between AI nodes. If false it only patrols (old behaviour).");

            HuntRepathSeconds = cfg.Bind(
                "Hunting",
                "HuntRepathSeconds",
                2.5f,
                new ConfigDescription(
                    "How often (seconds) the hunt re-selects the closest reachable player and recomputes its destination.",
                    new AcceptableValueRange<float>(1.0f, 10.0f)));
        }
    }
}
