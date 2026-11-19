# Mahoraga LC — Master Asset & Integration Contract (`ASSET_CONTRACT.md`)

> **Status:** **FINALIZED v2** — 100% locked and cross-verified against [`docs/RE_FINDINGS.md`](file:///home/fede/Escritorio/Mahoraga%20LC/docs/RE_FINDINGS.md) (`Assembly-CSharp.dll`, `sharedassets3.assets`, `level3`, `globalgamemanagers`, and `LethalLib 1.2.0`).
>
> **Applies to:** `@modeler`, `@audio`, `unity-builder`, `csharp-dev`, QA / Packager.
> **Rule:** Every identifier, numeric constant, layer ID, tag, and path in backticks below is **case-sensitive** and **non-negotiable**.

---

## 1. Assembly, Engine & AssetBundle Contract

| Key | Exact Value | Notes |
|---|---|---|
| **DLL / Assembly Name** | `Fede.MahoragaLC.dll` | Compiled assembly name (`AssemblyName = Fede.MahoragaLC`) |
| **BepInEx Plugin GUID** | `Fede.MahoragaLC` | Used by `BepInPlugin` attribute & config file `Fede.MahoragaLC.cfg` |
| **Root C# Namespace** | `MahoragaLC` | All runtime C# types live under `MahoragaLC.*` |
| **Primary AI Script** | `MahoragaLC.AI.MahoragaAI` | Inherits `EnemyAI`; attached to `MahoragaPrefab` root |
| **Primary Audio Script** | `MahoragaLC.Audio.MahoragaAudioController` | Inherits `MonoBehaviour`; attached to `MahoragaPrefab` root |
| **Config Class** | `MahoragaLC.Config.MahoragaConfig` | BepInEx config wrapper |
| **AssetBundle Filename** | `mahoraga_assets` | Lowercase, **no file extension**. Shipped alongside `Fede.MahoragaLC.dll` |
| **Unity Engine Version** | **`2022.3.62f2`** | Verified from game `globalgamemanagers`; Windows x64 Standalone AssetBundle target |
| **HDRP Package Version** | **`14.0.12`** (`com.unity.render-pipelines.high-definition`) | Matches shipped `Unity.RenderPipelines.HighDefinition.Runtime.dll` |
| **NGO Package Version** | **`1.12.2`** (`com.unity.netcode.gameobjects`) | Matches shipped `Unity.Netcode.Runtime.dll` |
| **AI Navigation Version** | **`1.1.6`** (`com.unity.ai.navigation`) | Matches shipped `Unity.AI.Navigation.dll` |
| **LethalLib Version** | **`1.2.0`** (`Evaisa-LethalLib`) | Installed at `.../MahoragaDev/BepInEx/plugins/Evaisa-LethalLib/LethalLib/LethalLib.dll` |

---

## 2. AssetBundle Contents (`mahoraga_assets`)

All assets below MUST be tagged into the `mahoraga_assets` AssetBundle so `csharp-dev` loads them by exact asset name:

| Asset Type | Exact Asset Name | Recommended Unity Project Path | Required By |
|---|---|---|---|
| Prefab (`GameObject`) | `MahoragaPrefab` | `Assets/Mahoraga/Prefabs/MahoragaPrefab.prefab` | `MahoragaEnemyType.enemyPrefab`, `NetworkPrefabs.RegisterNetworkPrefab` |
| ScriptableObject (`EnemyType`) | `MahoragaEnemyType` | `Assets/Mahoraga/Data/MahoragaEnemyType.asset` | `Enemies.RegisterEnemy` |
| ScriptableObject (`TerminalNode`) | `MahoragaTerminalNode` | `Assets/Mahoraga/Data/MahoragaTerminalNode.asset` | Bestiary display entry |
| ScriptableObject (`TerminalKeyword`) | `MahoragaTerminalKeyword` | `Assets/Mahoraga/Data/MahoragaTerminalKeyword.asset` | Terminal noun `"mahoraga"` |
| AnimatorController | `MahoragaAnimator` | `Assets/Mahoraga/Animations/MahoragaAnimator.controller` | `MahoragaPrefab` Animator |
| Material (`HDRP/Lit`) | `Mahoraga_Mat` | `Assets/Mahoraga/Materials/Mahoraga_Mat.mat` | `SkinnedMeshRenderer` on `MahoragaPrefab` |
| AudioClip | `mahoraga_music_main` | `Assets/Mahoraga/Audio/mahoraga_music_main.ogg` | `MahoragaAudioController` / `MahoragaMusicCoordinator` |
| AudioClip | `mahoraga_wheel_turn` | `Assets/Mahoraga/Audio/mahoraga_wheel_turn.ogg` | `MahoragaAudioController` |
| AudioClip | `mahoraga_footstep_1` | `Assets/Mahoraga/Audio/mahoraga_footstep_1.ogg` | `MahoragaAudioController` |
| AudioClip | `mahoraga_footstep_2` | `Assets/Mahoraga/Audio/mahoraga_footstep_2.ogg` | `MahoragaAudioController` |
| AudioClip | `mahoraga_slash` | `Assets/Mahoraga/Audio/mahoraga_slash.ogg` | `MahoragaAudioController` |
| AudioClip | `mahoraga_impact` | `Assets/Mahoraga/Audio/mahoraga_impact.ogg` | `MahoragaAudioController` |
| AudioClip | `mahoraga_roar` | `Assets/Mahoraga/Audio/mahoraga_roar.ogg` | `MahoragaAudioController` |
| AudioClip | `mahoraga_death` | `Assets/Mahoraga/Audio/mahoraga_death.ogg` | `MahoragaAudioController` |

---

## 3. 3D Model, Scale, Orientation & Doorway Contract (`@modeler`)

### 3.1 Source & Export Files
- Blender source: `assets/model/Mahoraga.blend`
- Unity-ready FBX: `assets/model/Mahoraga.fbx`
- FBX Export Settings (Blender 5.2.3 LTS -> Unity `2022.3.62f2`):
  - `use_mesh_modifiers=True`, `add_leaf_bones=False`
  - `apply_unit_scale=True`, `apply_scale_options='FBX_SCALE_ALL'`
  - `axis_forward='-Y'`, `axis_up='Z'` (character facing Blender `-Y` exports cleanly to Unity `+Z` forward, `+Y` up)
  - `bake_anim=True`, `bake_anim_use_all_actions=True`, `bake_anim_use_nla_strips=False`
  - `use_armature_deform_only=False` (ensures every contract bone below is preserved cleanly in the exported skeleton)
- Target Triangle Budget: **15,000 – 35,000 triangles** (organic sculpted game-ready anatomy, clean non-overlapping UVs).
- Object Transforms: All Mesh & Armature transforms applied cleanly (`Location = (0,0,0)`, `Rotation = (0,0,0)`, `Scale = (1,1,1)`).

### 3.2 Exact Height, Ceiling Clearance & Coordinate Calibration (Verified from `RE_FINDINGS.md`)
- **World Origin / Pivot**: Sole of bare feet rests cleanly at ground plane (`Blender Z = 0.00 m` / `Unity Y = 0.00 u`), centered laterally on `X = 0.00`.
- **Forward Facing**: Character faces **Blender `-Y`** (= **Unity `+Z`** forward).
- **Verified Lethal Company Player Height**: **`playerHeight = 2.50 u`** (`CharacterController.height = 2.50 u`, `spine.004_end = 2.510 u`, `ScavengerModel LOD1 top = 2.545 u`, `MainCamera eye = 2.360 u`).
- **Locked Target Standing Height (`mahoragaTargetHeight`)**:
  $$\mathbf{\text{mahoragaTargetHeight} = 1.5 \times 2.50\text{ u} = 3.75\text{ m (Unity units)}}$$
  - Standing **feet soles (`Z = 0.00`) to crown of elongated skull = exact `3.75 m`** at prefab scale `(1, 1, 1)` (places Mahoraga right between Coil-Head `3.595 u` and Nutcracker `3.903 u`).
  - At `3.75 u`, a standing player's first-person eyes (`Y = 2.36 u`) look straight into Mahoraga's lower chest / solar plexus (`~63%` of body height) and sharply upward at the eyeless 4-winged skull (`Y = 3.25–3.75 u`).
- **CRITICAL Dharma Wheel Ceiling Clearance (`<= 3.90 m`)**:
  - Standard Facility hallways (`HallwayTileTypeA/B/C/D`) have a measured ceiling height of **`3.916 u`**!
  - Therefore, position the floating Eight-Handled Dharma Wheel **behind the upper back / nape centered around `Z = 3.25–3.35 m` (outer rim radius `~0.50–0.55 m`)** so the **highest point of the wheel / top spoke sphere stays `<= 3.90 m`** (strictly clearing `3.916 u` factory hallway ceilings with zero clipping while remaining unmistakably iconic above/behind the shoulders!).
- **Doorway Visual Clearance (`1.51–1.64 W × 3.10–3.27 H` Doors)**:
  - Keep shoulder width visually compact (`~1.05–1.25 m`) for `3.75 m` heroic proportions so Mahoraga reads naturally striding through `1.51–1.64 u` doors.
  - Give `Walk` and `Run` a predatory forward torso hunch (`~10–15°`, lowering skull crown by `~0.20–0.30 m` during locomotion) so his head clears `3.10–3.22 u` doorway lintels gracefully while towering at the full **`3.75 m`** in `Idle`, `Attack_*`, and `Adapt_WheelSpin`!

### 3.3 Required Bone Hierarchy (Exact Names — Generic Rig)

Every bone name below MUST exist verbatim in `Mahoraga.fbx`. Unity FBX Rig Import Type MUST be **Generic** (NOT Humanoid).

```text
Armature
└── Hips
    ├── Spine
    │   └── Chest
    │       ├── Neck
    │       │   └── Head
    │       │       ├── Wing_UL          (Upper Left eye-socket wing)
    │       │       ├── Wing_UR          (Upper Right eye-socket wing)
    │       │       ├── Wing_LL          (Lower Left eye-socket wing)
    │       │       ├── Wing_LR          (Lower Right eye-socket wing)
    │       │       └── HeadTail_1       (Occipital crest/tail root)
    │       │           └── HeadTail_2
    │       │               └── HeadTail_3
    │       │                   └── HeadTail_4
    │       ├── WheelBone                (Floating 8-spoked Dharma Wheel pivot)
    │       ├── Shoulder_L
    │       │   └── UpperArm_L
    │       │       └── Forearm_L
    │       │           └── Hand_L
    │       └── Shoulder_R
    │           └── UpperArm_R
    │               └── Forearm_R
    │                   ├── Hand_R
    │                   └── SwordBone    (Sword of Extermination — bandaged to right forearm)
    ├── Thigh_L
    │   └── Shin_L
    │       └── Foot_L
    │           └── Toe_L
    └── Thigh_R
        └── Shin_R
            └── Foot_R
                └── Toe_R
```

#### Critical Bone & Weight-Painting Rules
1. **`WheelBone` Rotation Axis Contract (CRITICAL)**:
   - Place `WheelBone` head at the exact geometric center of the Eight-Handled Dharma Wheel (`Z ≈ 3.25–3.35 m`, floating cleanly behind upper back/nape, max top vertex `<= 3.90 m`).
   - Orient `WheelBone` so its **local Y axis** (Blender bone head -> tail longitudinal vector) is **strictly perpendicular to the plane of the wheel ring** (pointing along the hub normal).
   - Rotating `WheelBone` by **`+45.0°` around local Y** (`localRotation *= Quaternion.Euler(0f, 45f, 0f)`) spins the 8-spoked wheel cleanly in-plane by exactly 1 spoke (`360° / 8 = 45°`) with zero wobble or clipping.
   - **Exclusive Skinning**: 100% of Dharma Wheel mesh vertices MUST be weighted `1.0` exclusively to `WheelBone` (`0.0` to all other bones).
2. **`SwordBone` Skinning**:
   - Parented to `Forearm_R` so the Sword of Extermination remains cleanly strapped along the bandaged right forearm extending past the clenched right fist.
   - 100% of blade + ornate guard vertices weighted `1.0` to `SwordBone`.
3. **`Wing_UL`, `Wing_UR`, `Wing_LL`, `Wing_LR`**:
   - Parented to `Head`, rooted cleanly at eye sockets/temples.
4. **`HeadTail_1` .. `HeadTail_4`**:
   - Continuous chain sweeping down behind skull/back; must clear the Dharma Wheel across all 8 clips.
5. **Hakama & Obi Sash**:
   - Smooth weight painting across `Hips`, `Thigh_L/R`, and `Shin_L/R` with zero leg clipping during wide `Run` / `Attack_*` strides.

---

## 4. Animation Clips & Animator Contract

### 4.1 Required In-Place Animation Clips (Exact Names — 8 Clips)

All 8 clips MUST be exported inside `Mahoraga.fbx`, baked at **30 FPS**, and strictly **IN-PLACE** (zero net X/Z root translation on `Armature` or `Hips`; `Animator.applyRootMotion = false`).

| Exact Clip Name | Loop | Target Duration | Key Timing & Motion Requirements |
|---|---|---|---|
| `Idle` | Yes | `2.5s – 3.0s` | Full `3.75 m` towering breathing heave, subtle shoulder/chest sway, gentle wheel hover, subtle wing tremor & tail drift. |
| `Walk` | Yes | `1.2s – 1.4s` | Heavy stalker stride with slight `~10°` forward torso hunch (doorway-friendly); distinct footfalls at ~25% and ~75%. |
| `Run` | Yes | `0.75s – 0.85s` | Aggressive predatory charge (`~15°` forward hunch); right arm draws `SwordBone` ready to cleave; heavy footfalls at ~25% and ~75%. |
| `Attack_Cleave` | No | `1.15s` | Right-arm Sword of Extermination wide horizontal/diagonal slash. Windup `0.00s–0.38s`, strike apex at **`t ≈ 0.38s`**, recovery `0.55s–1.15s`. |
| `Attack_Slam` | No | `1.45s` | Overhead/crushing downward slam. Windup `0.00s–0.55s`, impact smash at **`t ≈ 0.55s`**, recovery `0.75s–1.45s`. |
| `Adapt_WheelSpin` | No | `2.20s` | Dramatic planted bracing stance, skull tilts upward, all 4 eye-wings flare wide, **`WheelBone` rotates +45° around local Y** smoothly between `t ≈ 0.25s` and `t ≈ 1.05s` synced with `mahoraga_wheel_turn`. |
| `Stun` | Yes | `1.00s` | Recoiling/hunched shuddering stance during Zap Gun or Flashbang before adaptation, wings twitching. |
| `Death` | No | `2.20s` | Knees buckle, Sword drops, torso collapses cleanly to ground, wings fold, Dharma Wheel tilts & settles. Holds final frame cleanly. |

> **Cumulative Wheel Persistence (`LateUpdate`)**: `MahoragaAI.LateUpdate()` sets `WheelBone.localRotation` relative to its cached bind rest rotation using the synced cumulative angle (`wheelStepCount * 45f`, smoothly eased from `(wheelStepCount - 1) * 45f` to `wheelStepCount * 45f` during `Adapt_WheelSpin`) so the wheel permanently stays rotated at `45°, 90°, 135°... 360°` after blending back to `Idle`/`Run`.

### 4.2 `MahoragaAnimator` Controller Parameters (Exact Names & Types)

| Exact Parameter Name | Type | Default | Driven By (`MahoragaAI`) |
|---|---|---|---|
| `Speed` | `Float` | `0.0` | Horizontal speed in m/s (`agent.velocity.magnitude` on server, smoothed displacement velocity on clients) |
| `IsChasing` | `Bool` | `false` | `true` in `Chasing` or `Attacking` |
| `IsStunned` | `Bool` | `false` | `true` when `(stunNormalizedTimer > 0f || stunnedIndefinitely > 0) && !isEnemyDead && !isAdapting` |
| `IsDead` | `Bool` | `false` | `true` permanently after `KillEnemy()` |
| `AttackCleave` | `Trigger` | — | Fired on entering `Attacking` (Cleave) or door-forcing strike |
| `AttackSlam` | `Trigger` | — | Fired on entering `Attacking` (Slam) |
| `Adapt` | `Trigger` | — | Fired on entering `Adapting` |

### 4.3 `MahoragaAnimator` State Graph Transitions
- **Default Entry State**: `Idle`
- **Locomotion transitions**:
  - `Idle` -> `Walk`: `Speed > 0.25` and `IsChasing == false` (`0.15s`)
  - `Walk` -> `Idle`: `Speed <= 0.20` (`0.15s`)
  - `Idle` / `Walk` -> `Run`: `IsChasing == true` and `Speed > 0.40` (or `Speed >= 4.8`) (`0.12s`)
  - `Run` -> `Walk`: `IsChasing == false` and `Speed > 0.25` (`0.15s`)
  - `Run` -> `Idle`: `Speed <= 0.20` (`0.15s`)
- **Any State transitions** (`Can Transition To Self = false`, priority order):
  1. `Any State` -> `Death`: `IsDead == true` (`0.10s`, terminal — no exit)
  2. `Any State` -> `Adapt_WheelSpin`: `Adapt` trigger and `IsDead == false` (`0.10s`; auto-exit at `Exit Time = 0.95` to `Idle`)
  3. `Any State` -> `Stun`: `IsStunned == true` and `IsDead == false` (`0.10s`; exit to `Idle` when `IsStunned == false`)
  4. `Any State` -> `Attack_Cleave`: `AttackCleave` trigger and `!IsDead && !IsStunned` (`0.08s`; auto-exit `0.95` to `Idle`)
  5. `Any State` -> `Attack_Slam`: `AttackSlam` trigger and `!IsDead && !IsStunned` (`0.08s`; auto-exit `0.95` to `Idle`)

---

## 5. Textures & HDRP `14.0.12` Material Contract

### 5.1 Required Texture Files (`assets/model/textures/`, all `2048x2048` PNG)

| Exact Filename | Color Space | HDRP/Lit Property | Notes |
|---|---|---|---|
| `Mahoraga_Albedo.png` | **sRGB** | `_BaseColorMap` | Alabaster/bone skin, dark charcoal pleated hakama & obi, dark forearm bandages, antique gold Dharma wheel, metallic/ivory Sword of Extermination |
| `Mahoraga_Normal.png` | **Linear** (Normal Map) | `_NormalMap` (**OpenGL Y+**) | Muscle anatomy, wing blades/feathers, hakama pleats, wheel details |
| `Mahoraga_Metallic.png` | **Linear** | Source grayscale | Metallic = 1.0 on gold wheel & blade/guard; 0.0 on skin/cloth |
| `Mahoraga_Roughness.png` | **Linear** | Source grayscale | Matte skin/cloth (`0.65–0.85`), brushed gold/steel (`0.22–0.38`) |
| `Mahoraga_Emissive.png` | **sRGB** | `_EmissiveColorMap` | **Pure `#000000` everywhere EXCEPT Dharma Wheel golden regions (`#FFD060`) & optional subtle sword runes**, so driving `_EmissiveColor` HDR intensity flashes ONLY the Wheel golden during `Adapting`! |
| `Mahoraga_MaskMap.png` | **Linear** | `_MaskMap` (RGBA packed) | **`R` = Metallic**, **`G` = Ambient Occlusion**, **`B` = Detail Mask (`128`)**, **`A` = Smoothness (`1.0 - Roughness`)** |

### 5.2 HDRP/Lit Material (`Mahoraga_Mat`)
- Shader: **`HDRP/Lit`** (`HDRP 14.0.12`).
- Bind `_BaseColorMap`, `_MaskMap`, `_NormalMap`, `_EmissiveColorMap`.
- Enable Emission (`_UseEmissiveIntensity = true`); subtle idle bronze-gold glow (`#FFB830`, `~0.4` nits), runtime pulsed to radiant gold (`~60` nits) via `MaterialPropertyBlock` (`_EmissiveColor`) on `TriggerAdaptationClientRpc`.

### 5.3 Mandatory QA Renders & Thunderstore Icon (`assets/model/renders/`)
- `front.png`, `three_quarter.png`, `side.png`, `back.png`, `closeup_head_wheel.png`, `anim_attack_pose.png`, `scale_comparison_1_5x.png` (standing next to a labeled **`2.50 m`** Lethal Company player silhouette vs **`3.75 m`** Mahoraga!).
- `icon_256.png` (`256×256` RGBA PNG Thunderstore mod icon).

---

## 6. Audio Asset Contract (`@audio`)

All 8 `.ogg` files live in `assets/audio/` (and are imported into `UnityProject/Assets/Mahoraga/Audio/`). Raw downloads stay strictly inside `assets/audio/source/`.

| Exact Clip Name | File Path | Verified Duration & Format | Channels | Unity Import Settings | Role |
|---|---|---|---|---|---|
| `mahoraga_music_main` | `assets/audio/mahoraga_music_main.ogg` | `130.219s`, Vorbis q6, 44.1 kHz, `-14.00 LUFS`, `-5.30 dBTP` | **Stereo** | `ForceToMono = false`, `LoadType = CompressedInMemory` (or `Streaming`) | Trimmed strictly from `00:00:51.000` (FFT-verified), seamless loop; played by singleton `MahoragaMusicCoordinator` |
| `mahoraga_wheel_turn` | `assets/audio/mahoraga_wheel_turn.ogg` | `2.199s`, Vorbis q6, 44.1 kHz, `-13.90 LUFS` | Mono | `ForceToMono = true`, `DecompressOnLoad` | Metallic ratchet + resonant bronze temple gong (`45m` 3D; exact match to `2.20s` `Adapting` state) |
| `mahoraga_footstep_1` | `assets/audio/mahoraga_footstep_1.ogg` | `0.480s`, Vorbis q6, 44.1 kHz, `-16.20 LUFS` | Mono | `ForceToMono = true`, `DecompressOnLoad` | Heavy bare-foot thud #1 (`30m` 3D) |
| `mahoraga_footstep_2` | `assets/audio/mahoraga_footstep_2.ogg` | `0.518s`, Vorbis q6, 44.1 kHz, `-18.50 LUFS` | Mono | `ForceToMono = true`, `DecompressOnLoad` | Heavy bare-foot thud #2 (`30m` 3D) |
| `mahoraga_slash` | `assets/audio/mahoraga_slash.ogg` | `0.780s`, Vorbis q6, 44.1 kHz, `-11.90 LUFS` | Mono | `ForceToMono = true`, `DecompressOnLoad` | Sword of Extermination heavy cleave whoosh + metallic ring (`30m` 3D) |
| `mahoraga_impact` | `assets/audio/mahoraga_impact.ogg` | `0.742s`, Vorbis q6, 44.1 kHz, `-17.30 LUFS` | Mono | `ForceToMono = true`, `DecompressOnLoad` | Devastating slam / hit / locked-door bash crunch (`35m` 3D) |
| `mahoraga_roar` | `assets/audio/mahoraga_roar.ogg` | `2.048s`, Vorbis q6, 44.1 kHz, `-11.90 LUFS` | Mono | `ForceToMono = true`, `CompressedInMemory` | Deep inhuman shikigami aggro roar (`45m` 3D) |
| `mahoraga_death` | `assets/audio/mahoraga_death.ogg` | `2.480s`, Vorbis q6, 44.1 kHz, `-13.80 LUFS` | Mono | `ForceToMono = true`, `CompressedInMemory` | Dark dissipating collapse rumble (`45m` 3D) |

> *Mixer Routing Rule (`unity-builder` + `csharp-dev`):* Assign `VoiceSFXSource` and `BodySFXSource` to the `Diagetic` AudioMixer group (`tools/LC-ExampleEnemy-ref/UnityProject/Assets/AudioMixerController/Diagetic.mixer`), and call `LethalLib.Modules.Utilities.FixMixerGroups(MahoragaPrefab)` in `Plugin.Awake()`!

---

## 7. `MahoragaPrefab` Hierarchy, Verified Layers & Serialized Fields

### 7.1 Verified Unity Layer & Tag Table (from `globalgamemanagers`)

| Layer Name | Exact Layer ID | Bitmask | Usage on `MahoragaPrefab` / Runtime |
|---|---:|---:|---|
| **`Player`** | **`3`** | `1 << 3` | Player hit/proximity queries |
| **`Room`** | **`8`** | `1 << 8` | Facility geometry / wall occlusion |
| **`InteractableObject`** | **`9`** | `1 << 9` | `DoorLock` proximity scanning |
| **`MapRadar`** | **`14`** | `1 << 14` | `MapDot` child GameObject (ship radar visibility) |
| **`LineOfSight`** | **`18`** | `1 << 18` | Sight blocking volumes |
| **`Enemies`** | **`19`** | `1 << 19` (`524288`) | **MANDATORY** on `MahoragaPrefab` root AND `CollisionCollider` (`ShotgunItem`, `Shovel`, `Landmine`, `StunGrenadeItem` query Layer 19!) |
| **`ScanNode`** | **`22`** | `1 << 22` | `ScanNode` child GameObject |
| **`EnemiesNotRendered`** | **`23`** | `1 << 23` | Reference only (do NOT put main renderer here) |
| **Wall Occlusion Mask** | — | `StartOfRound.Instance.collidersAndRoomMaskAndDefault` | Verified exact mask used by `EnemyAI` & `NutcrackerEnemyAI` (`QueryTriggerInteraction.Ignore`) |

> **MANDATORY TAG RULE**: Both `MahoragaPrefab` root AND `CollisionCollider` MUST have **`Tag = "Enemy"`** and **`Layer = 19 (Enemies)`**. `DoorLock.OnTriggerStay` strictly checks `other.CompareTag("Enemy")`!

### 7.2 Exact Prefab GameObject Hierarchy

```text
MahoragaPrefab                          [Layer: 19 (Enemies), Tag: "Enemy"]
├── Components on Root:
│   ├── NetworkObject                   (Unity.Netcode.NetworkObject — NGO 1.12.2)
│   ├── NavMeshAgent                    (agentTypeID=0 Humanoid, radius=0.60, height=3.30, baseOffset=0.0,
│   │                                    speed=3.2, angularSpeed=360, acceleration=45, stoppingDistance=0.2,
│   │                                    obstacleAvoidanceType=LowQualityObstacleAvoidance)
│   ├── MahoragaAI                      (MahoragaLC.AI.MahoragaAI : EnemyAI)
│   └── MahoragaAudioController         (MahoragaLC.Audio.MahoragaAudioController)
├── ModelContainer                      (Position=(0,0,0), Rotation=(0,0,0), Scale=(1,1,1))
│   └── Mahoraga                        (Instantiated Mahoraga.fbx — standing skull crown Y=3.75 u)
│       ├── Animator                    (Controller=MahoragaAnimator, ApplyRootMotion=false, CullingMode=AlwaysAnimate)
│       ├── SkinnedMeshRenderer         (Material=Mahoraga_Mat, updateWhenOffscreen=true)
│       └── Armature -> ... -> WheelBone
│           └── WheelGlowLight          (Optional Point Light + HDAdditionalLightData, Color=#FFB830, Intensity=0 at idle)
├── EyeTransform                        (Position=(0, 3.30, 0.35), Rotation=(0,0,0) — assigned to EnemyAI.eye)
├── CollisionCollider                   [Layer: 19 (Enemies), Tag: "Enemy"]
│   ├── BoxCollider                     (isTrigger=true, center=(0, 1.80, 0), size=(1.10, 3.60, 1.00))
│   ├── Rigidbody                       (isKinematic=true, useGravity=false)
│   └── EnemyAICollisionDetect          (mainScript=MahoragaAI, canCollideWithBeans=false)
├── ScanNode                            [Layer: 22 (ScanNode), Tag: "Untagged"]
│   ├── BoxCollider                     (isTrigger=false [VERIFIED vanilla LC], center=(0, 2.20, 0), size=(1.00, 1.80, 1.00))
│   └── ScanNodeProperties              (headerText="Mahoraga", subText="Divine General", nodeType=1,
│                                        minRange=2, maxRange=18, requiresLineOfSight=true, creatureScanID=-1)
├── MapDot                              [Layer: 14 (MapRadar)]
│   └── MeshFilter + MeshRenderer       (Sphere/Dot mesh, localPos=(0, 0.5, 0), localScale=(1.3, 0.5, 1.3), HDRP/Unlit Red)
├── VoiceSFXSource                      (AudioSource: spatialBlend=1.0, minDistance=4, maxDistance=45, Logarithmic, Diagetic mixer, playOnAwake=false)
└── BodySFXSource                       (AudioSource: spatialBlend=1.0, minDistance=2.5, maxDistance=30, Logarithmic, Diagetic mixer, playOnAwake=false)
```

### 7.3 Exact Serialized Fields on `MahoragaAI` & `MahoragaAudioController`

#### `MahoragaLC.AI.MahoragaAI` (`: EnemyAI`)
- Inherited `EnemyAI` fields wired on prefab:
  - `enemyType = MahoragaEnemyType`
  - `creatureAnimator = Animator`
  - `creatureVoice = VoiceSFXSource`
  - `creatureSFX = BodySFXSource`
  - `dieSFX = null` *(CRITICAL: `EnemyAI.KillEnemy` plays `dieSFX` on `creatureVoice`; keep `null` so `MahoragaAudioController.OnMahoragaDied()` is the single owner of `mahoraga_death` with zero double-playback!)*
  - `agent = NavMeshAgent`
  - `eye = EyeTransform`
  - `thisNetworkObject = NetworkObject`
  - `skinnedMeshRenderers = [SkinnedMeshRenderer]`
  - **`enemyBehaviourStates`** = **5 populated `EnemyBehaviourState` entries** (`[0]=Patrolling`, `[1]=Investigating`, `[2]=Chasing`, `[3]=Attacking`, `[4]=Adapting` — **MANDATORY**: `< 5` entries causes `IndexOutOfRangeException` in `EnemyAI.SwitchToBehaviourStateOnLocalClient`!)
  - `enemyHP = 18` (overridden at runtime from `MahoragaConfig.MaxHP`)
  - `AIIntervalTime = 0.2f`, `syncMovementSpeed = 0.22f`
- Additional `[SerializeField]` fields on `MahoragaAI`:
  - `public Transform wheelBone;` — bound to `Armature/.../WheelBone` (runtime fallback: recursive search `"WheelBone"`)
  - `public Transform swordBone;` — bound to `Armature/.../SwordBone` (runtime fallback: recursive search `"SwordBone"`)
  - `public Light wheelGlowLight;` — bound to `WheelGlowLight` (optional; null-safe)
  - `public MahoragaAudioController audioController;` — bound to `MahoragaAudioController` on root
  - `public Renderer mainRenderer;` — bound to `SkinnedMeshRenderer` (for HDR emissive flash)

#### `MahoragaLC.Audio.MahoragaAudioController` (`: MonoBehaviour`)
- `public AudioSource voiceSource;` — bound to `VoiceSFXSource`
- `public AudioSource bodySource;` — bound to `BodySFXSource`
- `public AudioClip musicMainClip;` — bound to `mahoraga_music_main`
- `public AudioClip wheelTurnClip;` — bound to `mahoraga_wheel_turn`
- `public AudioClip footstep1Clip;` — bound to `mahoraga_footstep_1`
- `public AudioClip footstep2Clip;` — bound to `mahoraga_footstep_2`
- `public AudioClip slashClip;` — bound to `mahoraga_slash`
- `public AudioClip impactClip;` — bound to `mahoraga_impact`
- `public AudioClip roarClip;` — bound to `mahoraga_roar`
- `public AudioClip deathClip;` — bound to `mahoraga_death`

*(Defense-in-depth: `Awake()`/`Start()` also resolve any null field by child name or cached AssetBundle clips.)*

---

## 8. `MahoragaEnemyType` Default Asset Settings

| Field (`EnemyType`) | Default Value | Notes |
|---|---|---|
| `enemyName` | `"Mahoraga"` | Display & internal name |
| `enemyPrefab` | `MahoragaPrefab` | Networked prefab |
| `PowerLevel` | `3f` | Overridden by `MahoragaConfig.PowerLevel` |
| `MaxCount` | `1` | Overridden by `MahoragaConfig.MaxCount` |
| `isOutsideEnemy` | `false` | Optional cloned outside registration via `AllowOutdoorSpawn` |
| `isDaytimeEnemy` | `false` | Night/interior apex enemy |
| `canDie` | `true` | Killable via diverse lethal burst |
| `canBeDestroyed` | `false` | Preserves corpse during `Death` clip |
| `destroyOnDeath` | `false` | Keeps collapsed body cleanly visible |
| `canBeStunned` | `true` | Negated per-category once adapted to `ZapGun`/`StunGrenade` |
| `canSeeThroughFog` | `true` | Eyeless anomalous perception |
| `disableAnimatorWhenFar` | `false` | Prevents frozen poses on long corridors |
| `stunTimeMultiplier` | `1.0f` | Balanced base stun duration before adaptation |
| `doorSpeedMultiplier` | `1.5f` | Opens unlocked doors briskly via `DoorLock.OnTriggerStay`; locked doors forced by `MahoragaAI` |
| `probabilityCurve` | Flat `1.0` (`(0,1) -> (1,1)`) | Consistent threat throughout shift |
| `numberSpawnedFalloff` | `(0,1) -> (1,0)` | Paired with `MaxCount = 1` |
| `useNumberSpawnedFalloff` | `true` | Standard LC spawn falloff |
| `spawningDisabled` | `false` | Controlled by `EnableMod` |
| `pushPlayerForce` | `0.25f` | Heavy physical presence |
| `pushPlayerDistance` | `1.40f` | Keeps players from clipping through torso |
| `hitBodySFX` | `null` | Keep `null` (and pass `playHitSFX: false` to `base.HitEnemy`) to avoid `EnemyAI.cs:2445-2454` unguarded `hitEnemyVoiceSFX` null-clip warning; `MahoragaAudioController` owns hit/deflect SFX cleanly |
| `hitEnemyVoiceSFX` | `null` | Unused (`MahoragaAudioController` owns combat vocal/impact SFX) |
| `deathSFX` | `null` | Unused (`MahoragaAudioController.OnMahoragaDied()` is the single owner of `mahoraga_death`) |

---

## 9. Cross-Team Verification Checklist

- [x] `@env-re`: `docs/RE_FINDINGS.md` verified (`playerHeight = 2.50 u`, `mahoragaTargetHeight = 3.75 u`, layers `3/8/9/14/18/19/22/23`, NavMeshAgent `r=0.60, h=3.30`, Unity `2022.3.62f2`, HDRP `14.0.12`, NGO `1.12.2`, LethalLib `1.2.0`).
- [x] `@design-arch`: All 4 design & contract docs finalized with zero `[PENDING]` placeholders.
- [ ] `@modeler`: Lock stature to exact **`3.75 m`** feet-to-skull-crown (Dharma Wheel top **`<= 3.90 m`** for `3.916 u` hallways!), exact bone names (`WheelBone` local Y hub normal), 8 in-place clips (with `~10–15°` forward lean in `Walk`/`Run`), 6 `2048×2048` textures (`Mahoraga_MaskMap.png` + wheel-masked `Mahoraga_Emissive.png`), 7 QA renders (`scale_comparison_1_5x.png` vs `2.50 m` player!) + `icon_256.png`.
- [x] `@audio`: All 8 verified `.ogg` clips (`mahoraga_music_main.ogg` trimmed from `00:00:51.000`, `130.219s`) + `AUDIO_MANIFEST.md` delivered (`#8`).
- [ ] `unity-builder`: Target Unity `2022.3.62f2` + HDRP `14.0.12` + NGO `1.12.2` + AI.Navigation `1.1.6`; wire Layers (`19`, `22`, `14`), Tag `"Enemy"` on root & `CollisionCollider`, `ScanNode` `isTrigger = false`, 5 `enemyBehaviourStates`, `Diagetic` mixer; export Windows x64 bundle `mahoraga_assets`.
- [ ] `csharp-dev`: Build NetcodePatcher-woven `Fede.MahoragaLC.dll`, keep ownership strictly on Server (`IsServer`), route player damage via `ApplyPlayerDamageClientRpc` (owning-client `DamagePlayer`), handle `ShotgunItem` `hitID = -1`, `HitFromExplosion`, `stunnedIndefinitely = 0` on ZapGun immunity, `DoorLock.UnlockDoorSyncWithServer()`, and LethalLib `FixMixerGroups` + Overload 3 `RegisterEnemy`.
