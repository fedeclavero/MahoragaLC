using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MahoragaLC.AI;
using MahoragaLC.Audio;
using MahoragaLC.Effects;
using Unity.Netcode;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.Rendering.HighDefinition;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Audio;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

public static class MahoragaAssetBuilder
{
    private const string BundleName = "mahoraga_assets";
    private const string RootDir = "Assets/MahoragaMod";
    private const string AudioDir = RootDir + "/Audio";
    private const string ModelDir = RootDir + "/Model";
    private const string MaterialsDir = RootDir + "/Materials";
    private const string AnimationsDir = RootDir + "/Animations";
    private const string PrefabsDir = RootDir + "/Prefabs";
    private const string DataDir = RootDir + "/Data";
    private const string EffectsDir = RootDir + "/Effects";

    private const string FbxPath = ModelDir + "/Mahoraga.fbx";
    private const string AlbedoPath = ModelDir + "/Mahoraga_Albedo.png";
    private const string NormalPath = ModelDir + "/Mahoraga_Normal.png";
    private const string MaskMapPath = ModelDir + "/Mahoraga_MaskMap.png";
    private const string MetallicPath = ModelDir + "/Mahoraga_Metallic.png";
    private const string RoughnessPath = ModelDir + "/Mahoraga_Roughness.png";
    private const string EmissivePath = ModelDir + "/Mahoraga_Emissive.png";

    private const string MahoragaMatPath = MaterialsDir + "/Mahoraga_Mat.mat";
    private const string MapDotMatPath = MaterialsDir + "/MapDot_Mat.mat";
    private const string AnimatorPath = AnimationsDir + "/MahoragaAnimator.controller";
    private const string PrefabPath = PrefabsDir + "/MahoragaPrefab.prefab";
    private const string EnemyTypePath = DataDir + "/MahoragaEnemyType.asset";
    private const string TerminalNodePath = DataDir + "/MahoragaTerminalNode.asset";
    private const string TerminalKeywordPath = DataDir + "/MahoragaTerminalKeyword.asset";
    private const string ShadowPoolMatPath = MaterialsDir + "/ShadowPool_Mat.mat";
    private const string ShadowPoolMeshPath = EffectsDir + "/ShadowPool.asset";
    private const string DiageticMixerPath = "Assets/AudioMixerController/Diagetic.mixer";

    private const string BundleOutputDir = "AssetBundles/StandaloneWindows64";

    private static readonly string[] RequiredClips =
    {
        "Idle",
        "Walk",
        "Run",
        "Attack_Cleave",
        "Attack_Slam",
        "Adapt_WheelSpin",
        "Stun",
        "Death"
    };

    // Optional clip: the animator agent adds the "Summon_Emerge" action to Mahoraga.fbx later. Until then the
    // Summon_Emerge animator state plays FallbackSummonClip (so the build never breaks).
    private const string SummonClipName = "Summon_Emerge";
    private const string FallbackSummonClip = "Adapt_WheelSpin";
    private static bool _hasRealSummonClip;

    private static readonly HashSet<string> LoopingClips = new HashSet<string>(StringComparer.Ordinal)
    {
        "Idle",
        "Walk",
        "Run",
        "Stun"
    };

    private static readonly string[] RequiredAudioNames =
    {
        "mahoraga_music_main",
        "mahoraga_wheel_turn",
        "mahoraga_footstep_1",
        "mahoraga_footstep_2",
        "mahoraga_slash",
        "mahoraga_impact",
        "mahoraga_roar",
        "mahoraga_death",
        "mahoraga_summon"
    };

    private const string BestiaryDisplayText =
@"MAHORAGA

Sigurd's danger level: 95%

Scientific name: Makora Sila-Octo
Classification: Uncontained Apex Anomaly
Observed stature: ~3.75m (1.5x standard crew height)

An extremely tall, pale humanoid entity encountered
across abandoned Company facilities on all surveyed
moons. Despite possessing no eyes -- four rigid white
wings erupt directly from its orbital sockets -- it
tracks personnel through dark corridors with terrifying
precision and reacts violently to sound.

A heavy blade is bound directly to its right forearm,
and an ornate, eight-handled golden wheel hovers
unsupported behind its skull.

ACOUSTIC WARNING:
Personnel consistently report an unmistakable low
rhythmic pulse echoing through facility bulkheads well
before visual contact. If the tone sharpens and loses
its muffled quality, Mahoraga has line of sight.

THE WHEEL OF ADAPTATION:
Do NOT rely on a single defense mechanism. Field black-
box recordings confirm that after enduring repeated
strikes or electrical/flash stuns of the same type, the
floating golden wheel turns 45 degrees with a loud
metallic chime. Upon each turn, the entity rapidly
regenerates tissue and becomes nearly impervious to
that weapon or stun category for the remainder of the
shift.

SURVIVAL ADVISORY:
- Standard locked doors will NOT stop it; it will force
  them open within seconds.
- Powered steel Blast Doors controlled from the ship
  terminal CAN delay or isolate it. Coordinate with
  your ship operator immediately.
- Evacuation is strongly recommended. If termination is
  attempted, crews must rotate diverse high-impact
  weapons (firearms, explosives, melee) and deliver a
  decisive lethal burst before the wheel completes its
  turn.

""We hit it twice with the shotgun. The thing above its
head clicked like a giant clock, lit up gold, and the
third shell did nothing. Leave the scrap. Run.""
  -- Recovered voice log, Moon 8-Titan";

    [MenuItem("Mahoraga/Build All Assets & Bundle")]
    public static void BuildAll()
    {
        try
        {
            Debug.Log("[MAHORAGA-BUILD] === Starting MahoragaAssetBuilder.BuildAll ===");
            EnsureDirectory(MaterialsDir);
            EnsureDirectory(AnimationsDir);
            EnsureDirectory(PrefabsDir);
            EnsureDirectory(DataDir);
            EnsureDirectory(EffectsDir);

            // 0. Verify HDRP shaders up-front (abort loudly if missing)
            Shader hdrpLit = Shader.Find("HDRP/Lit");
            if (hdrpLit == null)
            {
                throw new Exception("FATAL: Shader.Find(\"HDRP/Lit\") returned null! HDRP 14.0.12 is not properly active.");
            }
            Shader hdrpUnlit = Shader.Find("HDRP/Unlit");
            if (hdrpUnlit == null)
            {
                throw new Exception("FATAL: Shader.Find(\"HDRP/Unlit\") returned null! HDRP 14.0.12 is not properly active.");
            }
            Debug.Log($"[MAHORAGA-BUILD] Verified shaders: '{hdrpLit.name}' and '{hdrpUnlit.name}'.");

            // 1. Configure audio clips
            Dictionary<string, AudioClip> audioClips = ConfigureAudioClips();

            // 2. Configure 2K HDRP textures
            ConfigureTextures();

            // 3. Configure FBX model (Generic rig, exact 8 AnimationClips with proper looping)
            Dictionary<string, AnimationClip> animClips = ConfigureFbxModel();

            // 4. Build true HDRP/Lit and HDRP/Unlit materials
            Material mahoragaMat = BuildMahoragaMaterial(hdrpLit);
            Material mapDotMat = BuildMapDotMaterial(hdrpUnlit);
            Material shadowPoolMat = BuildShadowPoolMaterial(hdrpLit);
            Mesh shadowPoolMesh = BuildShadowPoolMesh();

            // 5. Build AnimatorController
            AnimatorController animatorController = BuildAnimatorController(animClips);

            // 6. Build ScriptableObjects & MahoragaPrefab
            BuildScriptableObjectsAndPrefab(audioClips, mahoragaMat, mapDotMat, shadowPoolMat, shadowPoolMesh, animatorController);

            // 7. Assign exact AssetBundle labels & build Windows x64 LZ4 bundle
            AssignBundleLabels();
            string bundlePath = BuildWindowsAssetBundle();

            // 8. Reload & verify the built AssetBundle end-to-end
            VerifyBuiltBundle(bundlePath);

            Debug.Log("[MAHORAGA-BUILD] === MahoragaAssetBuilder.BuildAll SUCCEEDED ===");
        }
        catch (Exception ex)
        {
            Debug.LogError("[MAHORAGA-BUILD] FATAL BUILD FAILURE:\n" + ex);
            EditorApplication.Exit(1);
            throw;
        }
    }

    private static void EnsureDirectory(string assetFolderPath)
    {
        if (!Directory.Exists(assetFolderPath))
        {
            Directory.CreateDirectory(assetFolderPath);
        }
    }

    private static Dictionary<string, AudioClip> ConfigureAudioClips()
    {
        var clips = new Dictionary<string, AudioClip>(StringComparer.Ordinal);
        foreach (string clipName in RequiredAudioNames)
        {
            string path = $"{AudioDir}/{clipName}.ogg";
            var importer = AssetImporter.GetAtPath(path) as AudioImporter;
            if (importer == null)
            {
                throw new Exception($"Missing audio file or AudioImporter at '{path}'.");
            }

            bool isMusic = clipName == "mahoraga_music_main";
            bool isCompressedLongSfx = clipName == "mahoraga_roar" || clipName == "mahoraga_death" || clipName == "mahoraga_summon";

            importer.forceToMono = !isMusic;
            importer.loadInBackground = false;
            importer.ambisonic = false;

            var settings = importer.defaultSampleSettings;
            settings.loadType = (isMusic || isCompressedLongSfx)
                ? AudioClipLoadType.CompressedInMemory
                : AudioClipLoadType.DecompressOnLoad;
            settings.compressionFormat = AudioCompressionFormat.Vorbis;
            settings.quality = 0.7f;
            settings.sampleRateSetting = AudioSampleRateSetting.PreserveSampleRate;
            importer.defaultSampleSettings = settings;
            importer.SaveAndReimport();

            AudioClip clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
            if (clip == null)
            {
                throw new Exception($"Failed to load AudioClip at '{path}'.");
            }
            clips[clipName] = clip;
            Debug.Log($"[MAHORAGA-BUILD] Audio '{clipName}': channels={clip.channels}, freq={clip.frequency}Hz, length={clip.length:F3}s, forceToMono={importer.forceToMono}, loadType={settings.loadType}");
        }
        return clips;
    }

    private static void ConfigureTextures()
    {
        ConfigureTexture(AlbedoPath, TextureImporterType.Default, srgb: true, TextureImporterAlphaSource.None);
        ConfigureTexture(EmissivePath, TextureImporterType.Default, srgb: true, TextureImporterAlphaSource.None);
        ConfigureTexture(NormalPath, TextureImporterType.NormalMap, srgb: false, TextureImporterAlphaSource.None);
        ConfigureTexture(MaskMapPath, TextureImporterType.Default, srgb: false, TextureImporterAlphaSource.FromInput);
        ConfigureTexture(MetallicPath, TextureImporterType.Default, srgb: false, TextureImporterAlphaSource.None);
        ConfigureTexture(RoughnessPath, TextureImporterType.Default, srgb: false, TextureImporterAlphaSource.None);
        Debug.Log("[MAHORAGA-BUILD] All 6 textures configured (Albedo/Emissive sRGB, NormalMap, MaskMap linear + InputAlpha, Metallic/Roughness linear).");
    }

    private static void ConfigureTexture(string path, TextureImporterType type, bool srgb, TextureImporterAlphaSource alphaSource)
    {
        var importer = AssetImporter.GetAtPath(path) as TextureImporter;
        if (importer == null)
        {
            throw new Exception($"TextureImporter not found at '{path}'.");
        }

        importer.textureType = type;
        importer.sRGBTexture = srgb;
        importer.alphaSource = alphaSource;
        importer.alphaIsTransparency = false;
        importer.mipmapEnabled = true;
        importer.maxTextureSize = 2048;
        importer.textureCompression = TextureImporterCompression.CompressedHQ;
        importer.wrapMode = TextureWrapMode.Repeat;
        importer.filterMode = FilterMode.Trilinear;
        importer.anisoLevel = 4;
        importer.SaveAndReimport();
    }

    private static Dictionary<string, AnimationClip> ConfigureFbxModel()
    {
        var importer = AssetImporter.GetAtPath(FbxPath) as ModelImporter;
        if (importer == null)
        {
            throw new Exception($"ModelImporter not found at '{FbxPath}'.");
        }

        importer.animationType = ModelImporterAnimationType.Generic;
        importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
        importer.importAnimation = true;
        importer.materialImportMode = ModelImporterMaterialImportMode.None;
        importer.globalScale = 1.0f;
        importer.useFileScale = true;
        importer.isReadable = true;
        importer.importNormals = ModelImporterNormals.Import;
        importer.importTangents = ModelImporterTangents.CalculateMikk;
        importer.meshCompression = ModelImporterMeshCompression.Off;
        importer.optimizeMeshPolygons = false;
        importer.optimizeMeshVertices = false;
        importer.SaveAndReimport();

        ModelImporterClipAnimation[] defaults = importer.defaultClipAnimations;
        if (defaults == null || defaults.Length == 0)
        {
            throw new Exception($"No animation clips found inside '{FbxPath}'.");
        }

        var configuredClips = new List<ModelImporterClipAnimation>();
        foreach (var def in defaults)
        {
            string cleanName = def.name;
            int pipeIdx = cleanName.LastIndexOf('|');
            if (pipeIdx >= 0 && pipeIdx < cleanName.Length - 1)
            {
                cleanName = cleanName.Substring(pipeIdx + 1);
            }

            if (!RequiredClips.Contains(cleanName) && cleanName != SummonClipName)
            {
                Debug.LogWarning($"[MAHORAGA-BUILD] Skipping unexpected clip '{def.name}' (normalized '{cleanName}').");
                continue;
            }

            bool shouldLoop = LoopingClips.Contains(cleanName);
            var clipAnim = new ModelImporterClipAnimation
            {
                name = cleanName,
                takeName = def.takeName,
                firstFrame = def.firstFrame,
                lastFrame = def.lastFrame,
                loopTime = shouldLoop,
                loopPose = shouldLoop,
                lockRootRotation = true,
                lockRootHeightY = false,
                lockRootPositionXZ = true,
                keepOriginalOrientation = true,
                keepOriginalPositionY = true,
                keepOriginalPositionXZ = true
            };
            configuredClips.Add(clipAnim);
        }

        importer.clipAnimations = configuredClips.ToArray();
        importer.SaveAndReimport();

        var loadedClips = new Dictionary<string, AnimationClip>(StringComparer.Ordinal);
        foreach (UnityEngine.Object subAsset in AssetDatabase.LoadAllAssetsAtPath(FbxPath))
        {
            if (subAsset is AnimationClip clip && !clip.name.StartsWith("__preview__", StringComparison.Ordinal))
            {
                loadedClips[clip.name] = clip;
                Debug.Log($"[MAHORAGA-BUILD] Imported AnimationClip '{clip.name}': length={clip.length:F3}s, frameRate={clip.frameRate}, loop={clip.isLooping}");
            }
        }

        foreach (string req in RequiredClips)
        {
            if (!loadedClips.ContainsKey(req))
            {
                throw new Exception($"Required AnimationClip '{req}' missing from '{FbxPath}'! Found: {string.Join(", ", loadedClips.Keys)}");
            }
            bool expectedLoop = LoopingClips.Contains(req);
            if (loadedClips[req].isLooping != expectedLoop)
            {
                throw new Exception($"AnimationClip '{req}' loopTime mismatch: expected {expectedLoop}, got {loadedClips[req].isLooping}");
            }
        }

        _hasRealSummonClip = loadedClips.ContainsKey(SummonClipName);
        if (_hasRealSummonClip)
        {
            if (loadedClips[SummonClipName].isLooping)
            {
                throw new Exception($"AnimationClip '{SummonClipName}' must not loop.");
            }
            Debug.Log($"[MAHORAGA-BUILD] Optional clip '{SummonClipName}' found in the FBX: length={loadedClips[SummonClipName].length:F3}s.");
        }
        else
        {
            Debug.LogWarning($"[MAHORAGA-BUILD] Optional clip '{SummonClipName}' is NOT in the FBX; the Summon_Emerge state falls back to '{FallbackSummonClip}'.");
        }

        return loadedClips;
    }

    private static Material BuildMahoragaMaterial(Shader hdrpLit)
    {
        Texture2D albedo = AssetDatabase.LoadAssetAtPath<Texture2D>(AlbedoPath);
        Texture2D normal = AssetDatabase.LoadAssetAtPath<Texture2D>(NormalPath);
        Texture2D maskMap = AssetDatabase.LoadAssetAtPath<Texture2D>(MaskMapPath);
        Texture2D emissive = AssetDatabase.LoadAssetAtPath<Texture2D>(EmissivePath);

        if (albedo == null || normal == null || maskMap == null || emissive == null)
        {
            throw new Exception("One or more required Mahoraga textures failed to load.");
        }

        if (AssetDatabase.LoadAssetAtPath<Material>(MahoragaMatPath) != null)
        {
            AssetDatabase.DeleteAsset(MahoragaMatPath);
        }

        var mat = new Material(hdrpLit)
        {
            name = "Mahoraga_Mat"
        };

        // Base color
        mat.SetTexture("_BaseColorMap", albedo);
        mat.SetColor("_BaseColor", Color.white);

        // Mask map (R=Metallic, G=AO, B=Detail, A=Smoothness)
        mat.SetTexture("_MaskMap", maskMap);
        mat.SetFloat("_Metallic", 1.0f);
        mat.SetFloat("_MetallicRemapMin", 0.0f);
        mat.SetFloat("_MetallicRemapMax", 1.0f);
        mat.SetFloat("_Smoothness", 1.0f);
        mat.SetFloat("_SmoothnessRemapMin", 0.0f);
        mat.SetFloat("_SmoothnessRemapMax", 1.0f);
        mat.SetFloat("_AORemapMin", 0.0f);
        mat.SetFloat("_AORemapMax", 1.0f);
        mat.EnableKeyword("_MASKMAP");

        // Normal map (tangent space, OpenGL Y+)
        mat.SetTexture("_NormalMap", normal);
        mat.SetFloat("_NormalScale", 1.0f);
        mat.SetFloat("_NormalMapSpace", 0.0f);
        mat.EnableKeyword("_NORMALMAP");
        mat.EnableKeyword("_NORMALMAP_TANGENT_SPACE");

        // Emissive map (pure black except Dharma Wheel gold + subtle blade runes)
        Color goldLdr = new Color(1.0f, 0.7216f, 0.1882f, 1.0f); // #FFB830
        const float idleNits = 0.4f;
        mat.SetTexture("_EmissiveColorMap", emissive);
        mat.SetFloat("_UseEmissiveIntensity", 1.0f);
        mat.SetFloat("_EmissiveIntensityUnit", 0.0f); // Nits
        mat.SetColor("_EmissiveColorLDR", goldLdr);
        mat.SetFloat("_EmissiveIntensity", idleNits);
        mat.SetColor("_EmissiveColor", goldLdr.linear * idleNits);
        mat.SetFloat("_EmissiveExposureWeight", 1.0f);
        mat.EnableKeyword("_EMISSIVE_COLOR_MAP");
        mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;

        // Opaque Lit surface defaults
        mat.SetFloat("_SurfaceType", 0.0f);
        mat.SetFloat("_CullMode", 2.0f);
        mat.SetFloat("_CullModeForward", 2.0f);

        HDMaterial.ValidateMaterial(mat);

        AssetDatabase.CreateAsset(mat, MahoragaMatPath);
        AssetDatabase.SaveAssets();
        Material savedMat = AssetDatabase.LoadAssetAtPath<Material>(MahoragaMatPath);
        if (savedMat == null || savedMat.shader == null || savedMat.shader.name != "HDRP/Lit")
        {
            throw new Exception("Mahoraga_Mat did not save cleanly with HDRP/Lit shader!");
        }
        Debug.Log($"[MAHORAGA-BUILD] Created Material '{savedMat.name}' with shader '{savedMat.shader.name}'.");
        return savedMat;
    }

    private static Material BuildMapDotMaterial(Shader hdrpUnlit)
    {
        if (AssetDatabase.LoadAssetAtPath<Material>(MapDotMatPath) != null)
        {
            AssetDatabase.DeleteAsset(MapDotMatPath);
        }

        var mat = new Material(hdrpUnlit)
        {
            name = "MapDot_Mat"
        };
        mat.SetColor("_UnlitColor", new Color(1.0f, 0.05f, 0.05f, 1.0f));
        mat.SetFloat("_SurfaceType", 0.0f);
        HDMaterial.ValidateMaterial(mat);

        AssetDatabase.CreateAsset(mat, MapDotMatPath);
        AssetDatabase.SaveAssets();
        Material savedMat = AssetDatabase.LoadAssetAtPath<Material>(MapDotMatPath);
        if (savedMat == null || savedMat.shader == null || savedMat.shader.name != "HDRP/Unlit")
        {
            throw new Exception("MapDot_Mat did not save cleanly with HDRP/Unlit shader!");
        }
        return savedMat;
    }

    private static AnimatorController BuildAnimatorController(Dictionary<string, AnimationClip> clips)
    {
        if (AssetDatabase.LoadAssetAtPath<AnimatorController>(AnimatorPath) != null)
        {
            AssetDatabase.DeleteAsset(AnimatorPath);
        }

        AnimatorController controller = AnimatorController.CreateAnimatorControllerAtPath(AnimatorPath);
        controller.AddParameter("Speed", AnimatorControllerParameterType.Float);
        controller.AddParameter("IsChasing", AnimatorControllerParameterType.Bool);
        controller.AddParameter("IsStunned", AnimatorControllerParameterType.Bool);
        controller.AddParameter("IsDead", AnimatorControllerParameterType.Bool);
        controller.AddParameter("AttackCleave", AnimatorControllerParameterType.Trigger);
        controller.AddParameter("AttackSlam", AnimatorControllerParameterType.Trigger);
        controller.AddParameter("Adapt", AnimatorControllerParameterType.Trigger);
        controller.AddParameter("Summon", AnimatorControllerParameterType.Trigger);
        controller.AddParameter(new AnimatorControllerParameter
        {
            name = "SummonSpeed",
            type = AnimatorControllerParameterType.Float,
            defaultFloat = 1.0f
        });

        AnimatorStateMachine sm = controller.layers[0].stateMachine;

        AnimatorState idleState = sm.AddState("Idle", new Vector3(250, 0, 0));
        idleState.motion = clips["Idle"];
        sm.defaultState = idleState;

        AnimatorState walkState = sm.AddState("Walk", new Vector3(500, -80, 0));
        walkState.motion = clips["Walk"];

        AnimatorState runState = sm.AddState("Run", new Vector3(500, 80, 0));
        runState.motion = clips["Run"];

        AnimatorState attackCleaveState = sm.AddState("Attack_Cleave", new Vector3(250, 200, 0));
        attackCleaveState.motion = clips["Attack_Cleave"];

        AnimatorState attackSlamState = sm.AddState("Attack_Slam", new Vector3(250, 280, 0));
        attackSlamState.motion = clips["Attack_Slam"];

        AnimatorState adaptState = sm.AddState("Adapt_WheelSpin", new Vector3(250, -200, 0));
        adaptState.motion = clips["Adapt_WheelSpin"];

        AnimatorState stunState = sm.AddState("Stun", new Vector3(250, -120, 0));
        stunState.motion = clips["Stun"];

        AnimatorState deathState = sm.AddState("Death", new Vector3(250, -280, 0));
        deathState.motion = clips["Death"];

        // Summon_Emerge: real clip when the FBX has it, otherwise the wheel-spin pose as a stand-in.
        AnimatorState summonState = sm.AddState("Summon_Emerge", new Vector3(500, -200, 0));
        summonState.motion = clips.TryGetValue(SummonClipName, out AnimationClip summonClip) ? summonClip : clips[FallbackSummonClip];
        summonState.speedParameterActive = true;
        summonState.speedParameter = "SummonSpeed";

        // Locomotion transitions (ASSET_CONTRACT.md §4.3)
        // Idle -> Walk: Speed > 0.25 && IsChasing == false (0.15s)
        var tIdleToWalk = idleState.AddTransition(walkState);
        ConfigureImmediateTransition(tIdleToWalk, 0.15f);
        tIdleToWalk.AddCondition(AnimatorConditionMode.Greater, 0.25f, "Speed");
        tIdleToWalk.AddCondition(AnimatorConditionMode.IfNot, 0f, "IsChasing");

        // Walk -> Idle: Speed <= 0.20 (0.15s)
        var tWalkToIdle = walkState.AddTransition(idleState);
        ConfigureImmediateTransition(tWalkToIdle, 0.15f);
        tWalkToIdle.AddCondition(AnimatorConditionMode.Less, 0.201f, "Speed");

        // Idle -> Run: (IsChasing && Speed > 0.40) OR (Speed >= 4.8) (0.12s)
        var tIdleToRun1 = idleState.AddTransition(runState);
        ConfigureImmediateTransition(tIdleToRun1, 0.12f);
        tIdleToRun1.AddCondition(AnimatorConditionMode.If, 0f, "IsChasing");
        tIdleToRun1.AddCondition(AnimatorConditionMode.Greater, 0.40f, "Speed");

        var tIdleToRun2 = idleState.AddTransition(runState);
        ConfigureImmediateTransition(tIdleToRun2, 0.12f);
        tIdleToRun2.AddCondition(AnimatorConditionMode.Greater, 4.799f, "Speed");

        // Walk -> Run: (IsChasing && Speed > 0.40) OR (Speed >= 4.8) (0.12s)
        var tWalkToRun1 = walkState.AddTransition(runState);
        ConfigureImmediateTransition(tWalkToRun1, 0.12f);
        tWalkToRun1.AddCondition(AnimatorConditionMode.If, 0f, "IsChasing");
        tWalkToRun1.AddCondition(AnimatorConditionMode.Greater, 0.40f, "Speed");

        var tWalkToRun2 = walkState.AddTransition(runState);
        ConfigureImmediateTransition(tWalkToRun2, 0.12f);
        tWalkToRun2.AddCondition(AnimatorConditionMode.Greater, 4.799f, "Speed");

        // Run -> Walk: IsChasing == false && Speed > 0.25 (0.15s)
        var tRunToWalk = runState.AddTransition(walkState);
        ConfigureImmediateTransition(tRunToWalk, 0.15f);
        tRunToWalk.AddCondition(AnimatorConditionMode.IfNot, 0f, "IsChasing");
        tRunToWalk.AddCondition(AnimatorConditionMode.Greater, 0.25f, "Speed");

        // Run -> Idle: Speed <= 0.20 (0.15s)
        var tRunToIdle = runState.AddTransition(idleState);
        ConfigureImmediateTransition(tRunToIdle, 0.15f);
        tRunToIdle.AddCondition(AnimatorConditionMode.Less, 0.201f, "Speed");

        // Any State transitions (strict priority order, canTransitionToSelf = false)
        // 1. Any State -> Death: IsDead == true (0.10s, terminal)
        var tAnyToDeath = sm.AddAnyStateTransition(deathState);
        ConfigureAnyStateTransition(tAnyToDeath, 0.10f);
        tAnyToDeath.AddCondition(AnimatorConditionMode.If, 0f, "IsDead");

        // 2. Any State -> Adapt_WheelSpin: Adapt trigger && !IsDead (0.10s; exit 0.95 -> Idle)
        var tAnyToAdapt = sm.AddAnyStateTransition(adaptState);
        ConfigureAnyStateTransition(tAnyToAdapt, 0.10f);
        tAnyToAdapt.AddCondition(AnimatorConditionMode.If, 0f, "Adapt");
        tAnyToAdapt.AddCondition(AnimatorConditionMode.IfNot, 0f, "IsDead");

        var tAdaptToIdle = adaptState.AddTransition(idleState);
        ConfigureExitTimeTransition(tAdaptToIdle, 0.95f, 0.10f);

        // 2b. Any State -> Summon_Emerge: Summon trigger && !IsDead (0.10s; exit 0.95 -> Idle). Speed = SummonSpeed.
        var tAnyToSummon = sm.AddAnyStateTransition(summonState);
        ConfigureAnyStateTransition(tAnyToSummon, 0.10f);
        tAnyToSummon.AddCondition(AnimatorConditionMode.If, 0f, "Summon");
        tAnyToSummon.AddCondition(AnimatorConditionMode.IfNot, 0f, "IsDead");

        var tSummonToIdle = summonState.AddTransition(idleState);
        ConfigureExitTimeTransition(tSummonToIdle, 0.95f, 0.10f);

        // 3. Any State -> Stun: IsStunned && !IsDead (0.10s; exit to Idle when !IsStunned)
        var tAnyToStun = sm.AddAnyStateTransition(stunState);
        ConfigureAnyStateTransition(tAnyToStun, 0.10f);
        tAnyToStun.AddCondition(AnimatorConditionMode.If, 0f, "IsStunned");
        tAnyToStun.AddCondition(AnimatorConditionMode.IfNot, 0f, "IsDead");

        var tStunToIdle = stunState.AddTransition(idleState);
        ConfigureImmediateTransition(tStunToIdle, 0.12f);
        tStunToIdle.AddCondition(AnimatorConditionMode.IfNot, 0f, "IsStunned");

        // 4. Any State -> Attack_Cleave: AttackCleave && !IsDead && !IsStunned (0.08s; exit 0.95 -> Idle)
        var tAnyToCleave = sm.AddAnyStateTransition(attackCleaveState);
        ConfigureAnyStateTransition(tAnyToCleave, 0.08f);
        tAnyToCleave.AddCondition(AnimatorConditionMode.If, 0f, "AttackCleave");
        tAnyToCleave.AddCondition(AnimatorConditionMode.IfNot, 0f, "IsDead");
        tAnyToCleave.AddCondition(AnimatorConditionMode.IfNot, 0f, "IsStunned");

        var tCleaveToIdle = attackCleaveState.AddTransition(idleState);
        ConfigureExitTimeTransition(tCleaveToIdle, 0.95f, 0.10f);

        // 5. Any State -> Attack_Slam: AttackSlam && !IsDead && !IsStunned (0.08s; exit 0.95 -> Idle)
        var tAnyToSlam = sm.AddAnyStateTransition(attackSlamState);
        ConfigureAnyStateTransition(tAnyToSlam, 0.08f);
        tAnyToSlam.AddCondition(AnimatorConditionMode.If, 0f, "AttackSlam");
        tAnyToSlam.AddCondition(AnimatorConditionMode.IfNot, 0f, "IsDead");
        tAnyToSlam.AddCondition(AnimatorConditionMode.IfNot, 0f, "IsStunned");

        var tSlamToIdle = attackSlamState.AddTransition(idleState);
        ConfigureExitTimeTransition(tSlamToIdle, 0.95f, 0.10f);

        EditorUtility.SetDirty(controller);
        AssetDatabase.SaveAssets();
        Debug.Log("[MAHORAGA-BUILD] Created AnimatorController 'MahoragaAnimator' with 9 parameters and 9 states (Summon_Emerge uses " + (_hasRealSummonClip ? SummonClipName : FallbackSummonClip + " as fallback") + ").");
        return controller;
    }

    private static void ConfigureImmediateTransition(AnimatorStateTransition t, float duration)
    {
        t.hasExitTime = false;
        t.hasFixedDuration = true;
        t.duration = duration;
        t.offset = 0f;
    }

    private static void ConfigureAnyStateTransition(AnimatorStateTransition t, float duration)
    {
        t.hasExitTime = false;
        t.hasFixedDuration = true;
        t.duration = duration;
        t.offset = 0f;
        t.canTransitionToSelf = false;
    }

    private static void ConfigureExitTimeTransition(AnimatorStateTransition t, float exitTime, float duration)
    {
        t.hasExitTime = true;
        t.exitTime = exitTime;
        t.hasFixedDuration = true;
        t.duration = duration;
        t.offset = 0f;
    }

    private static void BuildScriptableObjectsAndPrefab(
        Dictionary<string, AudioClip> audioClips,
        Material mahoragaMat,
        Material mapDotMat,
        Material shadowPoolMat,
        Mesh shadowPoolMesh,
        AnimatorController animatorController)
    {
        // Delete existing assets so rebuilds are 100% deterministic
        DeleteIfExists(EnemyTypePath);
        DeleteIfExists(TerminalNodePath);
        DeleteIfExists(TerminalKeywordPath);
        DeleteIfExists(PrefabPath);

        // 1. TerminalNode (TERMINAL_BESTIARY.md §1.2 & §2 — explicit -1 defaults for v73!)
        var terminalNode = ScriptableObject.CreateInstance<TerminalNode>();
        terminalNode.name = "MahoragaTerminalNode";
        terminalNode.creatureName = "Mahoraga";
        terminalNode.creatureFileID = -1;
        terminalNode.clearPreviousText = true;
        terminalNode.maxCharactersToType = 35;
        terminalNode.displayText = BestiaryDisplayText;
        terminalNode.terminalEvent = "";
        terminalNode.buyItemIndex = -1;
        terminalNode.buyVehicleIndex = -1;
        terminalNode.buyRerouteToMoon = -1;
        terminalNode.displayPlanetInfo = -1;
        terminalNode.shipUnlockableID = -1;
        terminalNode.storyLogFileID = -1;
        terminalNode.playSyncedClip = -1;
        AssetDatabase.CreateAsset(terminalNode, TerminalNodePath);

        // 2. TerminalKeyword (TERMINAL_BESTIARY.md §1.1)
        var terminalKeyword = ScriptableObject.CreateInstance<TerminalKeyword>();
        terminalKeyword.name = "MahoragaTerminalKeyword";
        terminalKeyword.word = "mahoraga";
        terminalKeyword.isVerb = false;
        terminalKeyword.specialKeywordResult = terminalNode;
        terminalKeyword.accessTerminalObjects = false;
        AssetDatabase.CreateAsset(terminalKeyword, TerminalKeywordPath);

        // 3. EnemyType (ASSET_CONTRACT.md §8 + #15 fixes: hitBodySFX/hitEnemyVoiceSFX/deathSFX = null)
        var enemyType = ScriptableObject.CreateInstance<EnemyType>();
        enemyType.name = "MahoragaEnemyType";
        enemyType.enemyName = "Mahoraga";
        enemyType.PowerLevel = 3f;
        enemyType.DiversityPowerLevel = 0;
        enemyType.MaxCount = 1;
        enemyType.isOutsideEnemy = false;
        enemyType.isDaytimeEnemy = false;
        enemyType.increasedChanceInterior = -1;
        enemyType.normalizedTimeInDayToLeave = 1f;
        enemyType.canDie = true;
        enemyType.canBeDestroyed = false;
        enemyType.destroyOnDeath = false;
        enemyType.canBeStunned = true;
        enemyType.canSeeThroughFog = true;
        enemyType.disableAnimatorWhenFar = false;
        enemyType.stunTimeMultiplier = 1.0f;
        enemyType.doorSpeedMultiplier = 1.5f;
        enemyType.stunGameDifficultyMultiplier = 1.0f;
        enemyType.probabilityCurve = AnimationCurve.Constant(0f, 1f, 1f);
        enemyType.numberSpawnedFalloff = AnimationCurve.Linear(0f, 1f, 1f, 0f);
        enemyType.useNumberSpawnedFalloff = true;
        enemyType.spawningDisabled = false;
        enemyType.pushPlayerForce = 0.25f;
        enemyType.pushPlayerDistance = 1.40f;
        enemyType.SizeLimit = NavSizeLimit.NoLimit;
        enemyType.EnemySize = EnemySize.Giant;
        enemyType.WaterType = EnemyWaterType.LandOnly;
        enemyType.timeToPlayAudio = 15f;
        enemyType.loudnessMultiplier = 1f;
        enemyType.nestSpawnPrefabWidth = 2f;
        enemyType.nestDistanceFromShip = -1f;
        // Per #14/#15 QA fix: null on EnemyType so MahoragaAudioController owns all SFX cleanly without double-play or null-clip warnings.
        enemyType.hitBodySFX = null;
        enemyType.hitEnemyVoiceSFX = null;
        enemyType.deathSFX = null;
        enemyType.stunSFX = null;
        enemyType.overrideVentSFX = null;
        enemyType.miscAnimations = Array.Empty<MiscAnimation>();
        enemyType.audioClips = Array.Empty<AudioClip>();
        AssetDatabase.CreateAsset(enemyType, EnemyTypePath);

        // Load Diagetic AudioMixer -> SFX group
        AudioMixer diageticMixer = AssetDatabase.LoadAssetAtPath<AudioMixer>(DiageticMixerPath);
        if (diageticMixer == null)
        {
            throw new Exception($"Diagetic AudioMixer not found at '{DiageticMixerPath}'.");
        }
        AudioMixerGroup[] sfxGroups = diageticMixer.FindMatchingGroups("SFX");
        if (sfxGroups == null || sfxGroups.Length == 0)
        {
            throw new Exception("AudioMixerGroup 'SFX' not found inside Diagetic.mixer.");
        }
        AudioMixerGroup diageticSfxGroup = sfxGroups[0];

        // 4. Construct MahoragaPrefab hierarchy (ASSET_CONTRACT.md §7.2)
        GameObject root = new GameObject("MahoragaPrefab");
        try
        {
            root.layer = 19; // Enemies
            root.tag = "Enemy";

            var netObj = root.AddComponent<NetworkObject>();
            netObj.DontDestroyWithOwner = true;
            netObj.SynchronizeTransform = true;
            netObj.ActiveSceneSynchronization = false;
            netObj.SceneMigrationSynchronization = true;
            netObj.SpawnWithObservers = true;
            netObj.AutoObjectParentSync = true;

            var navAgent = root.AddComponent<NavMeshAgent>();
            navAgent.agentTypeID = 0; // Humanoid
            navAgent.radius = 0.60f;
            navAgent.height = 3.30f;
            navAgent.baseOffset = 0.0f;
            navAgent.speed = 3.2f;
            navAgent.angularSpeed = 360f;
            navAgent.acceleration = 45f;
            navAgent.stoppingDistance = 0.2f;
            navAgent.autoBraking = true;
            navAgent.autoRepath = true;
            navAgent.autoTraverseOffMeshLink = true;
            navAgent.obstacleAvoidanceType = ObstacleAvoidanceType.LowQualityObstacleAvoidance;
            navAgent.avoidancePriority = 50;

            var ai = root.AddComponent<MahoragaAI>();
            var audioCtrl = root.AddComponent<MahoragaAudioController>();

            // SummonOffset (pivot moved ONLY in localPosition by the summoning; never scaled) -> ModelContainer -> Mahoraga (from FBX)
            GameObject summonOffset = new GameObject("SummonOffset");
            summonOffset.transform.SetParent(root.transform, false);
            summonOffset.transform.localPosition = Vector3.zero;
            summonOffset.transform.localRotation = Quaternion.identity;
            summonOffset.transform.localScale = Vector3.one;

            GameObject modelContainer = new GameObject("ModelContainer");
            modelContainer.transform.SetParent(summonOffset.transform, false);
            modelContainer.transform.localPosition = Vector3.zero;
            modelContainer.transform.localRotation = Quaternion.identity;
            modelContainer.transform.localScale = Vector3.one;

            GameObject fbxAsset = AssetDatabase.LoadAssetAtPath<GameObject>(FbxPath);
            if (fbxAsset == null)
            {
                throw new Exception($"Failed to load FBX GameObject at '{FbxPath}'.");
            }
            GameObject modelInst = (GameObject)PrefabUtility.InstantiatePrefab(fbxAsset);
            modelInst.name = "Mahoraga";
            modelInst.transform.SetParent(modelContainer.transform, false);
            modelInst.transform.localPosition = Vector3.zero;
            modelInst.transform.localRotation = Quaternion.identity;
            modelInst.transform.localScale = Vector3.one;

            Animator animator = modelInst.GetComponent<Animator>();
            if (animator == null)
            {
                animator = modelInst.AddComponent<Animator>();
            }
            animator.runtimeAnimatorController = animatorController;
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

            SkinnedMeshRenderer[] smrs = modelInst.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            if (smrs.Length == 0)
            {
                throw new Exception("No SkinnedMeshRenderer found inside Mahoraga.fbx!");
            }
            foreach (var smr in smrs)
            {
                smr.sharedMaterial = mahoragaMat;
                smr.updateWhenOffscreen = true;
                smr.shadowCastingMode = ShadowCastingMode.On;
                smr.receiveShadows = true;
            }
            SkinnedMeshRenderer mainSmr = smrs[0];

            // Locate WheelBone & SwordBone
            Transform wheelBone = FindDescendant(modelInst.transform, "WheelBone");
            Transform swordBone = FindDescendant(modelInst.transform, "SwordBone");
            if (wheelBone == null)
            {
                throw new Exception("Required bone 'WheelBone' not found inside Mahoraga.fbx!");
            }
            if (swordBone == null)
            {
                throw new Exception("Required bone 'SwordBone' not found inside Mahoraga.fbx!");
            }

            // WheelGlowLight child under WheelBone
            GameObject wheelLightGo = new GameObject("WheelGlowLight");
            wheelLightGo.transform.SetParent(wheelBone, false);
            wheelLightGo.transform.localPosition = Vector3.zero;
            wheelLightGo.transform.localRotation = Quaternion.identity;
            Light wheelLight = wheelLightGo.AddComponent<Light>();
            wheelLight.type = LightType.Point;
            wheelLight.color = new Color(1.0f, 0.722f, 0.188f, 1.0f);
            wheelLight.range = 6.5f;
            wheelLight.intensity = 0f;
            wheelLight.shadows = LightShadows.None;
            wheelLight.enabled = false;
            HDAdditionalLightData hdLight = wheelLightGo.AddComponent<HDAdditionalLightData>();
            hdLight.lightUnit = LightUnit.Lumen;
            hdLight.intensity = 0f;

            // SummonEffect: glossy black shadow pool + black droplets (opaque HDRP/Lit, see MahoragaSummonEffect)
            BuildSummonEffect(root.transform, shadowPoolMat, shadowPoolMesh);

            // EyeTransform
            GameObject eyeGo = new GameObject("EyeTransform");
            eyeGo.transform.SetParent(root.transform, false);
            eyeGo.transform.localPosition = new Vector3(0f, 3.30f, 0.35f);
            eyeGo.transform.localRotation = Quaternion.identity;

            // CollisionCollider [Layer 19 (Enemies), Tag "Enemy"]
            GameObject collisionGo = new GameObject("CollisionCollider");
            collisionGo.transform.SetParent(root.transform, false);
            collisionGo.transform.localPosition = Vector3.zero;
            collisionGo.layer = 19; // Enemies
            collisionGo.tag = "Enemy";

            BoxCollider hitBox = collisionGo.AddComponent<BoxCollider>();
            hitBox.isTrigger = true;
            hitBox.center = new Vector3(0f, 1.80f, 0f);
            hitBox.size = new Vector3(1.10f, 3.60f, 1.00f);

            Rigidbody rb = collisionGo.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            rb.useGravity = false;

            EnemyAICollisionDetect collisionDetect = collisionGo.AddComponent<EnemyAICollisionDetect>();
            collisionDetect.mainScript = ai;
            collisionDetect.canCollideWithEnemies = false;
            collisionDetect.onlyCollideWhenGrounded = false;
            collisionDetect.alwaysAllowHitting = false;

            // ScanNode [Layer 22 (ScanNode), isTrigger = false, nodeType = 1]
            GameObject scanNodeGo = new GameObject("ScanNode");
            scanNodeGo.transform.SetParent(root.transform, false);
            scanNodeGo.transform.localPosition = Vector3.zero;
            scanNodeGo.layer = 22; // ScanNode
            scanNodeGo.tag = "Untagged";

            BoxCollider scanBox = scanNodeGo.AddComponent<BoxCollider>();
            scanBox.isTrigger = false;
            scanBox.center = new Vector3(0f, 2.20f, 0f);
            scanBox.size = new Vector3(1.00f, 1.80f, 1.00f);

            ScanNodeProperties scanProps = scanNodeGo.AddComponent<ScanNodeProperties>();
            scanProps.headerText = "Mahoraga";
            scanProps.subText = "Divine General";
            scanProps.nodeType = 1; // Red danger node
            scanProps.minRange = 2;
            scanProps.maxRange = 18;
            scanProps.requiresLineOfSight = true;
            scanProps.creatureScanID = -1;
            scanProps.scrapValue = 0;

            // MapDot [Layer 14 (MapRadar)]
            GameObject mapDotGo = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            mapDotGo.name = "MapDot";
            UnityEngine.Object.DestroyImmediate(mapDotGo.GetComponent<Collider>());
            mapDotGo.transform.SetParent(root.transform, false);
            mapDotGo.transform.localPosition = new Vector3(0f, 0.50f, 0f);
            mapDotGo.transform.localRotation = Quaternion.identity;
            mapDotGo.transform.localScale = new Vector3(1.30f, 0.50f, 1.30f);
            mapDotGo.layer = 14; // MapRadar
            mapDotGo.tag = "Untagged";
            MeshRenderer mapDotRenderer = mapDotGo.GetComponent<MeshRenderer>();
            mapDotRenderer.sharedMaterial = mapDotMat;
            mapDotRenderer.shadowCastingMode = ShadowCastingMode.Off;
            mapDotRenderer.receiveShadows = false;

            // VoiceSFXSource + AudioLowPassFilter + OccludeAudio
            GameObject voiceGo = new GameObject("VoiceSFXSource");
            voiceGo.transform.SetParent(root.transform, false);
            voiceGo.transform.localPosition = new Vector3(0f, 3.10f, 0.25f);
            AudioSource voiceSrc = Configure3DAudioSource(voiceGo, minDist: 4.0f, maxDist: 45.0f, diageticSfxGroup);

            // BodySFXSource + AudioLowPassFilter + OccludeAudio
            GameObject bodyGo = new GameObject("BodySFXSource");
            bodyGo.transform.SetParent(root.transform, false);
            bodyGo.transform.localPosition = new Vector3(0f, 1.50f, 0.15f);
            AudioSource bodySrc = Configure3DAudioSource(bodyGo, minDist: 2.5f, maxDist: 30.0f, diageticSfxGroup);

            // Wire inherited EnemyAI fields (ASSET_CONTRACT.md §7.3 + #15 fixes)
            ai.enemyType = enemyType;
            ai.skinnedMeshRenderers = smrs;
            ai.meshRenderers = Array.Empty<MeshRenderer>();
            ai.creatureAnimator = animator;
            ai.creatureVoice = voiceSrc;
            ai.creatureSFX = bodySrc;
            ai.eye = eyeGo.transform;
            ai.dieSFX = null; // Per #15: MahoragaAudioController.OnMahoragaDied() solely owns mahoraga_death
            ai.agent = navAgent;
            ai.thisNetworkObject = netObj;
            ai.enemyHP = 18;
            ai.AIIntervalTime = 0.2f;
            ai.syncMovementSpeed = 0.22f;
            ai.updatePositionThreshold = 1.0f;
            ai.moveTowardsDestination = true;
            ai.exitVentAnimationTime = 1.0f;
            ai.currentOwnershipOnThisClient = -1;
            ai.openDoorSpeedMultiplier = 1.5f;
            ai.enemyBehaviourStates = new EnemyBehaviourState[]
            {
                new EnemyBehaviourState { name = "Patrolling" },
                new EnemyBehaviourState { name = "Investigating" },
                new EnemyBehaviourState { name = "Chasing" },
                new EnemyBehaviourState { name = "Attacking" },
                new EnemyBehaviourState { name = "Adapting" },
                new EnemyBehaviourState { name = "Summoning" },
                new EnemyBehaviourState { name = "EngagingEnemy" }
            };

            // Wire custom MahoragaAI fields
            ai.wheelBone = wheelBone;
            ai.swordBone = swordBone;
            ai.wheelGlowLight = wheelLight;
            ai.audioController = audioCtrl;
            ai.mainRenderer = mainSmr;

            // Wire MahoragaAudioController fields
            audioCtrl.voiceSource = voiceSrc;
            audioCtrl.bodySource = bodySrc;
            audioCtrl.musicMainClip = audioClips["mahoraga_music_main"];
            audioCtrl.wheelTurnClip = audioClips["mahoraga_wheel_turn"];
            audioCtrl.footstep1Clip = audioClips["mahoraga_footstep_1"];
            audioCtrl.footstep2Clip = audioClips["mahoraga_footstep_2"];
            audioCtrl.slashClip = audioClips["mahoraga_slash"];
            audioCtrl.impactClip = audioClips["mahoraga_impact"];
            audioCtrl.roarClip = audioClips["mahoraga_roar"];
            audioCtrl.deathClip = audioClips["mahoraga_death"];
            audioCtrl.summonClip = audioClips["mahoraga_summon"];

            // Save prefab asset
            GameObject savedPrefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath, out bool saveSuccess);
            if (!saveSuccess || savedPrefab == null)
            {
                throw new Exception($"Failed to save MahoragaPrefab at '{PrefabPath}'.");
            }

            // Ensure NetworkObject has a non-zero GlobalObjectIdHash on the saved prefab!
            NetworkObject savedNetObj = savedPrefab.GetComponent<NetworkObject>();
            var netSo = new SerializedObject(savedNetObj);
            var hashProp = netSo.FindProperty("GlobalObjectIdHash");
            if (hashProp != null && hashProp.longValue == 0)
            {
                // Deterministic non-zero uint hash for MahoragaPrefab
                hashProp.longValue = 2847193651u;
                netSo.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(savedPrefab);
                PrefabUtility.SavePrefabAsset(savedPrefab);
                Debug.Log("[MAHORAGA-BUILD] Assigned deterministic GlobalObjectIdHash=2847193651 to MahoragaPrefab NetworkObject.");
            }
            else if (hashProp != null)
            {
                Debug.Log($"[MAHORAGA-BUILD] MahoragaPrefab NetworkObject GlobalObjectIdHash={hashProp.longValue}.");
            }

            // Wire EnemyType.enemyPrefab to the saved prefab asset
            enemyType.enemyPrefab = savedPrefab;
            EditorUtility.SetDirty(enemyType);
            EditorUtility.SetDirty(terminalNode);
            EditorUtility.SetDirty(terminalKeyword);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[MAHORAGA-BUILD] Created MahoragaPrefab, MahoragaEnemyType, MahoragaTerminalNode, MahoragaTerminalKeyword.");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(root);
        }
    }

    private static Material BuildShadowPoolMaterial(Shader hdrpLit)
    {
        if (AssetDatabase.LoadAssetAtPath<Material>(ShadowPoolMatPath) != null)
        {
            AssetDatabase.DeleteAsset(ShadowPoolMatPath);
        }

        // Opaque, near-black, very glossy HDRP/Lit: reads as wet black ink under the facility lights and uses the
        // same shader variants as Mahoraga_Mat (no decals, no transparent variants).
        var mat = new Material(hdrpLit)
        {
            name = "ShadowPool_Mat"
        };
        mat.SetColor("_BaseColor", new Color(0.012f, 0.012f, 0.016f, 1.0f));
        mat.SetFloat("_Metallic", 0.0f);
        mat.SetFloat("_Smoothness", 0.97f);
        mat.SetFloat("_SurfaceType", 0.0f);
        mat.SetFloat("_CullMode", 2.0f);
        mat.SetFloat("_CullModeForward", 2.0f);
        HDMaterial.ValidateMaterial(mat);

        AssetDatabase.CreateAsset(mat, ShadowPoolMatPath);
        AssetDatabase.SaveAssets();
        Material saved = AssetDatabase.LoadAssetAtPath<Material>(ShadowPoolMatPath);
        if (saved == null || saved.shader == null || saved.shader.name != "HDRP/Lit")
        {
            throw new Exception("ShadowPool_Mat did not save cleanly with HDRP/Lit shader!");
        }
        return saved;
    }

    /// <summary>
    /// Unit-radius irregular disc (organic edge) with a slightly raised glossy lip, ~1-2 cm tall, facing +Y.
    /// Scaled at runtime by MahoragaSummonEffect.
    /// </summary>
    private static Mesh BuildShadowPoolMesh()
    {
        const int segments = 72;
        float[] ringRadius = { 0f, 0.62f, 0.86f, 0.95f, 1.0f };
        float[] ringHeight = { 0.010f, 0.012f, 0.016f, 0.018f, 0.000f };

        var verts = new List<Vector3>();
        var uvs = new List<Vector2>();
        verts.Add(new Vector3(0f, ringHeight[0], 0f));
        uvs.Add(new Vector2(0.5f, 0.5f));

        for (int ring = 1; ring < ringRadius.Length; ring++)
        {
            for (int i = 0; i < segments; i++)
            {
                float theta = 2f * Mathf.PI * i / segments;
                // Organic, closed (periodic) edge irregularity.
                float irregular = 1f
                    + 0.10f * Mathf.Sin(3f * theta + 1.3f)
                    + 0.06f * Mathf.Sin(5f * theta + 0.4f)
                    + 0.04f * Mathf.Sin(9f * theta + 2.1f)
                    + 0.03f * Mathf.Sin(13f * theta);
                float r = ringRadius[ring] * irregular * 0.9f; // 0.9 keeps the maximum extent near 1
                var v = new Vector3(r * Mathf.Sin(theta), ringHeight[ring], r * Mathf.Cos(theta));
                verts.Add(v);
                uvs.Add(new Vector2(v.x * 0.5f + 0.5f, v.z * 0.5f + 0.5f));
            }
        }

        var tris = new List<int>();
        // Center fan (clockwise seen from above => front face up)
        for (int i = 0; i < segments; i++)
        {
            int a = 1 + i;
            int b = 1 + (i + 1) % segments;
            tris.Add(0);
            tris.Add(a);
            tris.Add(b);
        }

        // Ring bands
        for (int ring = 1; ring < ringRadius.Length - 1; ring++)
        {
            int inner = 1 + (ring - 1) * segments;
            int outer = 1 + ring * segments;
            for (int i = 0; i < segments; i++)
            {
                int i1 = (i + 1) % segments;
                tris.Add(inner + i);
                tris.Add(outer + i);
                tris.Add(outer + i1);

                tris.Add(inner + i);
                tris.Add(outer + i1);
                tris.Add(inner + i1);
            }
        }

        var mesh = new Mesh { name = "ShadowPool" };
        mesh.SetVertices(verts);
        mesh.SetUVs(0, uvs);
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateTangents();
        mesh.RecalculateBounds();

        if (AssetDatabase.LoadAssetAtPath<Mesh>(ShadowPoolMeshPath) != null)
        {
            AssetDatabase.DeleteAsset(ShadowPoolMeshPath);
        }
        AssetDatabase.CreateAsset(mesh, ShadowPoolMeshPath);
        AssetDatabase.SaveAssets();
        Mesh saved = AssetDatabase.LoadAssetAtPath<Mesh>(ShadowPoolMeshPath);
        if (saved == null)
        {
            throw new Exception("ShadowPool mesh did not save.");
        }

        Vector3[] normals = saved.normals;
        float avgUp = 0f;
        for (int i = 0; i < normals.Length; i++)
        {
            avgUp += normals[i].y;
        }
        avgUp /= Mathf.Max(1, normals.Length);
        Assert(avgUp > 0.8f, $"ShadowPool mesh faces downward (average normal.y={avgUp:F3}); winding is inverted!");
        Debug.Log($"[MAHORAGA-BUILD] Built ShadowPool mesh: {saved.vertexCount} verts, {saved.triangles.Length / 3} tris, avg normal.y={avgUp:F3}, bounds={saved.bounds.size:F3}.");
        return saved;
    }

    private static void BuildSummonEffect(Transform root, Material shadowPoolMat, Mesh shadowPoolMesh)
    {
        GameObject effectGo = new GameObject("SummonEffect");
        effectGo.transform.SetParent(root, false);
        effectGo.transform.localPosition = Vector3.zero;
        effectGo.transform.localRotation = Quaternion.identity;

        // Pool
        GameObject poolGo = new GameObject("ShadowPool");
        poolGo.transform.SetParent(effectGo.transform, false);
        poolGo.transform.localScale = new Vector3(0.001f, 1f, 0.001f);
        var poolFilter = poolGo.AddComponent<MeshFilter>();
        poolFilter.sharedMesh = shadowPoolMesh;
        var poolRenderer = poolGo.AddComponent<MeshRenderer>();
        poolRenderer.sharedMaterial = shadowPoolMat;
        poolRenderer.shadowCastingMode = ShadowCastingMode.Off;
        poolRenderer.receiveShadows = true;
        poolRenderer.enabled = false;

        // Droplets: black glossy mesh particles splashing up while he climbs out
        GameObject dropGo = new GameObject("Droplets");
        dropGo.transform.SetParent(effectGo.transform, false);
        dropGo.transform.localPosition = new Vector3(0f, 0.05f, 0f);
        dropGo.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f); // cone emits along local +Z -> world up

        var ps = dropGo.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        var main = ps.main;
        main.loop = true;
        main.playOnAwake = false;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.5f, 0.95f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(1.6f, 4.8f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.035f, 0.12f);
        main.startColor = new Color(0.01f, 0.01f, 0.014f, 1f);
        main.gravityModifier = 1.7f;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = 160;

        var emission = ps.emission;
        emission.enabled = true;
        emission.rateOverTime = 0f;

        var shape = ps.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = 24f;
        shape.radius = 0.75f;
        shape.radiusThickness = 1f;

        var sizeOverLifetime = ps.sizeOverLifetime;
        sizeOverLifetime.enabled = true;
        sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
            new Keyframe(0f, 1f), new Keyframe(0.7f, 0.9f), new Keyframe(1f, 0f)));

        var psr = dropGo.GetComponent<ParticleSystemRenderer>();
        psr.renderMode = ParticleSystemRenderMode.Mesh;
        psr.mesh = Resources.GetBuiltinResource<Mesh>("Sphere.fbx");
        psr.sharedMaterial = shadowPoolMat;
        psr.shadowCastingMode = ShadowCastingMode.Off;
        psr.receiveShadows = false;

        var effect = effectGo.AddComponent<MahoragaSummonEffect>();
        effect.poolTransform = poolGo.transform;
        effect.poolRenderer = poolRenderer;
        effect.droplets = ps;
    }

    private static AudioSource Configure3DAudioSource(GameObject go, float minDist, float maxDist, AudioMixerGroup mixerGroup)
    {
        AudioSource src = go.AddComponent<AudioSource>();
        src.playOnAwake = false;
        src.loop = false;
        src.spatialBlend = 1.0f;
        src.rolloffMode = AudioRolloffMode.Logarithmic;
        src.minDistance = minDist;
        src.maxDistance = maxDist;
        src.dopplerLevel = 0.5f;
        src.volume = 1.0f;
        src.outputAudioMixerGroup = mixerGroup;

        AudioLowPassFilter lpf = go.AddComponent<AudioLowPassFilter>();
        lpf.cutoffFrequency = 22000f;
        lpf.lowpassResonanceQ = 1.0f;

        OccludeAudio occlude = go.AddComponent<OccludeAudio>();
        occlude.useReverb = false;
        occlude.overridingLowPass = false;
        occlude.lowPassOverride = 20000f;
        occlude.debugLog = false;

        return src;
    }

    private static Transform FindDescendant(Transform parent, string targetName)
    {
        foreach (Transform t in parent.GetComponentsInChildren<Transform>(true))
        {
            if (t.name == targetName)
            {
                return t;
            }
        }
        return null;
    }

    private static void DeleteIfExists(string assetPath)
    {
        if (AssetDatabase.LoadMainAssetAtPath(assetPath) != null)
        {
            AssetDatabase.DeleteAsset(assetPath);
        }
    }

    private static void AssignBundleLabels()
    {
        // Clear any existing bundle assignments first
        foreach (string existingBundle in AssetDatabase.GetAllAssetBundleNames())
        {
            foreach (string p in AssetDatabase.GetAssetPathsFromAssetBundle(existingBundle))
            {
                AssetImporter ai = AssetImporter.GetAtPath(p);
                if (ai != null)
                {
                    ai.SetAssetBundleNameAndVariant(string.Empty, string.Empty);
                }
            }
        }
        AssetDatabase.RemoveUnusedAssetBundleNames();

        var bundledPaths = new List<string>
        {
            PrefabPath,
            EnemyTypePath,
            TerminalNodePath,
            TerminalKeywordPath,
            AnimatorPath,
            MahoragaMatPath,
            ShadowPoolMatPath,
            ShadowPoolMeshPath
        };
        foreach (string clipName in RequiredAudioNames)
        {
            bundledPaths.Add($"{AudioDir}/{clipName}.ogg");
        }

        foreach (string path in bundledPaths)
        {
            AssetImporter importer = AssetImporter.GetAtPath(path);
            if (importer == null)
            {
                throw new Exception($"Cannot assign AssetBundle label; missing importer at '{path}'.");
            }
            importer.SetAssetBundleNameAndVariant(BundleName, string.Empty);
        }

        AssetDatabase.SaveAssets();
        Debug.Log($"[MAHORAGA-BUILD] Tagged {bundledPaths.Count} contract assets into AssetBundle '{BundleName}'.");
    }

    private static string BuildWindowsAssetBundle()
    {
        EnsureDirectory(BundleOutputDir);

        // Clean previous bundle outputs for deterministic build
        string bundleFile = Path.Combine(BundleOutputDir, BundleName);
        if (File.Exists(bundleFile))
        {
            File.Delete(bundleFile);
        }
        string manifestFile = bundleFile + ".manifest";
        if (File.Exists(manifestFile))
        {
            File.Delete(manifestFile);
        }

        AssetBundleManifest manifest = BuildPipeline.BuildAssetBundles(
            BundleOutputDir,
            BuildAssetBundleOptions.ChunkBasedCompression | BuildAssetBundleOptions.ForceRebuildAssetBundle,
            BuildTarget.StandaloneWindows64);

        if (manifest == null)
        {
            throw new Exception("BuildPipeline.BuildAssetBundles returned null!");
        }

        if (!File.Exists(bundleFile))
        {
            throw new Exception($"Expected AssetBundle file not created at '{bundleFile}'.");
        }

        long sizeBytes = new FileInfo(bundleFile).Length;
        Debug.Log($"[MAHORAGA-BUILD] Built AssetBundle '{bundleFile}' ({sizeBytes:N0} bytes, target=StandaloneWindows64, LZ4 ChunkBasedCompression).");
        return bundleFile;
    }

    private static void VerifyBuiltBundle(string bundlePath)
    {
        Debug.Log("[MAHORAGA-VERIFY] === Starting In-Editor Reload Self-Check ===");
        AssetBundle.UnloadAllAssetBundles(true);

        AssetBundle bundle = AssetBundle.LoadFromFile(bundlePath);
        if (bundle == null)
        {
            throw new Exception($"SELF-CHECK FAILED: Could not reload AssetBundle from '{bundlePath}'.");
        }

        try
        {
            string[] allNames = bundle.GetAllAssetNames();
            Debug.Log($"[MAHORAGA-VERIFY] Bundle contains {allNames.Length} explicit assets: {string.Join(", ", allNames)}");

            // 1. Verify all 14 contract assets load by exact name
            GameObject prefab = RequireAsset<GameObject>(bundle, "MahoragaPrefab");
            EnemyType enemyType = RequireAsset<EnemyType>(bundle, "MahoragaEnemyType");
            TerminalNode terminalNode = RequireAsset<TerminalNode>(bundle, "MahoragaTerminalNode");
            TerminalKeyword terminalKeyword = RequireAsset<TerminalKeyword>(bundle, "MahoragaTerminalKeyword");
            RuntimeAnimatorController animCtrl = RequireAsset<RuntimeAnimatorController>(bundle, "MahoragaAnimator");
            Material mahoragaMat = RequireAsset<Material>(bundle, "Mahoraga_Mat");
            Material shadowPoolMaterial = RequireAsset<Material>(bundle, "ShadowPool_Mat");
            Mesh shadowPoolMeshAsset = RequireAsset<Mesh>(bundle, "ShadowPool");
            Assert(shadowPoolMaterial.shader != null && shadowPoolMaterial.shader.name == "HDRP/Lit",
                $"ShadowPool_Mat shader mismatch: '{shadowPoolMaterial.shader?.name}'");
            Assert(shadowPoolMeshAsset.vertexCount > 100, "ShadowPool mesh is empty");
            foreach (string clipName in RequiredAudioNames)
            {
                AudioClip clip = RequireAsset<AudioClip>(bundle, clipName);
                if (clip.length <= 0.1f)
                {
                    throw new Exception($"SELF-CHECK FAILED: AudioClip '{clipName}' has invalid duration {clip.length}s.");
                }
            }

            // 2. Verify ScriptableObjects
            Assert(enemyType.enemyName == "Mahoraga", "EnemyType.enemyName must be 'Mahoraga'");
            Assert(enemyType.enemyPrefab != null, "EnemyType.enemyPrefab is null");
            Assert(enemyType.hitBodySFX == null, "EnemyType.hitBodySFX must be null per #15");
            Assert(enemyType.hitEnemyVoiceSFX == null, "EnemyType.hitEnemyVoiceSFX must be null per #15");
            Assert(enemyType.deathSFX == null, "EnemyType.deathSFX must be null per #15");
            Assert(Mathf.Approximately(enemyType.doorSpeedMultiplier, 1.5f), "EnemyType.doorSpeedMultiplier must be 1.5");
            Assert(terminalNode.creatureName == "Mahoraga", "TerminalNode.creatureName must be 'Mahoraga'");
            Assert(terminalNode.creatureFileID == -1, "TerminalNode.creatureFileID must be -1");
            Assert(terminalNode.buyItemIndex == -1, "TerminalNode.buyItemIndex must be -1");
            Assert(terminalNode.storyLogFileID == -1, "TerminalNode.storyLogFileID must be -1");
            Assert(!string.IsNullOrWhiteSpace(terminalNode.displayText) && terminalNode.displayText.Contains("MAHORAGA"), "TerminalNode.displayText invalid");
            Assert(terminalKeyword.word == "mahoraga", "TerminalKeyword.word must be 'mahoraga'");
            Assert(terminalKeyword.specialKeywordResult != null, "TerminalKeyword.specialKeywordResult is null");

            // 3. Verify Material shader
            Assert(mahoragaMat.shader != null && mahoragaMat.shader.name == "HDRP/Lit",
                $"Mahoraga_Mat shader mismatch: '{mahoragaMat.shader?.name}' (expected 'HDRP/Lit')");
            Assert(mahoragaMat.GetTexture("_BaseColorMap") != null, "Mahoraga_Mat missing _BaseColorMap");
            Assert(mahoragaMat.GetTexture("_MaskMap") != null, "Mahoraga_Mat missing _MaskMap");
            Assert(mahoragaMat.GetTexture("_NormalMap") != null, "Mahoraga_Mat missing _NormalMap");
            Assert(mahoragaMat.GetTexture("_EmissiveColorMap") != null, "Mahoraga_Mat missing _EmissiveColorMap");

            // 4. Verify AnimatorController clips
            int expectedClipCount = RequiredClips.Length + (_hasRealSummonClip ? 1 : 0);
            int distinctClipCount = animCtrl.animationClips.Where(c => c != null).Select(c => c.name).Distinct().Count();
            Assert(distinctClipCount == expectedClipCount,
                $"AnimatorController has {distinctClipCount} distinct clips (expected {expectedClipCount}; Summon_Emerge real clip present: {_hasRealSummonClip})");
            if (_hasRealSummonClip)
            {
                Assert(animCtrl.animationClips.Any(c => c != null && c.name == SummonClipName), $"AnimatorController missing clip '{SummonClipName}'");
            }

            // Parameters/states are editor-only data: inspect the source controller the bundle was built from.
            var animControllerEditor = AssetDatabase.LoadAssetAtPath<AnimatorController>(AnimatorPath);
            Assert(animControllerEditor != null, "MahoragaAnimator source controller not found");
            Assert(animControllerEditor.parameters.Any(pr => pr.name == "Summon" && pr.type == AnimatorControllerParameterType.Trigger), "Animator missing trigger 'Summon'");
            Assert(animControllerEditor.parameters.Any(pr => pr.name == "SummonSpeed" && pr.type == AnimatorControllerParameterType.Float), "Animator missing float 'SummonSpeed'");
            AnimatorState summonAnimState = animControllerEditor.layers[0].stateMachine.states
                .Select(cs => cs.state).FirstOrDefault(st => st.name == "Summon_Emerge");
            Assert(summonAnimState != null && summonAnimState.motion != null, "Animator state 'Summon_Emerge' missing or has no motion");
            Assert(summonAnimState.motion.name == (_hasRealSummonClip ? SummonClipName : FallbackSummonClip),
                $"Summon_Emerge motion is '{summonAnimState.motion.name}', expected '{(_hasRealSummonClip ? SummonClipName : FallbackSummonClip)}'");
            Assert(animControllerEditor.layers[0].stateMachine.anyStateTransitions.Any(tr => tr.destinationState == summonAnimState),
                "No Any State -> Summon_Emerge transition");
            foreach (string reqClip in RequiredClips)
            {
                Assert(animCtrl.animationClips.Any(c => c != null && c.name == reqClip), $"AnimatorController missing clip '{reqClip}'");
            }

            // 5. Instantiate prefab & inspect components, missing scripts, layers, tags, shaders, height
            GameObject inst = UnityEngine.Object.Instantiate(prefab);
            try
            {
                int missingScriptTotal = 0;
                foreach (Transform t in inst.GetComponentsInChildren<Transform>(true))
                {
                    missingScriptTotal += GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject);
                    foreach (Component c in t.gameObject.GetComponents<Component>())
                    {
                        if (c == null)
                        {
                            missingScriptTotal++;
                        }
                    }
                }
                Assert(missingScriptTotal == 0, $"Found {missingScriptTotal} missing script(s) on reloaded MahoragaPrefab!");

                // Root checks
                Assert(inst.layer == 19, $"Root layer is {inst.layer}, expected 19 (Enemies)");
                Assert(inst.CompareTag("Enemy"), $"Root tag is '{inst.tag}', expected 'Enemy'");

                NetworkObject netObj = inst.GetComponent<NetworkObject>();
                Assert(netObj != null, "Root missing NetworkObject");
                var netSo = new SerializedObject(netObj);
                long netHash = netSo.FindProperty("GlobalObjectIdHash")?.longValue ?? 0;
                Assert(netHash != 0, "NetworkObject GlobalObjectIdHash is 0!");

                NavMeshAgent nav = inst.GetComponent<NavMeshAgent>();
                Assert(nav != null, "Root missing NavMeshAgent");
                Assert(Mathf.Approximately(nav.radius, 0.60f), $"NavMeshAgent.radius={nav.radius}, expected 0.60");
                Assert(Mathf.Approximately(nav.height, 3.30f), $"NavMeshAgent.height={nav.height}, expected 3.30");
                Assert(Mathf.Approximately(nav.baseOffset, 0.0f), $"NavMeshAgent.baseOffset={nav.baseOffset}, expected 0.0");

                MahoragaAI ai = inst.GetComponent<MahoragaAI>();
                Assert(ai != null, "Root missing MahoragaAI");
                Assert(ai.enemyType != null, "MahoragaAI.enemyType is null");
                Assert(ai.creatureAnimator != null, "MahoragaAI.creatureAnimator is null");
                Assert(ai.creatureVoice != null, "MahoragaAI.creatureVoice is null");
                Assert(ai.creatureSFX != null, "MahoragaAI.creatureSFX is null");
                Assert(ai.dieSFX == null, "MahoragaAI.dieSFX must be null per #15");
                Assert(ai.agent != null, "MahoragaAI.agent is null");
                Assert(ai.eye != null, "MahoragaAI.eye is null");
                Assert(ai.thisNetworkObject != null, "MahoragaAI.thisNetworkObject is null");
                Assert(ai.enemyBehaviourStates != null && ai.enemyBehaviourStates.Length == 7,
                    $"MahoragaAI.enemyBehaviourStates.Length={ai.enemyBehaviourStates?.Length}, expected 7");
                string[] expectedStateNames = { "Patrolling", "Investigating", "Chasing", "Attacking", "Adapting", "Summoning", "EngagingEnemy" };
                for (int si = 0; si < expectedStateNames.Length; si++)
                {
                    Assert(ai.enemyBehaviourStates[si] != null && ai.enemyBehaviourStates[si].name == expectedStateNames[si],
                        $"enemyBehaviourStates[{si}] is '{ai.enemyBehaviourStates[si]?.name}', expected '{expectedStateNames[si]}'");
                }

                // Summoning hierarchy: root / SummonOffset / ModelContainer / Mahoraga, plus the SummonEffect pool.
                Transform offsetTr = inst.transform.Find("SummonOffset");
                Assert(offsetTr != null, "Missing SummonOffset pivot");
                Assert(offsetTr.localPosition == Vector3.zero && offsetTr.localScale == Vector3.one, "SummonOffset must start at zero offset and unit scale");
                Assert(offsetTr.Find("ModelContainer") != null, "ModelContainer must be a child of SummonOffset");
                Assert(offsetTr.Find("ModelContainer/Mahoraga") != null, "Mahoraga model must live under SummonOffset/ModelContainer");

                MahoragaSummonEffect summonFx = inst.GetComponentInChildren<MahoragaSummonEffect>(true);
                Assert(summonFx != null && summonFx.poolTransform != null && summonFx.poolRenderer != null && summonFx.droplets != null,
                    "MahoragaSummonEffect missing or references unwired");
                Assert(summonFx.poolRenderer.sharedMaterial != null && summonFx.poolRenderer.sharedMaterial.shader.name == "HDRP/Lit",
                    "ShadowPool renderer must use an HDRP/Lit material");
                Assert(summonFx.poolRenderer.GetComponent<MeshFilter>().sharedMesh != null, "ShadowPool MeshFilter has no mesh");
                var fxRenderer = summonFx.droplets.GetComponent<ParticleSystemRenderer>();
                Assert(fxRenderer.renderMode == ParticleSystemRenderMode.Mesh && fxRenderer.mesh != null && fxRenderer.sharedMaterial != null,
                    "Droplets particle renderer must be Mesh mode with a mesh and material");
                Assert(summonFx.droplets.emission.rateOverTime.constant == 0f, "Droplets must not emit until the summoning drives them");

                Assert(ai.wheelBone != null, "MahoragaAI.wheelBone is null");
                Assert(ai.swordBone != null, "MahoragaAI.swordBone is null");
                Assert(ai.wheelGlowLight != null, "MahoragaAI.wheelGlowLight is null");
                Assert(ai.audioController != null, "MahoragaAI.audioController is null");
                Assert(ai.mainRenderer != null, "MahoragaAI.mainRenderer is null");

                MahoragaAudioController audioCtrl = inst.GetComponent<MahoragaAudioController>();
                Assert(audioCtrl != null, "Root missing MahoragaAudioController");
                Assert(audioCtrl.voiceSource != null && audioCtrl.bodySource != null, "MahoragaAudioController AudioSources missing");
                Assert(audioCtrl.voiceSource.outputAudioMixerGroup != null && audioCtrl.voiceSource.outputAudioMixerGroup.audioMixer.name == "Diagetic",
                    "VoiceSFXSource not routed to Diagetic mixer");
                Assert(audioCtrl.bodySource.outputAudioMixerGroup != null && audioCtrl.bodySource.outputAudioMixerGroup.audioMixer.name == "Diagetic",
                    "BodySFXSource not routed to Diagetic mixer");
                Assert(audioCtrl.voiceSource.GetComponent<AudioLowPassFilter>() != null, "VoiceSFXSource missing AudioLowPassFilter");
                Assert(audioCtrl.bodySource.GetComponent<AudioLowPassFilter>() != null, "BodySFXSource missing AudioLowPassFilter");
                Assert(audioCtrl.musicMainClip != null && audioCtrl.wheelTurnClip != null &&
                       audioCtrl.footstep1Clip != null && audioCtrl.footstep2Clip != null &&
                       audioCtrl.slashClip != null && audioCtrl.impactClip != null &&
                       audioCtrl.roarClip != null && audioCtrl.deathClip != null,
                    "One or more AudioClips null on MahoragaAudioController");
                Assert(audioCtrl.summonClip != null && audioCtrl.summonClip.name == "mahoraga_summon" && audioCtrl.summonClip.length > 4.5f,
                    "MahoragaAudioController.summonClip missing or too short");

                // CollisionCollider checks
                Transform colTr = inst.transform.Find("CollisionCollider");
                Assert(colTr != null, "Missing CollisionCollider child");
                Assert(colTr.gameObject.layer == 19, $"CollisionCollider layer={colTr.gameObject.layer}, expected 19");
                Assert(colTr.gameObject.CompareTag("Enemy"), $"CollisionCollider tag='{colTr.gameObject.tag}', expected 'Enemy'");
                BoxCollider colBox = colTr.GetComponent<BoxCollider>();
                Assert(colBox != null && colBox.isTrigger, "CollisionCollider BoxCollider missing or isTrigger!=true");
                EnemyAICollisionDetect detect = colTr.GetComponent<EnemyAICollisionDetect>();
                Assert(detect != null && detect.mainScript == ai, "EnemyAICollisionDetect missing or mainScript not wired");

                // ScanNode checks
                Transform scanTr = inst.transform.Find("ScanNode");
                Assert(scanTr != null, "Missing ScanNode child");
                Assert(scanTr.gameObject.layer == 22, $"ScanNode layer={scanTr.gameObject.layer}, expected 22");
                BoxCollider scanBox = scanTr.GetComponent<BoxCollider>();
                Assert(scanBox != null && !scanBox.isTrigger, "ScanNode BoxCollider missing or isTrigger!=false");
                ScanNodeProperties scanProps = scanTr.GetComponent<ScanNodeProperties>();
                Assert(scanProps != null && scanProps.nodeType == 1 && scanProps.headerText == "Mahoraga", "ScanNodeProperties invalid");

                // MapDot checks
                Transform mapDotTr = inst.transform.Find("MapDot");
                Assert(mapDotTr != null, "Missing MapDot child");
                Assert(mapDotTr.gameObject.layer == 14, $"MapDot layer={mapDotTr.gameObject.layer}, expected 14");
                MeshRenderer mapDotMr = mapDotTr.GetComponent<MeshRenderer>();
                Assert(mapDotMr != null && mapDotMr.sharedMaterial != null && mapDotMr.sharedMaterial.shader.name == "HDRP/Unlit",
                    $"MapDot shader mismatch: '{mapDotMr?.sharedMaterial?.shader?.name}'");

                // Shader & height verification on SkinnedMeshRenderer
                SkinnedMeshRenderer smr = inst.GetComponentInChildren<SkinnedMeshRenderer>(true);
                Assert(smr != null, "Reloaded prefab missing SkinnedMeshRenderer");
                foreach (Material m in smr.sharedMaterials)
                {
                    Assert(m != null && m.shader != null && m.shader.name == "HDRP/Lit",
                        $"SkinnedMeshRenderer material '{m?.name}' uses shader '{m?.shader?.name}' instead of 'HDRP/Lit'!");
                }

                Bounds smrBounds = smr.bounds;
                Mesh baked = new Mesh();
                smr.BakeMesh(baked);
                Vector3[] verts = baked.vertices;
                BoneWeight[] weights = smr.sharedMesh.boneWeights;
                int wheelBoneIndex = Array.FindIndex(smr.bones, b => b != null && b.name == "WheelBone");
                Assert(wheelBoneIndex >= 0, "WheelBone not found in SkinnedMeshRenderer.bones!");

                float minY = float.MaxValue;
                float totalMaxY = float.MinValue;
                float bodyMaxY = float.MinValue;
                float wheelMaxY = float.MinValue;

                for (int i = 0; i < verts.Length; i++)
                {
                    float wy = smr.transform.TransformPoint(verts[i]).y;
                    if (wy < minY) minY = wy;
                    if (wy > totalMaxY) totalMaxY = wy;

                    bool isWheelVert = (i < weights.Length) && (
                        (weights[i].boneIndex0 == wheelBoneIndex && weights[i].weight0 > 0.5f) ||
                        (weights[i].boneIndex1 == wheelBoneIndex && weights[i].weight1 > 0.5f));

                    if (isWheelVert)
                    {
                        if (wy > wheelMaxY) wheelMaxY = wy;
                    }
                    else
                    {
                        if (wy > bodyMaxY) bodyMaxY = wy;
                    }
                }
                UnityEngine.Object.DestroyImmediate(baked);

                Debug.Log($"[MAHORAGA-VERIFY] SkinnedMeshRenderer.bounds: min={smrBounds.min:F4}, max={smrBounds.max:F4}, size={smrBounds.size:F4}");
                Debug.Log($"[MAHORAGA-VERIFY] Exact Baked World Y: feetMinY={minY:F4}u, skullCrownBodyMaxY={bodyMaxY:F4}u, dharmaWheelTopMaxY={wheelMaxY:F4}u, totalMaxY={totalMaxY:F4}u");
                Debug.Log($"[MAHORAGA-VERIFY] MissingScripts=0, Shaders=HDRP/Lit (body) + HDRP/Unlit (MapDot), NetworkObjectHash={netHash}, Assets=14/14 + summon (clip, ShadowPool mesh+material) PASS, Summon_Emerge real clip={_hasRealSummonClip}.");

                Assert(Mathf.Abs(minY) <= 0.02f, $"Feet minY={minY:F4}u deviates from 0.00u!");
                Assert(bodyMaxY >= 3.675f && bodyMaxY <= 3.825f, $"Standing skull crown height={bodyMaxY:F4}u outside 3.75u ±2% [3.675, 3.825]!");
                Assert(totalMaxY <= 3.905f, $"Total height (incl. Dharma Wheel)={totalMaxY:F4}u exceeds 3.90u hallway ceiling clearance!");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(inst);
            }
        }
        finally
        {
            bundle.Unload(true);
        }

        Debug.Log("[MAHORAGA-VERIFY] === ALL SELF-CHECK ASSERTIONS PASSED ===");
    }

    private static T RequireAsset<T>(AssetBundle bundle, string assetName) where T : UnityEngine.Object
    {
        T asset = bundle.LoadAsset<T>(assetName);
        if (asset == null)
        {
            throw new Exception($"SELF-CHECK FAILED: Required asset '{assetName}' ({typeof(T).Name}) could not be loaded from '{BundleName}'.");
        }
        return asset;
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new Exception("SELF-CHECK FAILED: " + message);
        }
    }
}
