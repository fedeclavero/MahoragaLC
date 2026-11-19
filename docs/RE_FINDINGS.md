# Lethal Company Reverse-Engineering & Environment Setup Findings (`Mahoraga LC`)

> **Authoritative Source of Truth** produced by `env-re` from the live Steam installation at
> `/mnt/HDD_1TB/SteamLibrary/steamapps/common/Lethal Company/Lethal Company_Data/`
> (stale `~/.steam/` copies ignored), `LethalLib 1.2.0`, and Unity `2022.3.62f2`.
> Raw decompilations (`ilspycmd 11.2.0.10050`), authenticated PE/CLI metadata (`rea 6.8.0`), and Unity batchmode logs are archived under [`docs/re/`](file:///home/fede/Escritorio/Mahoraga%20LC/docs/re/).

---

## 1. Environment & Toolchain Verification

### 1.1 Project Skeleton
Created under `/home/fede/Escritorio/Mahoraga LC/`:
- `docs/re/`, `tools/`, `assets/model/textures/`, `assets/model/renders/`, `assets/audio/source/`, `UnityProject/`, `Plugin/`, `dist/`
- Read-only reference template cloned to: `/home/fede/Escritorio/Mahoraga LC/tools/LC-ExampleEnemy-ref` (`https://github.com/Hamunii/LC-ExampleEnemy`)

### 1.2 Unity 2022.3.62f2 + Windows Build Support (Mono) + Batchmode Verification
- **Game Engine Version (verified from `globalgamemanagers`)**: `2022.3.62f2` (`7670c08855a9`)
- **Unity Editor Path**: `/home/fede/Unity/2022.3.62f2/Editor/Unity`
- **Windows Build Support (Mono)**: Extracted from `UnitySetup-Windows-Mono-Support-for-Editor-2022.3.62f2.pkg` into:
  `/home/fede/Unity/2022.3.62f2/Editor/Data/PlaybackEngines/WindowsStandaloneSupport`
  - Verified layout matches `LinuxStandaloneSupport`: contains `Variations/` (`win64_player_nondevelopment_mono`, `win64_player_development_mono`, `win32_*`, `mono`), `UnityEditor.WindowsStandalone.Extensions.dll`, `WinPlayerBuildProgram.exe`, `Bee/`, `modules.asset`.
- **License & Shared Library Fixes Applied (User-Space)**:
  - Symlinked Flatpak UnityHub licenses & config (`~/.var/app/com.unity.UnityHub/config/unity3d/Unity/{licenses,config}`) into both `~/.local/share/unity3d/Unity/` and `~/.config/unity3d/Unity/`.
  - Host system ships `libxml2.so.16`, while Unity `2022.3.62f2` links `libxml2.so.2` with ELF `RUNPATH = [$ORIGIN/Data/Tools:$ORIGIN]`. Symlinked `~/.local/share/flatpak/app/com.unity.UnityHub/x86_64/stable/active/files/lib/libxml2.so.2` -> `/home/fede/Unity/2022.3.62f2/Editor/Data/Tools/libxml2.so.2`.
- **Batchmode Smoke Test Result**:
  - Command: `/home/fede/Unity/2022.3.62f2/Editor/Unity -batchmode -nographics -quit -logFile -`
  - **Exit Code**: `0` (no `LD_LIBRARY_PATH` required!)
  - Verified log lines (`docs/re/unity_batchmode_verified.log`):
    - `[Licensing::Client] Successfully resolved entitlement details`
    - `Register platform support module: /home/fede/Unity/2022.3.62f2/Editor/Data/PlaybackEngines/WindowsStandaloneSupport/UnityEditor.WindowsStandalone.Extensions.dll`
    - `Exiting batchmode successfully now!`

### 1.3 r2modman & `MahoragaDev` Profile
- **r2modman v3.2.21 AppImage**: `/home/fede/.local/bin/r2modman` (`chmod +x`)
- **Desktop Entry**: `/home/fede/.local/share/applications/r2modman.desktop`
- **Dev Profile Root**: `/home/fede/.config/r2modmanPlus-local/LethalCompany/profiles/MahoragaDev/`
- **Exact `LethalLib.dll` Path**:
  `/home/fede/.config/r2modmanPlus-local/LethalCompany/profiles/MahoragaDev/BepInEx/plugins/Evaisa-LethalLib/LethalLib/LethalLib.dll`
- **Installed Thunderstore Packages (all direct + transitive dependencies)**:

| Package | Version | Installed Location in `MahoragaDev` |
|---|---|---|
| `BepInEx-BepInExPack` | `5.4.2305` | Profile root (`winhttp.dll`, `doorstop_config.ini`, `BepInEx/core/*`) |
| `Evaisa-LethalLib` | `1.2.0` | `BepInEx/plugins/Evaisa-LethalLib/LethalLib/LethalLib.dll` (+ `lethallib` bundle) |
| `Evaisa-HookGenPatcher` | `0.0.5` | `BepInEx/patchers/Evaisa-HookGenPatcher/` + `BepInEx/config/HookGenPatcher.cfg` |
| `MonoDetour-MonoDetour` | `0.6.3` | `BepInEx/core/MonoDetour-MonoDetour/` (dep of `MonoDetour_BepInEx_5`) |
| `MonoDetour-MonoDetour_BepInEx_5` | `0.6.3` | `BepInEx/patchers/MonoDetour-MonoDetour_BepInEx_5/0.com.github.MonoDetour.BepInEx.5.dll` |
| `giosuel-Imperium` | `1.4.0` | `BepInEx/plugins/giosuel-Imperium/giosuel.Imperium.dll` |
| `DiFFoZ-BepInEx_MonoMod_Debug_Patcher` | `1.1.1` | `BepInEx/patchers/DiFFoZ-BepInEx_MonoMod_Debug_Patcher/` (dep of `Imperium`) |
| `Rune580-LethalCompany_InputUtils` | `0.7.13` | `BepInEx/plugins/Rune580-LethalCompany_InputUtils/` (dep of `Imperium`) |
| `xilophor-LethalNetworkAPI` | `3.3.3` | `BepInEx/plugins/xilophor-LethalNetworkAPI/` (dep of `Imperium`) |
| `Lordfirespeed-OdinSerializer` | `2024.2.2700` | `BepInEx/core/Lordfirespeed-OdinSerializer/OdinSerializer/OdinSerializer.dll` (dep of `LethalNetworkAPI`) |

*(Note: Verified from `BepInEx.Utility.TryResolveDllAssembly` that BepInEx 5 searches `BepInEx/core`, `patchers`, and `plugins` recursively via `SearchOption.AllDirectories`.)*

---

## 2. Shipped Assembly & Unity Package Versions

Verified via `rea inspect-managed-artifact` (`docs/re/rea/*.json`) and binary package version metadata from `/mnt/HDD_1TB/SteamLibrary/steamapps/common/Lethal Company/Lethal Company_Data/Managed/`:

| Component / Assembly | Exact Package / Version | CLR / SHA-256 (prefix) |
|---|---|---|
| **Unity Engine** | **`2022.3.62f2`** | `globalgamemanagers` |
| **`Unity.Netcode.Runtime.dll`** (`com.unity.netcode.gameobjects`) | **`1.12.2`** | CLR `v4.0.30319`, `dd5313b862c6...` |
| **`Unity.Netcode.Components.dll`** | **`1.12.2`** | CLR `v4.0.30319`, `24ac15d1059d...` |
| **`Unity.RenderPipelines.HighDefinition.Runtime.dll`** (`com.unity.render-pipelines.high-definition`) | **`14.0.12`** | CLR `v4.0.30319`, `d1985d5a5b73...` |
| **`Unity.RenderPipelines.Core.Runtime.dll`** (`com.unity.render-pipelines.core`) | **`14.0.12`** | CLR `v4.0.30319`, `168d2ee3f8a0...` |
| **`Unity.AI.Navigation.dll`** (`com.unity.ai.navigation`) | **`1.1.6`** | — |
| **`Unity.InputSystem.dll`** (`com.unity.inputsystem`) | **`1.14.0`** | — |
| **`Assembly-CSharp.dll`** | Game build `build-guid=2ef0cde3b693468cbb8621cc6980bb67` | CLR `v4.0.30319`, `5f7db5538b78...` |
| **`LethalLib.dll`** | Thunderstore **`1.2.0`** (`MyPluginInfo.PLUGIN_VERSION = "1.2.0"`, assembly version `1.0.0.0`) | CLR `v4.0.30319`, `28fde57d12bf...` |

> **IMPORTANT for `@unity-builder`**: `LC-ExampleEnemy-ref` targets an older LC build (`Unity 2022.3.9f1`, HDRP `14.0.8`, NGO `1.5.2`). Our `UnityProject` MUST target **Unity `2022.3.62f2`**, **HDRP `14.0.12`**, **Netcode for GameObjects `1.12.2`**, and **`com.unity.ai.navigation` `1.1.6`** so AssetBundle shaders (`HDRP/Lit`) and `NetworkObject` serialization match the live game cleanly!

---

## 3. Exact Scale Calibration: Player Height, `mahoragaTargetHeight`, Enemies & Doorways

All measurements below were extracted directly from Lethal Company's serialized prefabs (`sharedassets3.assets`, `sharedassets1.assets`, `level3`, `level4`, `level10`) and decompiled `PlayerControllerB.cs` using exact Transform hierarchy composition and bindpose-to-rest-pose skinned vertex evaluation.

### 3.1 Lethal Company Crew / Player Exact Height
- **Standing `CharacterController.height`** (`sharedassets3.assets` `Player` prefab & `PlayerControllerB.cs` L5508):
  - **`height = 2.50 u`** (`center = (0, 1.28, 0)` -> capsule spans `Y = [0.03, 2.53]`, `radius = 0.40 u`, `stepOffset = 0.44 u`, `slopeLimit = 48°`)
  - *(Crouching: `PlayerControllerB.cs` L5501–5502 lerps `height -> 1.50 u`, `center.y -> 0.72 u`)*
- **First-Person Camera Eye Height** (`Player/ScavengerModel/metarig/CameraContainer/MainCamera`):
  - `ScavengerModel.scale = (2.0, 2.0, 2.0)` × `metarig.scale = (0.5608, 0.5608, 0.5608)` = effective world scale `1.1216`; `CameraContainer.localPos = (0, -0.012, 2.104)` rotated `-90° X`
  - **Exact Camera Eye Height = `2.360 u`** (`2.3599 u` above Player origin `Y = 0`)
- **Rig Crown-of-Head Bone (`spine.004_end`)**: **`Y = 2.510 u`** (`2.5102 u`; neck/head base `spine.004 = 2.131 u`)
- **Skinned Visual Mesh (`ScavengerModel/LOD1` & `MaskedPlayerEnemy/LOD1`, 7,998 verts in Player-space)**:
  - **`Y = [-0.037, +2.545]`** (total bounding height `2.581 u`; `+2.545 u` above ground `Y = 0`)
- **`Player` BoxCollider**: `center.y = 1.190 u`, `size.y = 2.270 u` (`Y = [0.055, 2.325]`)
- *(Note: `level4` editor proxy `playerScaleReferenceModel` is `2.20 u`, whereas all live spawned `Player` instances in `level3` & `sharedassets3.assets` are `2.50 u`.)*

### 3.2 Canonical `mahoragaTargetHeight`
$$\text{playerHeight} = \mathbf{2.50\text{ u (meters)}}$$
$$\mathbf{\text{mahoragaTargetHeight} = 1.5 \times 2.50 = 3.75\text{ u (meters)}}$$

- **Calibration Specification for `@modeler` & `@unity-builder`**:
  - Feet soles flat at **`Y = 0.00`** (Blender **`Z = 0.00`**).
  - Standing **top of skull / crown of head = `3.75 m` (= `3.75` Unity units at prefab scale `(1, 1, 1)`)**.
  - At `3.75 u`, a standing player's eyes (`Y = 2.36 u`) look straight into Mahoraga's lower chest / solar plexus (`~63%` of body height) and sharply upward at the eyeless 4-winged skull (`Y = 3.25–3.75 u`).
  - **Dharma Wheel Ceiling Clearance Note**: Standard Facility hallways (`HallwayTileTypeA/B/C/D`) have a **`3.916 u`** ceiling! Positioning the floating Eight-Handled Dharma Wheel **behind the upper back / nape centered around `Y = 3.25–3.35 u` (outer rim radius `~0.50–0.55 u`, top of upper spoke sphere `~3.85–3.90 u`)** keeps even the wheel cleanly inside `3.916 u` factory hallway ceilings with zero ceiling clipping while staying unmistakably iconic above/behind the shoulders!

### 3.3 Cross-Check Against Shipped Vanilla Enemies & Reference Template

| Entity (Shipped Asset) | Measured Visual Height (u) | Ratio vs Player (`2.50 u`) | `NavMeshAgent` (`radius`, `height`, `baseOffset`) |
|---|---:|---:|---|
| `DressGirl` (Ghost Girl) | `2.244 u` | `0.90×` | `r = 0.50`, `h = 2.00`, `off = 0.00` |
| `Player` / `MaskedPlayerEnemy` | `2.50–2.58 u` | `1.00×` | `r = 0.59`, `h = 2.00`, `off = 0.00` |
| `ButlerEnemy` | `2.837 u` | `1.13×` | — |
| `LC-ExampleEnemyModel.fbx` (template) | `3.137 u` | `1.25×` | `r = 0.70`, `h = 2.00`, `off = 0.00` |
| `Flowerman` (Bracken) | `3.459 u` | `1.38×` | `r = 1.17`, `h = 3.30`, `off = 0.00` |
| `SpringMan` (Coil-Head) | `3.595 u` | `1.44×` | `r = 0.81`, `h = 3.30`, `off = 0.00` |
| **`Mahoraga` (Target)** | **`3.750 u`** | **`1.50×`** | **Recommended: `r = 0.60`, `h = 3.30`, `off = 0.00`** |
| `NutcrackerEnemy` | `3.903 u` | `1.56×` | `r = 0.67`, `h = 3.30`, `off = 0.00` |
| `JesterEnemy` (popped) | `5.751 u` | `2.30×` | `r = 1.01`, `h = 2.00`, `off = 0.00` |
| `ForestGiant` (outside) | `10.418 u` | `4.17×` | `r = 1.50`, `h = 2.00`, `off = 0.00` |

### 3.4 Shipped Doorway & Hallway Dimensions & Doorway-Safe `NavMeshAgent`
Measured directly from shipped meshes and `DoorLock` colliders:
- **Factory Standard Door (`StandardDoorSize`)**: Width `1.643 u`, Height `3.220 u`
- **Steel / Manor Door (`FancyDoor` / `FancyDoorFrame` / `SteelDoor`)**: Door mesh `1.514 W × 3.141 H`, `DoorLock` solid collider `1.837 W × 3.095 H`, frame `1.918 W × 3.270 H` (`FancyDoorFrameWide`: `3.680 W × 3.270 H`)
- **Mineshaft Door (`YellowMineDoor` / `MineDoorFrame`)**: Door mesh `1.515 W × 3.008 H`, frame `3.676 W × 3.679 H`
- **Glass Double Doors (`GlassDoubleDoorsMapModel`)**: `2.464 W × 3.095 H`
- **Hallway Ceilings**:
  - `HallwayTileTypeA/B/C/D` (standard Factory): **`3.916 u`** height
  - `NarrowHallwayTile2x2`: **`5.500 u`** height
  - `DefaultLargeDoorwayTile`: **`6.200 u`** height
- **Baked NavMesh Settings (`NavMeshProjectSettings` -> `Humanoid`)**:
  - `agentRadius = 0.35`, `agentHeight = 1.00`, `agentSlope = 55.9°`, `agentClimb = 0.80`
- **Recommended Doorway-Safe `NavMeshAgent` for Mahoraga**:
  - `agentTypeID = 0` (`Humanoid`)
  - **`radius = 0.60`** (narrower than `Nutcracker` `0.67`, `SpringMan` `0.81`, `Flowerman` `1.17`; comfortably clears `1.51–1.64 u` doors and tight factory catwalks)
  - **`height = 3.30`** (exact match with `Flowerman`, `SpringMan`, `Nutcracker`; Unity's runtime `NavMeshAgent.height` does not re-reject baked `Humanoid` doorway navmesh triangles, while accurately representing tall bipedal clearance)
  - **`baseOffset = 0.0`**, **`angularSpeed = 360`**, **`acceleration = 45`**, **`stoppingDistance = 0.2`**, **`obstacleAvoidanceType = LowQualityObstacleAvoidance`** (prevents doorway snagging against other enemies)
  - **Animation tip**: Giving `Walk` and `Run` a slight predatory forward torso hunch (`~10–15°`, lowering head crest ~`0.2–0.3 u` while stalking/charging) keeps Mahoraga's head from visibly brushing `3.10–3.22 u` door lintels while towering at a full `3.75 u` in `Idle` and attacks!

---

## 4. Exact Unity Layers, Tags & Prefab Wiring

Extracted directly from `globalgamemanagers` `TagManager` (Physics Gravity = `(0, -32, 0)`):

### 4.1 Complete Layer Table (0–30)

| ID | Layer Name | ID | Layer Name | ID | Layer Name |
|---:|---|---:|---|---:|---|
| 0 | `Default` | 11 | `Colliders` | 22 | **`ScanNode`** |
| 1 | `TransparentFX` | 12 | `PhysicsObject` | 23 | `EnemiesNotRendered` |
| 2 | `Ignore Raycast` | 13 | `Triggers` | 24 | `MiscLevelGeometry` |
| 3 | **`Player`** | 14 | **`MapRadar`** | 25 | `Terrain` |
| 4 | `Water` | 15 | `NavigationSurface` | 26 | `PlaceableShipObjects` |
| 5 | `UI` | 16 | `MoldSpore` | 27 | `PlacementBlocker` |
| 6 | `Props` | 17 | `Anomaly` | 28 | `Railing` |
| 7 | `HelmetVisor` | 18 | `LineOfSight` | 29 | `DecalStickableSurface` |
| 8 | `Room` | 19 | **`Enemies`** | 30 | `Vehicle` |
| 9 | `InteractableObject` | 20 | `PlayerRagdoll` | | |
| 10 | `Foliage` | 21 | `MapHazards` | | |

### 4.2 Required Prefab Layer & Tag Wiring (Verified from Vanilla Enemies + `DoorLock` + `ShotgunItem` + `Landmine`)
- **Enemy Root GameObject**:
  - Layer: **`19` (`Enemies`)**, Tag: **`Enemy`**
  - Components: `NetworkObject`, `NavMeshAgent`, `MahoragaAI` (subclass of `EnemyAI`)
- **Hitbox / Collision Child (`EnemyAICollisionDetect`)**:
  - Layer: **`19` (`Enemies`)** — **MANDATORY**: `ShotgunItem` (`524288 == 1 << 19`), `StunGrenadeItem` (`524288 == 1 << 19`), `Landmine` (`layer == 19`), and `Shovel` all query Layer 19!
  - Tag: **`Enemy`** — **MANDATORY**: `DoorLock.OnTriggerStay` checks `other.CompareTag("Enemy")`! Without Tag `"Enemy"` on the trigger collider GameObject, Mahoraga CANNOT trigger automatic enemy door opening!
  - Collider: `BoxCollider` or `CapsuleCollider` with **`isTrigger = true`** (matching `Flowerman`, `SpringMan`, `Nutcracker`), spanning approximately `width 1.1 × height 3.6 × depth 1.0` (`center.y = 1.8`).
  - `EnemyAICollisionDetect.mainScript` MUST be assigned to the root `MahoragaAI` (LethalLib explicitly warns if null).
- **ScanNode Child**:
  - Layer: **`22` (`ScanNode`)**
  - Components: `ScanNodeProperties` + **`BoxCollider` with `isTrigger = false`** (verified on `Flowerman`, `SpringMan`, `Nutcracker` — `BoxCollider` size `(1,1,1)`, child scale `~0.5–0.8`, positioned at chest `Y ≈ 2.2 u`).
  - `ScanNodeProperties` fields: `nodeType = 1` (red danger node), `minRange = 1` (or `2`), `maxRange = 13` (or `18`), `requiresLineOfSight = true`, `headerText = "Mahoraga"`, `subText = ""` (or updated dynamically), `creatureScanID` (set automatically by `LethalLib` when `TerminalNode` has `creatureFileID`, or set to matching custom ID — see §6.2).
- **MapDot Child (Ship Radar)**:
  - Layer: **`14` (`MapRadar`)**
  - Red sphere/dot mesh, `localPosition = (0, 0.5, 0)`, `localScale ≈ (1.3, 0.5, 1.3)` with unlit red material (`HDRP/Unlit`).
- **Eye Transform**:
  - Positioned at head/temple height **`Y ≈ 3.30 u`**, `Z ≈ +0.35 u` (used by `CheckLineOfSightForPlayer`, `IShockableWithGun.GetShockablePosition()`, and `StunGrenadeItem`).
- **AudioSources (`creatureVoice`, `creatureSFX`, plus optional `wheelAudio` / `footstepAudio`)**:
  - Assigned to `Diagetic` AudioMixer group (template at `tools/LC-ExampleEnemy-ref/UnityProject/Assets/AudioMixerController/Diagetic.mixer`), then fixed at runtime via `LethalLib.Modules.Utilities.FixMixerGroups(prefab)`!

---

## 5. Exact `Assembly-CSharp.dll` API Reference

### 5.1 `EnemyAI` Key Fields & Method Signatures (`docs/re/decompiled/EnemyAI.cs`)
- **Key Fields**:
  - `public EnemyType enemyType;`
  - `public Animator creatureAnimator;`
  - `public AudioSource creatureVoice;`, `public AudioSource creatureSFX;`
  - `public Transform eye;`, `public AudioClip dieSFX;`
  - `public EnemyBehaviourState[] enemyBehaviourStates;` (MUST be populated with ≥ number of states, or `SwitchToBehaviourStateOnLocalClient` throws `IndexOutOfRangeException`!)
  - `public int currentBehaviourStateIndex;`, `public int previousBehaviourStateIndex;`
  - `public NavMeshAgent agent;`
  - `public int enemyHP = 3;`
  - `public bool isEnemyDead;`, `public bool inSpecialAnimation;`, `public bool ventAnimationFinished;`
  - `public float openDoorSpeedMultiplier;` (copied from `enemyType.doorSpeedMultiplier` in `EnemyAI.Start()` L324!)
  - `public float stunNormalizedTimer;`, `public int stunnedIndefinitely;`, `public float postStunInvincibilityTimer;`, `public PlayerControllerB stunnedByPlayer;`
  - `public float AIIntervalTime = 0.2f;`, `public float syncMovementSpeed = 0.22f;`
- **Core Virtual & RPC Signatures**:
  ```csharp
  public virtual void Start()
  public virtual void Update() // handles stunNormalizedTimer decay (Time.deltaTime / enemyType.stunTimeMultiplier), vent spawn, SmoothDamp client interpolation, and DoAIInterval() timer on IsOwner
  public virtual void DoAIInterval() // sets agent.SetDestination(destination) & calls SyncPositionToClients()
  public virtual void HitEnemy(int force = 1, PlayerControllerB playerWhoHit = null, bool playHitSFX = false, int hitID = -1)
  public void HitEnemyOnLocalClient(int force = 1, Vector3 hitDirection = default(Vector3), PlayerControllerB playerWhoHit = null, bool playHitSFX = false, int hitID = -1)
  [ServerRpc(RequireOwnership = false)] public void HitEnemyServerRpc(int force, int playerWhoHit, bool playHitSFX, int hitID = -1)
  [ClientRpc] public void HitEnemyClientRpc(int force, int playerWhoHit, bool playHitSFX, int hitID = -1)
  public virtual void HitFromExplosion(float distance)
  public virtual void SetEnemyStunned(bool setToStunned, float setToStunTime = 1f, PlayerControllerB setStunnedByPlayer = null)
  public virtual void OnCollideWithPlayer(Collider other)
  public virtual void OnCollideWithEnemy(Collider other, EnemyAI collidedEnemy = null)
  public virtual void DetectNoise(Vector3 noisePosition, float noiseLoudness, int timesPlayedInOneSpot = 0, int noiseID = 0)
  public void SwitchToBehaviourState(int stateIndex) // calls SwitchToBehaviourStateOnLocalClient(stateIndex) + SwitchToBehaviourServerRpc(stateIndex)
  public void SwitchToBehaviourStateOnLocalClient(int stateIndex)
  public virtual void KillEnemy(bool destroy = false) // sets isEnemyDead = true, disables agent, triggers creatureAnimator.SetTrigger("KillEnemy")
  public void KillEnemyOnOwnerClient(bool overrideDestroy = false) // guards !IsOwner and (!enemyType.canDie && !flag)
  public PlayerControllerB MeetsStandardPlayerCollisionConditions(Collider other, bool inKillAnimation = false, bool overrideIsInsideFactoryCheck = false)
  public PlayerControllerB CheckLineOfSightForPlayer(float width = 45f, int range = 60, int proximityAwareness = -1)
  public PlayerControllerB CheckLineOfSightForClosestPlayer(float width = 45f, int range = 60, int proximityAwareness = -1, float bufferDistance = 0f)
  public bool TargetClosestPlayer(float bufferDistance = 1.5f, bool requireLineOfSight = false, float viewWidth = 70f)
  public bool SetDestinationToPosition(Vector3 position, bool checkForPath = false)
  public void ChangeOwnershipOfEnemy(ulong newOwnerClientId)
  ```
- **CRITICAL Networking / Authority Notes (`EnemyAI.cs`)**:
  1. `HitEnemy(...)` runs **once on EVERY client (including host/owner)** via `HitEnemyOnLocalClient` -> `HitEnemyServerRpc` -> `HitEnemyClientRpc` (which skips `playerWhoHit == localPlayerId` to avoid double-calling on the attacker). Therefore, **HP deduction, adaptation stack increments, and state transitions MUST be server/owner-authoritative** (`if (IsServer)` / `if (IsOwner)`) and broadcast via dedicated `ClientRpc` / `NetworkVariable` so all clients stay 100% synchronized!
  2. `MeetsStandardPlayerCollisionConditions(Collider other, ...)` returns non-null **ONLY on the local client of the collided player** (`component == GameNetworkManager.Instance.localPlayerController`). That is why `FlowermanAI`, `SpringManAI`, `JesterAI`, and `NutcrackerEnemyAI` either call `playerControllerB.DamagePlayer(...)` directly on the local collided player (which syncs via `PlayerControllerB` RPCs) or send a `[ServerRpc(RequireOwnership = false)]` for synchronized grab/kill animations!
  3. **Owner vs Server**: By default spawned enemies are owned by Server (`OwnerClientId == 0`), UNLESS `ChangeOwnershipOfEnemy(...)` is called. For **pure server-authoritative AI**, do **NOT** call `ChangeOwnershipOfEnemy` — keep ownership permanently on Host/Server (`IsServer && IsOwner`)!

### 5.2 Complete Damage, Weapon `hitID`, & Stun Source Table (For Mahoraga Adaptation!)
Verified directly from decompiled weapon/hazard classes (`docs/re/decompiled/{Shovel,ShotgunItem,KnifeItem,StunGrenadeItem,PatcherTool,Landmine,Turret,VehicleController,SpikeRoofTrap}.cs`):

| Damage / Stun Source | Method Invoked on `EnemyAI` | `force` | `hitID` | `playerWhoHit` | Notes for Adaptation Detection |
|---|---|---:|---:|---|---|
| **Shovel / Stop Sign / Yield Sign** (`Shovel.cs` L215) | `HitEnemy` | `1` | **`1`** | Attacker | Unambiguously identified by `hitID == 1` |
| **Kitchen Knife** (`KnifeItem.cs` L129) | `HitEnemy` | `1` | **`5`** | Attacker | Unambiguously identified by `hitID == 5` |
| **Shotgun** (`ShotgunItem.cs` L330–341) | `HitEnemy` | **`5`** (<3.7m), **`3`** (<6m), **`2`** (≥6m) | **`-1`** *(default!)* | Attacker | **TRAP**: `ShotgunItem` passes default `hitID = -1`! Detect via `hitID == -1 && playerWhoHit != null && (playerWhoHit.currentlyHeldObjectServer is ShotgunItem || force >= 2)` |
| **Landmine / Explosion / Jetpack / EasterEgg Blast** (`Landmine.cs` L435–436) | `HitEnemy` + `HitFromExplosion(dist)` | **`6`** | **`-1`** | `null` | Calls `HitEnemyOnLocalClient(6)` immediately followed by virtual **`HitFromExplosion(float distance)`** (only on `IsOwner`, within `4.5m`) |
| **Company Cruiser Ram** (`VehicleController.cs` L3120, 3133, 3151) | `HitEnemy` or `KillEnemyOnOwnerClient` | `2` / `12` / instakill | `331` / `-1` | Driver | High-speed ram calls `KillEnemyOnOwnerClient()` directly; guard in `KillEnemy` / `HitEnemy` if adapting to Blunt/Vehicle |
| **Stun Grenade / Homemade Flashbang / Radar Booster Flash** (`StunGrenadeItem.cs` L347, 351) | **`SetEnemyStunned(true, enemyStunTime, player)`** | — | — | Thrower / `null` | Does **not** call `HitEnemy`; override `SetEnemyStunned`! |
| **Zap Gun** (`PatcherTool.cs` -> `IShockableWithGun.ShockWithGun`) | **`SetEnemyStunned(true, 0.25f, player)`** + `stunnedIndefinitely++` | — | — | Shocker | Continuous shock increments `stunnedIndefinitely`; once adapted to Shock/Stun, override `SetEnemyStunned` to ignore/shorten & clear `stunnedIndefinitely = 0`! |
| **Spike Roof Trap** (`SpikeRoofTrap.cs` L198) | `KillEnemyOnOwnerClient()` | — | — | `null` | Bypasses `HitEnemy` unless `enemyType.canDie` or custom override handles it |

### 5.3 Door-Opening API (`DoorLock.cs`)
- **Automatic Unlocked Door Opening** (`DoorLock.cs` L291–308):
  - Runs on **Server only** inside `DoorLock.OnTriggerStay(Collider other)` when `other.CompareTag("Enemy")` and `!isLocked && !isDoorOpened`.
  - Accumulates `enemyDoorMeter += Time.deltaTime * component.mainScript.openDoorSpeedMultiplier;` (initialized in `EnemyAI.Start()` from `enemyType.doorSpeedMultiplier`).
  - At `enemyDoorMeter > 1f`: resets meter to `0f`, calls `GetComponent<AnimatedObjectTrigger>().TriggerAnimationNonPlayer(mainScript.useSecondaryAudiosOnAnimatedObjects, overrideBool: true)`, and calls `OpenDoorAsEnemyServerRpc()` -> `OpenDoorAsEnemyClientRpc()` -> `OpenDoorAsEnemy(true)` (sets `isDoorOpened = true`, `navMeshObstacle.enabled = false`).
- **Forcing Locked Doors (Active Door Breach / Adaptation)** (`DoorLock.cs` L102–160, L329–385):
  - Public fields: `public bool isLocked;`, `public DoorLock twinDoor;`
  - Unlocking from Server/Client: `doorLock.UnlockDoorSyncWithServer()` (calls `UnlockDoor()` + `UnlockDoorServerRpc()` -> `UnlockDoorClientRpc()`), then `doorLock.OpenDoorAsEnemyServerRpc()` + `doorLock.GetComponent<AnimatedObjectTrigger>()?.TriggerAnimationNonPlayer(false, overrideBool: true)`.
  - Note: Locked doors have `navMeshObstacle.carving = true` (carving a hole in the NavMesh), so a breaching Mahoraga scanning nearby `DoorLock`s within ~`2.5–3.5 u` along its target vector can cleanly shatter/force locked doors and immediately resume pathing!

### 5.4 `PlayerControllerB` Damage & Kill Signatures (`GameNetcodeStuff_PlayerControllerB.cs`)
```csharp
public void DamagePlayer(
    int damageNumber,
    bool hasDamageSFX = true,
    bool callRPC = true,
    CauseOfDeath causeOfDeath = CauseOfDeath.Unknown,
    int deathAnimation = 0,
    bool fallDamage = false,
    Vector3 force = default(Vector3))

public void KillPlayer(
    Vector3 bodyVelocity,
    bool spawnBody = true,
    CauseOfDeath causeOfDeath = CauseOfDeath.Unknown,
    int deathAnimation = 0,
    Vector3 positionOffset = default(Vector3),
    bool setOverrideDropItems = false)
```
- Note on `DamagePlayer` L1094: if `health - damageNumber <= 0 && !criticallyInjured && damageNumber < 50`, Lethal Company applies "critical injury protection" leaving the player at `5 HP`. Hits with **`damageNumber >= 50`** (e.g., Mahoraga cleave `50–65`, slam `75–100`) bypass critical-injury mercy and cleanly kill low-HP players!
- Relevant `CauseOfDeath` enum values: `Bludgeoning (1)`, `Blast (3)`, `Mauling (6)`, `Crushing (8)`, `Stabbing (14)`, `Snipping (17)`, `Scratching (18)`.
- Note: Both `DamagePlayer` and `KillPlayer` guard `if (!IsOwner || isPlayerDead || !AllowPlayerDeath()) return;`, so a server-authoritative attack should send a `[ClientRpc]` containing `targetPlayerClientId`, damage, `CauseOfDeath`, and knockback vector so the targeted player's owning client executes `DamagePlayer`/`KillPlayer`!

### 5.5 `EnemyType` & `ScanNodeProperties` Exact Schemas
- **`EnemyType` (`docs/re/decompiled/EnemyType.cs`)**:
  - `enemyName`, `probabilityCurve`, `spawningDisabled`, `numberSpawnedFalloff`, `useNumberSpawnedFalloff`, `enemyPrefab`
  - `PowerLevel` (float, e.g. `3f` for Jester/Flowerman/Coil-Head), `MaxCount` (int, `1` for unique Mahoraga boss!), `isOutsideEnemy`, `isDaytimeEnemy`, `increasedChanceInterior`, `normalizedTimeInDayToLeave`
  - `stunTimeMultiplier = 2f`, `doorSpeedMultiplier = 1f`, `stunGameDifficultyMultiplier = 1f`, `canBeStunned = true`, `canDie = true`, `canBeDestroyed = true`, `destroyOnDeath = false`, `canSeeThroughFog = false`, `disableAnimatorWhenFar = false`
  - `pushPlayerForce = 0.16f`, `pushPlayerDistance = 1.26f`
  - `SizeLimit` (`NavSizeLimit`), `EnemySize`, `WaterType`
  - `hitBodySFX`, `hitEnemyVoiceSFX`, `deathSFX`, `stunSFX`, `overrideVentSFX`, `miscAnimations`, `audioClips`
- **`ScanNodeProperties` (`docs/re/decompiled/ScanNodeProperties.cs`)**:
  - `maxRange = 7`, `minRange = 5`, `requiresLineOfSight = true`, `headerText`, `subText`, `scrapValue`, `creatureScanID = -1`, `nodeType` (`0` = default/green, `1` = red danger/enemy, `2` = scrap/blue)

### 5.6 Vanilla Enemy AI Patterns Studied (`docs/re/decompiled/`)
- **`FlowermanAI`** (Bracken, `enemyHP = 3`, `NMA r=1.17 h=3.30`): 3 states (0=roam, 1=evade when spotted, 2=anger chase up to `speed = 12f`); server-mediated `KillPlayerAnimationServerRpc` -> `KillPlayerAnimationClientRpc`; `HitEnemy` deducts HP and sets `angerMeter = 11f` on `IsOwner`.
- **`SpringManAI`** (Coil-Head, `NMA r=0.81 h=3.30`, `canDie = false`): freezes when any player has LOS, sprints up to `currentChaseSpeed` when unseen; `OnCollideWithPlayer` calls `DamagePlayer(90, ..., CauseOfDeath.Strangulation, 2)`.
- **`JesterAI`** (`NMA r=1.01 h=2.00`): 3 states (0=roam `5f`, 1=cranking windup `0f`, 2=unstoppable chase ramping `speed` to `18f` with `KillPlayerServerRpc` -> `KillPlayerClientRpc`).
- **`DressGirlAI`** (`NMA r=0.50 h=2.00`): single-target haunting, `speed = 5.25f`, `KillPlayer(..., CauseOfDeath.Unknown, 1)`.
- **`NutcrackerEnemyAI`** (`NMA r=0.67 h=3.30`): patrol/scan/attack states, conditional armor (`HitEnemy` only damages `enemyHP` when `isInspecting || currentBehaviourStateIndex == 2` — exact precedent for conditional damage resistance / immunity feedback SFX via `enemyType.audioClips`!).

---

## 6. Exact `LethalLib 1.2.0` API Reference

Verified via `ilspycmd` decompilation (`docs/re/decompiled_lethallib/`) and `rea inspect-managed-members`:

### 6.1 `LethalLib.Modules.NetworkPrefabs`
```csharp
public static void RegisterNetworkPrefab(GameObject prefab)
public static GameObject CreateNetworkPrefab(string name)
public static GameObject CloneNetworkPrefab(GameObject prefabToClone, string newName = null)
```
- Hooks `GameNetworkManager.Start` and registers every queued prefab with `NetworkManager.Singleton.AddNetworkPrefab(prefab)`.
- **Call Order in `Plugin.Awake()`**:
  1. Run NetcodePatcher `InitializeNetworkBehaviours()` reflection loop (before registering prefabs!).
  2. Load AssetBundle (`AssetBundle.LoadFromFile(...)`).
  3. Call `LethalLib.Modules.Utilities.FixMixerGroups(enemyType.enemyPrefab)`.
  4. Call `LethalLib.Modules.NetworkPrefabs.RegisterNetworkPrefab(enemyType.enemyPrefab)`.
  5. Call `LethalLib.Modules.Enemies.RegisterEnemy(...)`.

### 6.2 `LethalLib.Modules.Enemies` — All 6 `RegisterEnemy` Overloads
```csharp
// Overload 1 (explicit SpawnType + LevelTypes bitflags):
public static void RegisterEnemy(
    EnemyType enemy, int rarity, Levels.LevelTypes levelFlags, SpawnType spawnType,
    TerminalNode infoNode = null, TerminalKeyword infoKeyword = null)

// Overload 2 (explicit SpawnType + LevelTypes + custom modded level name overrides):
public static void RegisterEnemy(
    EnemyType enemy, int rarity, Levels.LevelTypes levelFlags, SpawnType spawnType,
    string[] spawnLevelOverrides = null, TerminalNode infoNode = null, TerminalKeyword infoKeyword = null)

// Overload 3 (explicit SpawnType + per-level rarity dictionaries — RECOMMENDED):
public static void RegisterEnemy(
    EnemyType enemy, SpawnType spawnType,
    Dictionary<Levels.LevelTypes, int>? levelRarities = null,
    Dictionary<string, int>? customLevelRarities = null,
    TerminalNode infoNode = null, TerminalKeyword infoKeyword = null)

// Overload 4 (auto SpawnType from EnemyType.isDaytimeEnemy / isOutsideEnemy + bitflags):
public static void RegisterEnemy(
    EnemyType enemy, int rarity, Levels.LevelTypes levelFlags,
    TerminalNode infoNode = null, TerminalKeyword infoKeyword = null)

// Overload 5 (auto SpawnType + bitflags + custom level overrides):
public static void RegisterEnemy(
    EnemyType enemy, int rarity, Levels.LevelTypes levelFlags,
    string[] spawnLevelOverrides = null, TerminalNode infoNode = null, TerminalKeyword infoKeyword = null)

// Overload 6 (auto SpawnType + per-level rarity dictionaries — CLEANEST):
public static void RegisterEnemy(
    EnemyType enemy,
    Dictionary<Levels.LevelTypes, int>? levelRarities = null,
    Dictionary<string, int>? customLevelRarities = null,
    TerminalNode infoNode = null, TerminalKeyword infoKeyword = null)
```
- `Enemies.SpawnType` enum: `Default` (inside), `Daytime`, `Outside`.
- `Levels.LevelTypes` flags (`LethalLib.Modules.Levels`):
  - `None = 1`, `ExperimentationLevel = 4`, `AssuranceLevel = 8`, `VowLevel = 16`, `OffenseLevel = 32`, `MarchLevel = 64`, `RendLevel = 128`, `DineLevel = 256`, `TitanLevel = 512`, `Modded = 1024`, `AdamanceLevel = 2048`, `ArtificeLevel = 4096`, `EmbrionLevel = 8192`, `Vanilla` (all 11 vanilla moons), `All = -1`.
- **Bestiary Registration**: Pass `infoNode` (`TerminalNode` with `creatureName`, `displayText`, `clearPreviousText = true`, optional `displayVideo`) and `infoKeyword` (`TerminalKeyword` with `word = "mahoraga"`, `isVerb = false`). LethalLib automatically hooks `Terminal.Start`, assigns the next free `creatureFileID` to `infoNode`, registers the `info` verb noun, updates `ScanNodeProperties.creatureScanID` on the prefab to match `creatureFileID`, and adds the enemy to `QuickMenuManager.testAllEnemiesLevel` (so Imperium & debug spawners discover it immediately!).

---

## 7. Archived Raw Evidence (`docs/re/`)

- `docs/re/unity_batchmode_verified.log` — verified Unity `2022.3.62f2` batchmode run (`exit 0`, entitlement resolved, `WindowsStandaloneSupport` registered)
- `docs/re/rea/*.json` — `rea 6.8.0` `inspect-managed-artifact` & `inspect-managed-members` Evidence JSONs for `Assembly-CSharp.dll`, `LethalLib.dll`, `Unity.Netcode.Runtime.dll`, `Unity.Netcode.Components.dll`, `Unity.RenderPipelines.HighDefinition.Runtime.dll`, `Unity.RenderPipelines.Core.Runtime.dll`, `UnityEngine.CoreModule.dll`
- `docs/re/decompiled/*.cs` — `ilspycmd 11.2.0` C# decompilations of `EnemyAI`, `EnemyType`, `EnemyAICollisionDetect`, `GameNetcodeStuff.PlayerControllerB`, `RoundManager`, `DoorLock`, `ScanNodeProperties`, `FlowermanAI`, `SpringManAI`, `JesterAI`, `DressGirlAI`, `NutcrackerEnemyAI`, `Shovel`, `ShotgunItem`, `KnifeItem`, `StunGrenadeItem`, `PatcherTool`, `Landmine`, `Turret`, `VehicleController`, `RadarBoosterItem`, `SpikeRoofTrap`, `JetpackItem`, `CauseOfDeath`
- `docs/re/decompiled_lethallib/` — complete `ilspycmd 11.2.0` decompilation of `LethalLib 1.2.0`
