using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using LethalLib.Modules;
using MahoragaLC.Config;
using MahoragaLC.Patches;
using UnityEngine;

namespace MahoragaLC
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    [BepInDependency(LethalLib.Plugin.ModGUID)]
    public sealed class Plugin : BaseUnityPlugin
    {
        public const string PluginGuid = "Fede.MahoragaLC";
        public const string PluginName = "MahoragaLC";
        public const string PluginVersion = "1.0.0";
        private const string AssetBundleName = "mahoraga_assets";

        private static readonly string[] RequiredAudioClipNames =
        {
            "mahoraga_music_main",
            "mahoraga_wheel_turn",
            "mahoraga_footstep_1",
            "mahoraga_footstep_2",
            "mahoraga_slash",
            "mahoraga_impact",
            "mahoraga_roar",
            "mahoraga_death"
        };

        internal static new ManualLogSource Logger { get; private set; } = null!;
        internal static MahoragaConfig BoundConfig { get; private set; } = null!;
        internal static string PluginDirectory { get; private set; } = string.Empty;
        internal static AssetBundle? ModAssets { get; private set; }
        internal static Dictionary<string, AudioClip> CachedAudioClips { get; } = new Dictionary<string, AudioClip>(StringComparer.Ordinal);

        private Harmony? _harmony;

        private void Awake()
        {
            Logger = base.Logger;
            PluginDirectory = Path.GetDirectoryName(Info.Location) ?? string.Empty;

            // 1. NetcodePatcher weaving initialization MUST execute before registering any NetworkPrefab.
            InitializeNetworkBehaviours();

            // 2. Bind BepInEx configuration.
            BoundConfig = MahoragaConfig.Bind(Config);
            if (!BoundConfig.EnableMod.Value)
            {
                Logger.LogInfo($"{PluginGuid} is disabled via configuration (EnableMod = false).");
                return;
            }

            // 3. Load mahoraga_assets AssetBundle from beside Fede.MahoragaLC.dll.
            string bundlePath = Path.Combine(PluginDirectory, AssetBundleName);
            if (!File.Exists(bundlePath))
            {
                Logger.LogError($"Missing AssetBundle '{AssetBundleName}' at '{bundlePath}'. Mod cannot register Mahoraga.");
                return;
            }

            ModAssets = AssetBundle.LoadFromFile(bundlePath);
            if (ModAssets == null)
            {
                Logger.LogError($"Failed to load AssetBundle '{AssetBundleName}' from '{bundlePath}'.");
                return;
            }

            EnemyType? mahoragaEnemyType = ModAssets.LoadAsset<EnemyType>("MahoragaEnemyType");
            GameObject? mahoragaPrefab = ModAssets.LoadAsset<GameObject>("MahoragaPrefab");
            TerminalNode? mahoragaTerminalNode = ModAssets.LoadAsset<TerminalNode>("MahoragaTerminalNode");
            TerminalKeyword? mahoragaTerminalKeyword = ModAssets.LoadAsset<TerminalKeyword>("MahoragaTerminalKeyword");

            if (mahoragaEnemyType == null)
            {
                Logger.LogError("AssetBundle 'mahoraga_assets' is missing 'MahoragaEnemyType'.");
                return;
            }

            if (mahoragaPrefab == null)
            {
                mahoragaPrefab = mahoragaEnemyType.enemyPrefab;
            }

            if (mahoragaPrefab == null)
            {
                Logger.LogError("AssetBundle 'mahoraga_assets' is missing 'MahoragaPrefab'.");
                return;
            }

            mahoragaEnemyType.enemyPrefab = mahoragaPrefab;
            CacheBundleAudioClips(ModAssets);

            // 4. Apply runtime config values & repair AudioMixer groups (Diagetic).
            mahoragaEnemyType.PowerLevel = BoundConfig.PowerLevel.Value;
            mahoragaEnemyType.MaxCount = BoundConfig.MaxCount.Value;
            mahoragaEnemyType.isOutsideEnemy = false;
            mahoragaEnemyType.isDaytimeEnemy = false;
            Utilities.FixMixerGroups(mahoragaPrefab);

            // 5. Register NetworkPrefab with LethalLib / NGO.
            NetworkPrefabs.RegisterNetworkPrefab(mahoragaPrefab);

            // 6. Register interior enemy across ALL vanilla & modded moons using LethalLib 1.2.0 Overload 3.
            var levelRarities = new Dictionary<Levels.LevelTypes, int>
            {
                { Levels.LevelTypes.All, BoundConfig.DefaultInteriorRarity.Value },
                { Levels.LevelTypes.Modded, BoundConfig.DefaultInteriorRarity.Value }
            };
            var customLevelRarities = new Dictionary<string, int>();
            BoundConfig.ApplyMoonOverrides(levelRarities, customLevelRarities);

            Enemies.RegisterEnemy(
                mahoragaEnemyType,
                Enemies.SpawnType.Default,
                levelRarities,
                customLevelRarities,
                mahoragaTerminalNode,
                mahoragaTerminalKeyword);

            // Optional outdoor spawn profile (clone without duplicating Bestiary Terminal entries).
            if (BoundConfig.AllowOutdoorSpawn.Value)
            {
                EnemyType outdoorEnemyType = Instantiate(mahoragaEnemyType);
                outdoorEnemyType.name = "MahoragaEnemyType_Outside";
                outdoorEnemyType.isOutsideEnemy = true;
                outdoorEnemyType.enemyPrefab = mahoragaPrefab;

                var outsideRarities = new Dictionary<Levels.LevelTypes, int>
                {
                    { Levels.LevelTypes.All, BoundConfig.OutdoorRarity.Value },
                    { Levels.LevelTypes.Modded, BoundConfig.OutdoorRarity.Value }
                };

                Enemies.RegisterEnemy(
                    outdoorEnemyType,
                    Enemies.SpawnType.Outside,
                    outsideRarities,
                    customLevelRarities: null,
                    infoNode: null!,
                    infoKeyword: null!);
            }

            // 7. Apply minimal Harmony hooks for deterministic explosion & flashbang tracking.
            _harmony = new Harmony(PluginGuid);
            _harmony.PatchAll(typeof(ExplosionTrackingPatch));

            Logger.LogInfo($"{PluginGuid} v{PluginVersion} loaded! Interior rarity={BoundConfig.DefaultInteriorRarity.Value}, Outdoor={BoundConfig.AllowOutdoorSpawn.Value}, MaxHP={BoundConfig.MaxHP.Value}, HitsPerAdaptationStage={BoundConfig.HitsPerAdaptationStage.Value}.");
        }

        private static void CacheBundleAudioClips(AssetBundle bundle)
        {
            CachedAudioClips.Clear();
            for (int i = 0; i < RequiredAudioClipNames.Length; i++)
            {
                string clipName = RequiredAudioClipNames[i];
                AudioClip? clip = bundle.LoadAsset<AudioClip>(clipName);
                if (clip != null)
                {
                    CachedAudioClips[clipName] = clip;
                }
                else
                {
                    Logger.LogWarning($"Optional fallback AudioClip '{clipName}' not found directly by name in '{AssetBundleName}'.");
                }
            }
        }

        private static void InitializeNetworkBehaviours()
        {
            Type[] types;
            try
            {
                types = Assembly.GetExecutingAssembly().GetTypes();
            }
            catch (ReflectionTypeLoadException ex)
            {
                types = Array.FindAll(ex.Types, t => t != null)!;
            }

            for (int i = 0; i < types.Length; i++)
            {
                Type type = types[i];
                MethodInfo[] methods = type.GetMethods(BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);
                for (int j = 0; j < methods.Length; j++)
                {
                    MethodInfo method = methods[j];
                    if (!method.IsStatic || method.GetParameters().Length != 0)
                    {
                        continue;
                    }

                    object[] attributes = method.GetCustomAttributes(typeof(RuntimeInitializeOnLoadMethodAttribute), false);
                    if (attributes.Length > 0)
                    {
                        method.Invoke(null, null);
                    }
                }
            }
        }
    }
}
