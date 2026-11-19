# Mahoraga LC — Independent QA & Verification Report (`QA_REPORT.md`)

> **Auditor:** `verifier` (read-only independent QA)  
> **Scope:** Phase 1 (Docs, Audio, Environment/RE) · Phase 2 (3D Model, Renders, Textures, FBX, Unity AssetBundle) · Phase 3 (C# Code, NetcodePatcher IL, Thunderstore Release Zip, `MahoragaDev` Parity)  
> **Status:** **ALL PHASES PASS** — Phase 1, Phase 2, and Phase 3 independently verified with **0 open findings** (`3 × P2` doc fixes verified after `#15`, `3` visual model polishes verified after `#19`, `1 × P1` + `1 × P2` C# runtime fixes verified after `#23`). Release ready for playtesting & distribution.

---

## Executive Summary

| Phase | Target | Owner | Verdict | Resolved / Open Findings |
|---|---|---|---|---|
| **Phase 1.1** | Docs internal consistency (`RE_FINDINGS`, `ASSET_CONTRACT`, `MAHORAGA_GDD`, `ARCHITECTURE`, `TERMINAL_BESTIARY`) | `@design-arch` / `@env-re` | **PASS** *(re-verified after `#15`)* | 0 open (3 `[P2]` items fixed & verified) |
| **Phase 1.2** | Audio deliverables (8 `.ogg` clips + `0:51` trim alignment + raw isolation) | `@audio` | **PASS** | 0 open |
| **Phase 1.3** | Environment & RE (`r2modman`, Unity `2022.3.62f2` WindowsStandaloneSupport + license, `MahoragaDev` profile) | `@env-re` | **PASS** | 0 open |
| **Phase 2.1** | 3D Model Visual QA (8 renders in `assets/model/renders/`), textures, `Mahoraga.fbx` | `@modeler` | **PASS** *(re-verified after `#19`)* | 0 open (`#17` visual polishes fixed & verified) |
| **Phase 2.2** | Unity AssetBundle `mahoraga_assets` & `UnityProject/build.log` | `@unity-builder` | **PASS** *(verified `#20`)* | 0 open |
| **Phase 3.1** | Final `Fede.MahoragaLC.dll` NetcodePatcher IL (`rea` + `ilspycmd`), code audit & unit tests | `@csharp-dev` | **PASS** *(re-verified after `#23`)* | 0 open (`1 × P1`, `1 × P2` fixed & verified) |
| **Phase 3.2** | Thunderstore zip `dist/Fede-MahoragaLC-1.0.0.zip` & `MahoragaDev` byte-for-byte parity | `@csharp-dev` | **PASS** *(re-verified after `#23`)* | 0 open |

---

## Phase 1 — Documentation, Audio & Environment Audit

### 1.1 Documentation & Decompiled API Consistency Audit

Audited all 5 documents (`docs/RE_FINDINGS.md`, `docs/ASSET_CONTRACT.md`, `docs/MAHORAGA_GDD.md`, `docs/ARCHITECTURE.md`, `docs/TERMINAL_BESTIARY.md`) against each other and directly against decompiled `Assembly-CSharp.dll` (`EnemyAI.cs`, `DoorLock.cs`, `Terminal.cs`, `TerminalNode`, `TerminalKeyword`) and `LethalLib 1.2.0` (`Enemies.cs`, `Levels.cs`, `Utilities.cs`, `TerminalUtils.cs`).

#### Findings & Resolution Status (Re-verified after `#15`)

1. **`[P2]` RESOLVED — Align Thunderstore package plugin path in `ARCHITECTURE.md` with required `BepInEx/plugins/Fede-MahoragaLC/` layout — `docs/ARCHITECTURE.md:50`**  
   *Fixed in `#15` and verified:* `docs/ARCHITECTURE.md:50` now specifies `BepInEx/plugins/Fede-MahoragaLC/Fede.MahoragaLC.dll` and `BepInEx/plugins/Fede-MahoragaLC/mahoraga_assets`.

2. **`[P2]` RESOLVED — Resolve duplicate `mahoraga_death` audio playback on `KillEnemy` — `docs/ARCHITECTURE.md:131`, `docs/ASSET_CONTRACT.md:287,344`**  
   *Fixed in `#15` and verified:* `ASSET_CONTRACT.md:287,344` sets `EnemyAI.dieSFX = null` and `MahoragaEnemyType.deathSFX = null`, and `ARCHITECTURE.md:131` establishes `MahoragaAudioController.OnMahoragaDied()` as the single owner of `mahoraga_death` + `0.35s` music fade-out (with a `dieSFX == null` guard).

3. **`[P2]` RESOLVED — Guard `EnemyAI.HitEnemy` unguarded `hitEnemyVoiceSFX` `PlayOneShot(null)` call — `docs/ARCHITECTURE.md:127`, `docs/ASSET_CONTRACT.md:342-343`**  
   *Fixed in `#15` and verified:* `ASSET_CONTRACT.md:342-343` sets `hitBodySFX = null` and `hitEnemyVoiceSFX = null`, and `ARCHITECTURE.md:127` mandates `base.HitEnemy(force, playerWhoHit, playHitSFX: false, hitID)` while routing hit/deflect audio through `MahoragaAudioController`.

#### Verified Consistent Across All 5 Docs (PASS)
- **Zero `[PENDING]` placeholders** remain in any doc.
- **Scale & Clearance:** `playerHeight = 2.50 u` -> `mahoragaTargetHeight = 3.75 u` (`1.5×`, feet `Z=0` / `Y=0` to skull crown `3.75 u`), Dharma Wheel top `<= 3.90 u` (clears `3.916 u` factory hallway ceilings), `WheelBone` local Y hub normal (`+45°` per adaptation), doorway-safe `NavMeshAgent` (`radius=0.60`, `height=3.30`, `baseOffset=0.0`), `EyeTransform` `Y=3.30`.
- **Layers & Tags:** `Player=3`, `Room=8`, `InteractableObject=9`, `MapRadar=14` (`MapDot`), `LineOfSight=18`, `Enemies=19` + `Tag="Enemy"` (Root & `CollisionCollider`, `isTrigger=true`), `ScanNode=22` (`isTrigger=false`, `nodeType=1`), `EnemiesNotRendered=23`.
- **Engine & Packages:** Unity `2022.3.62f2`, HDRP `14.0.12`, NGO `1.12.2`, AI.Navigation `1.1.6`, LethalLib `1.2.0`.
- **API Signatures Verified Against Decompiled IL:**
  - `LethalLib.Modules.Enemies.RegisterEnemy` Overload 3 (`Enemies.cs:435`): exact match; `Terminal_Start` (`Enemies.cs:238-244`) dynamically assigns `creatureFileID` and updates `ScanNodeProperties.creatureScanID`, and skips duplicate `creatureName` when outdoor clone is registered (`Enemies.cs:219-223`).
  - `TerminalNode` field initializers (`Assembly-CSharp.dll`): `buyItemIndex = -1`, `buyVehicleIndex = -1`, `buyRerouteToMoon = -1`, `displayPlanetInfo = -1`, `shipUnlockableID = -1`, `creatureFileID = -1`, `storyLogFileID = -1`, `playSyncedClip = -1`; `Terminal.AttemptLoadCreatureFileNode` gates `creatureFileID` on `scannedEnemyIDs`.
  - `DoorLock` (`DoorLock.cs:129,293,304-305,348`): `OnTriggerStay` checks `other.CompareTag("Enemy")`; forced breach via `UnlockDoorSyncWithServer()` + `AnimatedObjectTrigger.TriggerAnimationNonPlayer(..., overrideBool: true)` + `OpenDoorAsEnemyServerRpc()` matches vanilla enemy door-opening flow.
  - `LethalLib.Modules.Utilities.FixMixerGroups` (`Utilities.cs:49-86,126-133`): matches `Diagetic` mixer group name.
- **Contract Identifiers:** Bundle `mahoraga_assets`, assets (`MahoragaPrefab`, `MahoragaEnemyType`, `MahoragaTerminalNode`, `MahoragaTerminalKeyword`, `MahoragaAnimator`, `Mahoraga_Mat`), 15 core/special bones, 8 in-place clips, 7 Animator parameters, 8 audio clip names, 5 `enemyBehaviourStates`, assembly `Fede.MahoragaLC.dll`, namespace `MahoragaLC` — 100% aligned.

---

### 1.2 Independent Audio Deliverables Audit (`assets/audio/`) — **PASS**

Independently verified using `ffprobe`, `ffmpeg` (`ebur128=peak=true`), and Python (`numpy` + `scipy.signal.correlate`):

- **Source Isolation:** `raw_source.wav` (`pcm_s16le`, `48000 Hz`, 2 ch, `182.636563 s`, `35,066,298 B`) and `raw_source.info.json` exist **exclusively** inside `assets/audio/source/`. Zero `.wav` files in `assets/audio/`.
- **Strict `0:51` Trim Alignment (Independent FFT Cross-Correlation):** Matched `mahoraga_music_main.ogg` `[1.0s .. 9.0s]` against `assets/audio/source/raw_source.wav` `[45.0s .. 65.0s]`:
  - **Measured trim start in `raw_source.wav`:** **`51.000000 s`** (**`+0.000 ms` error**)
  - **Pearson correlation:** **`r = 0.997044`**
  - **Stereo loop wrap delta `|sample[0] - sample[-1]|`:** **`0.00000279`** (click-free)
- **Footstep Variation Independence:** `mahoraga_footstep_1.ogg` vs `mahoraga_footstep_2.ogg` Pearson `r = 0.000055`.

| File | Duration | Codec / SR / Ch | Size (B) | LUFS | True Peak (dBTP) | RMS (dBFS) | Sample Peak (dBFS) | Clipped | End Max | SHA-256 (16) | Verdict |
|---|---:|---|---:|---:|---:|---:|---:|---:|---:|---|---|
| `mahoraga_music_main.ogg` | `130.219 s` | Vorbis `44100 Hz` 2ch | `2,889,391` | `-14.00` | `-5.30` | `-16.77` | `-5.33` | `0` | `0.000000` | `7605155c6617ed86` | **PASS** |
| `mahoraga_wheel_turn.ogg` | `2.199 s` | Vorbis `44100 Hz` 1ch | `20,801` | `-13.90` | `-1.70` | `-13.48` | `-1.75` | `0` | `0.000000` | `2e3e5832f49ab7d7` | **PASS** |
| `mahoraga_footstep_1.ogg` | `0.480 s` | Vorbis `44100 Hz` 1ch | `7,436` | `-16.20` | `-1.90` | `-12.63` | `-1.91` | `0` | `0.000003` | `3c0a91fa92669ed8` | **PASS** |
| `mahoraga_footstep_2.ogg` | `0.518 s` | Vorbis `44100 Hz` 1ch | `7,853` | `-18.50` | `-1.80` | `-14.75` | `-1.77` | `0` | `0.000000` | `3cd6e42e59c0cccc` | **PASS** |
| `mahoraga_slash.ogg` | `0.780 s` | Vorbis `44100 Hz` 1ch | `12,311` | `-11.90` | `-1.70` | `-13.90` | `-1.72` | `0` | `0.000001` | `ed1da68276579186` | **PASS** |
| `mahoraga_impact.ogg` | `0.742 s` | Vorbis `44100 Hz` 1ch | `11,291` | `-17.30` | `-1.60` | `-15.80` | `-1.68` | `0` | `0.000000` | `82e773677eefa4ff` | **PASS** |
| `mahoraga_roar.ogg` | `2.048 s` | Vorbis `44100 Hz` 1ch | `24,995` | `-11.90` | `-1.50` | `-12.61` | `-1.54` | `0` | `0.000000` | `908aa535d7505259` | **PASS** |
| `mahoraga_death.ogg` | `2.480 s` | Vorbis `44100 Hz` 1ch | `34,895` | `-13.80` | `-1.70` | `-12.64` | `-1.71` | `0` | `0.000000` | `0d7844f8e17c1aea` | **PASS** |

All 8 files match `assets/audio/AUDIO_MANIFEST.md` verbatim.

---

### 1.3 Environment, Unity Batchmode & `MahoragaDev` Profile Audit — **PASS**

- **`r2modman` v3.2.21:** Verified `/home/fede/.local/bin/r2modman` (`200,580,076 B`, `rwxrwxr-x`, ELF 64-bit x86-64 executable) and valid desktop entry `/home/fede/.local/share/applications/r2modman.desktop` (`Exec=/home/fede/.local/bin/r2modman %U`).
- **Unity `2022.3.62f2` (`7670c08855a9`) + `WindowsStandaloneSupport`:**
  - Verified `/home/fede/Unity/2022.3.62f2/Editor/Data/PlaybackEngines/WindowsStandaloneSupport/` contains `UnityEditor.WindowsStandalone.Extensions.dll`, `WinPlayerBuildProgram.exe`, `Variations/` (`mono`, `win32_*`, `win64_*`), `Bee/`, `modules.asset`.
  - Verified user-space symlinks: `Editor/Data/Tools/libxml2.so.2` -> Flatpak UnityHub `libxml2.so.2`; `~/.config/unity3d/Unity/{config,licenses}` and `~/.local/share/unity3d/Unity/{config,licenses}` -> Flatpak UnityHub config/licenses.
  - **Live Batchmode Test Executed:** `/home/fede/Unity/2022.3.62f2/Editor/Unity -batchmode -nographics -quit -logFile -` -> **`EXIT_CODE=0`** with `[Licensing::Client] Successfully resolved entitlement details`, `Register platform support module: .../WindowsStandaloneSupport/UnityEditor.WindowsStandalone.Extensions.dll`, and `Exiting batchmode successfully now!`.
- **`MahoragaDev` Profile (`/home/fede/.config/r2modmanPlus-local/LethalCompany/profiles/MahoragaDev/`):**
  - Verified `doorstop_config.ini` (`enabled = true`, `target_assembly=BepInEx\core\BepInEx.Preloader.dll`), `winhttp.dll`, and all 10 Thunderstore packages with exact manifest versions:
    - `BepInExPack v5.4.2305`, `LethalLib v1.2.0` (`BepInEx/plugins/Evaisa-LethalLib/LethalLib/LethalLib.dll` + `lethallib`), `HookGenPatcher v0.0.5`, `MonoDetour v0.6.3`, `MonoDetour_BepInEx_5 v0.6.3`, `Imperium v1.4.0`, `BepInEx_MonoMod_Debug_Patcher v1.1.1`, `LethalCompany_InputUtils v0.7.13`, `LethalNetworkAPI v3.3.3`, `OdinSerializer v2024.2.2700`.
- **Archived RE Evidence & Reference Repo:** Verified `tools/LC-ExampleEnemy-ref/`, `docs/re/unity_batchmode_verified.log`, 11 `docs/re/rea/*.json` files, `docs/re/decompiled/`, `docs/re/decompiled_lethallib/`.

---

## Phase 2 — 3D Model Visual QA, Textures, FBX & Unity AssetBundle Audit

### 2.1 Final 3D Model, Renders, Textures & FBX Audit (`#19`) — **PASS**

- **Visual Inspection of All 8 Updated Renders (`read_file` on every PNG in `assets/model/renders/`):**
  - `front.png`, `three_quarter.png`, `side.png`, `back.png`, `closeup_head_wheel.png`, `anim_attack_pose.png`, `scale_comparison_1_5x.png`, `icon_256.png`:
  - Confirmed unmistakable JJK Divine General Mahoraga silhouette (eyeless elongated pale skull, 4 layered white feathered eye-socket wings, fanged grimacing jaw + dark mouth cavity, curved segmented occipital tail `HeadTail_1..4`, hyper-muscular bone-white anatomy, dark charcoal pleated hakama + obi sash, bandaged right forearm + ornate guard & double-edged Sword of Extermination, ornate antique-gold 8-spoked Dharma wheel).
  - Verified all 3 `#17` visual polishes cleanly fixed: (1) zero stray tooth tips outside lips/cheeks in `closeup_head_wheel.png` & `icon_256.png`; (2) zero white torso poke-through on dark obi sash in `back.png`; (3) smooth fused ankle/Achilles/instep & left wrist-to-hand transitions in `front.png`, `back.png`, `side.png`, `three_quarter.png`, and `anim_attack_pose.png`.
  - Confirmed clear labeled `1.5x` vs `1.0x` height comparison (`3.750 m` Mahoraga vs `2.500 m` LC Crewmate, both feet planted cleanly at `Z = 0.000 m`) and crisp `256×256` RGBA PNG `icon_256.png` (`86,815 B`, SHA-256 `d67bc9226fdef9d7416f8505bad3f827f348477f5d7f2ab33f457b8c2ede94c7`).
- **All 6 HDRP 2048×2048 PBR Textures (`assets/model/textures/`) — PASS:**
  - Verified exact `2048×2048` dimensions for `Mahoraga_Albedo.png` (RGBA), `Mahoraga_Normal.png` (RGB, OpenGL tangent-space `B` min `222`, mean `253.71`), `Mahoraga_Metallic.png` (L), `Mahoraga_Roughness.png` (L), `Mahoraga_Emissive.png` (RGB, 89.13% `#000000`, 10.87% golden mean RGB `(221.1, 183.5, 83.1)`), and `Mahoraga_MaskMap.png` (RGBA: `R == Metallic` with `max_diff=0`, `G` AO `110..252`, `B == 128` exact constant, `A == 255 - Roughness` with `max_diff=0`).
- **Independent Headless Blender 5.2.3 LTS FBX Re-Import (`assets/model/Mahoraga.fbx`, `3,262,268 B`; `Mahoraga.blend`, `1,396,902 B`) — PASS:**
  - `17,082` verts, **`32,144` triangles** (within `15k–35k` budget), 1 UV map (`UVMap`), 1 material (`Mahoraga_Mat`), all transforms cleanly applied `(0,0,0)/(0,0,0)/(1,1,1)`.
  - Exact **31 bones** matching `ASSET_CONTRACT.md` §3.3 verbatim (`0` missing, `0` wrong parent, `0` extra).
  - World bounds: `Z = [0.000000, 3.871367]`, feet at `0.000000 m`, non-wheel skull crown at exact **`3.750000 m`**, Dharma Wheel `Z = [2.938632, 3.871367]` (`<= 3.90 m`), facing Blender `-Y` (= Unity `+Z`).
  - Exclusive skinning: `WheelBone` `2,530` verts (`0` impure), `SwordBone` `673` verts (`0` impure).
  - `WheelBone` calibration: centroid offset **`0.000001 m`**, `|dot(WheelBone.localY, wheelPlaneNormal)| = 1.000000`.
  - All 8 baked 30-FPS AnimStack clips present (`Armature|Idle`, `Walk`, `Run`, `Attack_Cleave`, `Attack_Slam`, `Adapt_WheelSpin`, `Stun`, `Death`); locomotion/attack/adapt/stun clips strictly in-place (`net_root_XY = 0.0000 m`); `Adapt_WheelSpin` rotates `WheelBone` exact **`+45.0°`** around pure local Y `(0.0, 45.0, 0.0)`.

### 2.2 Unity AssetBundle & `UnityProject/build.log` Audit (`#20`) — **PASS**

- **Editor & Package Versions (`ProjectSettings/ProjectVersion.txt`, `Packages/manifest.json`):**
  - Unity `2022.3.62f2 (7670c08855a9)`, `com.unity.render-pipelines.high-definition: 14.0.12`, `com.unity.netcode.gameobjects: 1.12.2`, `com.unity.ai.navigation: 1.1.6`, `com.unity.inputsystem: 1.14.0`.
- **AssetBundle Header & Manifest (`UnityProject/AssetBundles/StandaloneWindows64/mahoraga_assets`):**
  - Size: **`12,335,022` bytes**, SHA-256 `8e799c50c59e4dbe71549bde471a4da538fe37d224dfcbef42aa1fdb090d6c4a`, header `UnityFS 2022.3.62f2`, target `BuildTarget.StandaloneWindows64`, `BuildAssetBundleOptions.ChunkBasedCompression` (LZ4), `Dependencies: []`.
  - All 14 contract assets explicitly bundled (`MahoragaPrefab`, `MahoragaEnemyType`, `MahoragaTerminalNode`, `MahoragaTerminalKeyword`, `MahoragaAnimator`, `Mahoraga_Mat`, and all 8 `mahoraga_*.ogg` clips).
  - Resolved all 9 Class 114 Script GUIDs from `mahoraga_assets.manifest`: `Fede.MahoragaLC.dll` (`MahoragaAI`, `MahoragaAudioController`), `Assembly-CSharp` (`EnemyAICollisionDetect`, `TerminalNode`, `EnemyType`, `OccludeAudio`, `TerminalKeyword`, `ScanNodeProperties`), `NGO 1.12.2` (`NetworkObject`), `HDRP 14.0.12` (`HDAdditionalLightData`).
  - Verified **0** `raw_source` or `.wav` files anywhere under `UnityProject/Assets/`.
- **`UnityProject/build.log` & `MahoragaAssetBuilder.cs` Audit:**
  - Clean exit (`Exiting batchmode successfully now!`), 0 compiler/shader/script errors, all `[MAHORAGA-VERIFY]` in-Editor reload assertions PASSED:
  - `MissingScripts=0`, `Shaders=HDRP/Lit (body) + HDRP/Unlit (MapDot)`, `NetworkObjectHash=705233307`, `Assets=14/14 PASS`.
  - Measured baked prefab height: `feetMinY = 0.0000 u`, standing skull crown **`3.7500 u`** (exact `1.5×`, within `±2%`), floating Dharma Wheel top **`3.8714 u`** (`<= 3.90 u`).
  - Layers/tags/components verified: Root & `CollisionCollider` Layer `19` + Tag `"Enemy"` (`isTrigger=true`, `EnemyAICollisionDetect.mainScript` wired), `ScanNode` Layer `22` (`isTrigger=false`, `nodeType=1`), `MapDot` Layer `14` (excluded from `ai.meshRenderers` so `EnableEnemyMesh` never overwrites Layer 14), `NavMeshAgent (radius=0.60, height=3.30, baseOffset=0.0)`, 5 `enemyBehaviourStates`, `Diagetic/SFX` mixer + `AudioLowPassFilter` + `OccludeAudio`, `dieSFX = hitBodySFX = hitEnemyVoiceSFX = deathSFX = null` (per `#15`).

---

## Phase 3 — Final C#, NetcodePatcher IL, Thunderstore Zip & Profile Parity Audit

### 3.1 Final C# Code Review, NetcodePatcher IL (`rea` + `ilspycmd`) & Unit Tests (`#23`) — **PASS**

#### Findings & Resolution Status (Re-verified after `#23`)

1. **`[P1]` RESOLVED — Fix aggro roar never playing on Host and permanently locking out after first roar on remote clients — `Plugin/src/AI/MahoragaAI.cs:783-790, 1337-1361, 1504-1525`**  
   *Fixed in `#23` and verified in source + decompiled final zipped DLL:* `SwitchBehaviourAndSyncServer` now computes `bool playRoar` (`!wasChasing && newState == MahoragaState.Chasing && !isEnemyDead && _roarCooldownTimer <= 0f`) and resets `_roarCooldownTimer = RoarCooldownSeconds` *before* mutating `currentBehaviourStateIndex`, passing `playRoar` into `SyncBehaviourStateClientRpc(int newState, int targetPlayerClientId, bool playRoar)`. `SetChaseTargetServer` passes `playRoar: false`. Both Host and remote clients execute `audioController?.PlayRoar()` in lockstep with zero client timer lockout.

2. **`[P2]` RESOLVED — Change `ExplosionTrackingPatch` hooks from `[HarmonyPostfix]` to `[HarmonyPrefix]` — `Plugin/src/Patches/ExplosionTrackingPatch.cs:23-52`**  
   *Fixed in `#23` and verified in source + decompiled final zipped DLL:* Both `OnSpawnExplosion` (`Landmine.SpawnExplosion`) and `OnExplodeStunGrenade` (`StunGrenadeItem.ExplodeStunGrenade`, guarded by `hasExploded`) now use `[HarmonyPrefix]`, populating the ring buffers before synchronous `HitEnemy` / `SetEnemyStunned` callbacks execute inside those methods.

#### Verified NetcodePatcher IL Proof on Final Zipped `Fede.MahoragaLC.dll`
- **Target Inspected:** `BepInEx/plugins/Fede-MahoragaLC/Fede.MahoragaLC.dll` extracted directly from `/home/fede/Escritorio/Mahoraga LC/dist/Fede-MahoragaLC-1.0.0.zip` (`78,848` bytes, SHA-256 `c594187988bd6811b65fc31593ae1d8ed539a682ca3604656ad7fcdf0e42b448`).
- **`rea inspect-managed-artifact` + `rea inspect-managed-members` (`MahoragaLC.AI.MahoragaAI` & `__GEN.NetworkVariableSerializationHelper`):**
  - Assembly `Fede.MahoragaLC v1.0.0.0`, CLR metadata `v4.0.30319`, AnyCPU IL-only PE.
  - Confirmed all 9 RPC methods, `__initializeVariables`, `__initializeRpcs`, and all 9 generated `__rpc_handler_*` static methods in PE metadata.
- **`ilspycmd 11.2.0` Full Decompilation Proof:**
  - `[module: NetcodePatchedAssembly]` present.
  - `__GEN.NetworkVariableSerializationHelper.InitializeSerialization()` (`[RuntimeInitializeOnLoadMethod]`) present and invoked via `Plugin.InitializeNetworkBehaviours()` as step 1 of `Plugin.Awake()` before `NetworkPrefabs.RegisterNetworkPrefab`.
  - **9/9 Custom NGO 1.12.2 RPCs Woven in `__initializeRpcs()`:**
    1. `SyncBehaviourStateClientRpc(int, int, bool)` -> `__registerRpc(283762812u, __rpc_handler_283762812)`
    2. `TriggerAttackClientRpc(bool, bool)` -> `__registerRpc(1306178499u, __rpc_handler_1306178499)`
    3. `ApplyPlayerDamageClientRpc(int, int, int, Vector3)` -> `__registerRpc(1768611705u, __rpc_handler_1768611705)`
    4. `PlayAttackImpactClientRpc(bool, Vector3)` -> `__registerRpc(4156102001u, __rpc_handler_4156102001)`
    5. `TriggerAdaptationClientRpc(byte, int, int, int)` -> `__registerRpc(85398385u, __rpc_handler_85398385)`
    6. `PlayDeflectFeedbackClientRpc(byte)` -> `__registerRpc(764651153u, __rpc_handler_764651153)`
    7. `RequestContactAttackServerRpc(int)` (`RequireOwnership = false`) -> `__registerRpc(2723845614u, __rpc_handler_2723845614)`
    8. `RequestFullSyncServerRpc(ServerRpcParams)` (`RequireOwnership = false`) -> `__registerRpc(2759870303u, __rpc_handler_2759870303)`
    9. `ApplyFullSyncClientRpc(int, int, int, int, bool, ClientRpcParams)` -> `__registerRpc(1412646740u, __rpc_handler_1412646740)`
- **Code & Sync Safety Verification:**
  - Permanent server ownership (`ChangeOwnershipOfEnemy` never called); `ApplyPlayerDamageClientRpc` routes `DamagePlayer` strictly to the hit player's owning client; `HitEnemy` passes `playHitSFX: false` to base and mutates HP only on `IsServer`; `KillEnemy` avoids double-playing `mahoraga_death`; `SetEnemyStunned` + `Update` enforce ZapGun `stunnedIndefinitely = 0` immunity; `DoorLock` locked-door forcing uses `UnlockDoorSyncWithServer()` + `AnimatedObjectTrigger.TriggerAnimationNonPlayer` + `OpenDoorAsEnemyServerRpc()`; `MahoragaMusicCoordinator` enforces single-instance closest alive same-domain selection, spectate-aware `isInsideFactory` isolation, `35m -> 16m` SmoothStep + `AudioLowPassFilter` wall occlusion (`StartOfRound.Instance.collidersAndRoomMaskAndDefault`), 1.0s gong ducking, `~0.33s` death fade-out, and optional `CustomMusic/` override.
- **Unit Tests (`dotnet run --project Plugin/Tests/MahoragaLC.Tests.csproj -c Release`):** **`10 passed, 0 failed`**.

---

### 3.2 Final Thunderstore Package & `MahoragaDev` Profile Parity (`#23` + `#26`) — **PASS**

Unzipped `/home/fede/Escritorio/Mahoraga LC/dist/Fede-MahoragaLC-1.0.0.zip` (`11,515,291` bytes, SHA-256 `2d421f46244a3f9251b6fdc08b8f3319000c72b0383b5e28e7d5364e2bba573d`) into a clean temporary directory and verified:

- **Exact 6 Zip Entries (0 forbidden / `.wav` / `raw_source` / `.pdb` files):**
  1. `manifest.json` — valid JSON (`"namespace": "Fede"`, `"name": "MahoragaLC"`, `"version_number": "1.0.0"`, `"dependencies": ["BepInEx-BepInExPack-5.4.2305", "Evaisa-LethalLib-1.2.0"]`)
  2. `icon.png` — exact `256×256` RGBA PNG, SHA-256 `d67bc9226fdef9d7416f8505bad3f827f348477f5d7f2ab33f457b8c2ede94c7` (byte-identical to `assets/model/renders/icon_256.png`)
  3. `README.md` — `5,369` bytes (Windows Thunderstore Mod Manager, Linux r2modman Proton + `[Logging.Console] Enabled = false` stability tip, Manual install, Config, and `CustomMusic/` override instructions)
  4. `CHANGELOG.md` — `516` bytes
  5. `BepInEx/plugins/Fede-MahoragaLC/Fede.MahoragaLC.dll` — `78,848` bytes, SHA-256 `c594187988bd6811b65fc31593ae1d8ed539a682ca3604656ad7fcdf0e42b448`
  6. `BepInEx/plugins/Fede-MahoragaLC/mahoraga_assets` — `12,335,022` bytes, SHA-256 `8e799c50c59e4dbe71549bde471a4da538fe37d224dfcbef42aa1fdb090d6c4a`
- **Byte-for-Byte Parity (`dist/` Zip == `MahoragaDev` Profile == `UnityProject` Bundle):**
  - `Fede.MahoragaLC.dll`: Zip == `/home/fede/.config/r2modmanPlus-local/LethalCompany/profiles/MahoragaDev/BepInEx/plugins/Fede-MahoragaLC/Fede.MahoragaLC.dll` (**EXACT SHA-256 MATCH**)
  - `mahoraga_assets`: Zip == `/home/fede/.config/r2modmanPlus-local/LethalCompany/profiles/MahoragaDev/BepInEx/plugins/Fede-MahoragaLC/mahoraga_assets` == `UnityProject/AssetBundles/StandaloneWindows64/mahoraga_assets` (**EXACT SHA-256 MATCH**)

---

## Phase 4 — Live In-Game Proton Runtime Verification & Host-Freeze Root-Cause Resolution (`#26`)

### 4.1 Exact Root Cause of the Host -> LAN `StartOfRound.OnEnable` Freeze
- **Evidence Analysis:**
  - In the frozen session (`23:55:42.793`), `BepInEx/LogOutput.log` ended at line 127 (`[Info   : Unity Log] Enabling connection callbacks in StartOfRound`, flushed 682ms later at `23:55:43.475` by `DiskLogListener.FlushTimer`'s 17th 2.000s tick), while line 840 of `StartOfRound.OnEnable()` (`Debug.Log("Began listening to SceneManager_OnLoadComplete1 on this client")`, 3 lines later in the same method with zero loops or method calls in between) never reached `DiskLogListener`, and `Enabling connection callbacks in StartOfRound` was never written to `Player.log` (which ended at line 167: `Color primaries 0 is unknown or unsupported by WindowsMediaFoundation... sharedassets1.resource`).
  - Decompiling `BepInEx.dll` (`Chainloader.Initialize` & `Chainloader.ReplayPreloaderLogs`) proved that at runtime `Logger.Listeners[0]` is `DiskLogListener` (in-memory `StreamWriter` flushed asynchronously every 2000ms) and `Logger.Listeners[1]` is `ConsoleLogListener` (synchronous Win32 console IPC: `Kon.ForegroundColor` -> `GetConsoleScreenBufferInfo(conOut)` + `SetConsoleTextAttribute(conOut)` + `ConsoleOut.Write` with `AutoFlush = true` to `CONOUT$` / `conhost.exe`).
  - Because Thunderstore's `BepInExPack 5.4.2305` defaults to `[Logging.Console] Enabled = true` in `BepInEx.cfg`, `BepInEx` called `AllocConsole()` (spawning a separate Wine `conhost.exe` window) and redirected `STD_OUTPUT_HANDLE`. When `SampleSceneRelay` activated in fullscreen mode concurrently with Wine's `WindowsMediaFoundation` worker thread decoding `sharedassets1.resource`, `ConsoleLogListener.LogEvent` (`Listeners[1]`) blocked the Unity Main Thread on `CONOUT$` right after `DiskLogListener.LogEvent` (`Listeners[0]`) and before `Debug.Log("Enabling connection callbacks in StartOfRound")` could write to `Player.log` or return to line 840 of `StartOfRound.OnEnable()`.
- **Fixes Applied to `MahoragaDev` Profile:**
  1. `BepInEx/config/BepInEx.cfg`: Set `[Logging.Console] Enabled = false` (eliminating `AllocConsole()` / `conhost.exe` and `ConsoleLogListener` main-thread blocking while keeping `[Logging.Disk] Enabled = true`) and `[Logging.Disk] LogLevels = Fatal, Error, Warning, Message, Info` (preventing `Imperium`'s per-frame `[Debug]` Oracle/Network binding spam from thrashing disk I/O).
  2. `mods.yml`: Populated `/home/fede/.config/r2modmanPlus-local/LethalCompany/profiles/MahoragaDev/mods.yml` (previously `[]`) with valid `ManifestV2` entries for all 11 installed packages (including `Fede-MahoragaLC 1.0.0`) and synced `manifest.json`, `icon.png`, `README.md`, and `CHANGELOG.md` into `BepInEx/plugins/Fede-MahoragaLC/` so r2modman's UI displays all installed mods and icons properly.

### 4.2 Live In-Game Execution Proof Under Proton 11 (`Unity 2022.3.62f2`, Lethal Company v81) — **19/19 PASS**
- **Dungeon Generation & Imperium Oracle Live Selection:**
  - Hosted LAN session -> loaded `SampleSceneRelay` (`Enabling connection callbacks in StartOfRound` -> `Began listening to SceneManager_OnLoadComplete1 on this client` -> `StartOfRound.Start` `1107298560`) -> pulled `StartGameLever` -> landed on `41 Experimentation` -> `Dungeon has finished generating on this client after multiple frames` -> `Imperium` Oracle simulated indoor enemy spawns from the 11-enemy pool and selected **`[ORACLE] Picked indoor entity: Mahoraga`**.
- **5-Suite Automated In-Game Runtime Verification (`19/19` Assertions Passed Live in `SampleSceneRelay`):**
  - **Suite 1 (Terminal Bestiary):** `Terminal` found; `MahoragaTerminalNode` registered in `Terminal.enemyFiles` (`creatureFileID = 30`); `MahoragaTerminalKeyword` (`"mahoraga"`) registered in `allKeywords` with `defaultVerb = "info"`.
  - **Suite 2 (Level & ScanNode Wiring):** `MahoragaEnemyType` registered across all `13` levels with non-null `enemyPrefab`; `ScanNodeProperties` wired with `creatureScanID = 30`, `layer = 22` (`ScanNode`), `nodeType = 1` (Danger).
  - **Suite 3 (Live NGO Spawn, HDRP Shader, AudioMixer & Animator):** `MahoragaPrefab` instantiated and spawned via `NetworkObject.Spawn()`; `MahoragaAI.Start()` & `Update()` ran with `0` errors (`enemyHP = 18`, `mainRenderer` shader = `'HDRP/Lit'`, `wheelBone`/`swordBone`/`eye`/`audioController` non-null, `voiceSource.outputAudioMixerGroup` = `'SFX'`, `AnimatorController` active).
  - **Suite 4 (Live Combat & Wheel of Adaptation):**
    - Hit #1 (`hitID = 1` Shovel, `force = 4`): reduced `enemyHP` from `18 -> 14` (`1/2` Melee adaptation counter).
    - Hit #2 (`hitID = 1` Shovel, `force = 4`): reduced `enemyHP` `14 -> 10`, triggered Wheel of Adaptation (`state = 4 Adapting`), healed `+20%` (`+4 HP` -> `enemyHP = 14`).
    - After `2.4s` adaptation window: `WheelBone` rotated exact **`+45.00°`** around local Y hub normal, and `MahoragaAI` exited `Adapting` into `state = 3` (`Attacking`).
    - Hit #3 (`hitID = 1` Shovel, `force = 4` against adapted Melee category): deflected with `95%` damage resistance (`RoundToInt(4 * 0.05) = 0` damage, `enemyHP` remained `14`).
  - **Suite 5 (Live Death Transition):** `KillEnemy(destroy: false)` cleanly transitioned `MahoragaAI` to `isEnemyDead = true` with `Animator` `IsDead = true`.

---

## Overall QA Assessment

- **Final Verdict: PASS (0 open defects across Phases 1–4; live in-game Proton runtime verification 19/19 PASS).**
