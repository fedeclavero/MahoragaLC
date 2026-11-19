# Mahoraga LC (`Fede.MahoragaLC`)

> *"Eight-Handled Sword Divergent Sila Divine General Mahoraga — Uncontained Apex Anomaly (Danger Level: 95%)."*

Adds **Divine General Mahoraga** (*Makora Sila-Octo*) to **Lethal Company** as a server-authoritative multiplayer apex horror boss enemy across all vanilla and modded moons.

---

## Features

- **1.5× Crew Stature (`3.75m`)**: Custom-sculpted HDRP 3D model towering over scavengers (`2.50m`) with an eyeless four-winged skull, occipital crest tail, pleated hakama, Sword of Extermination bound to his right forearm, and floating ornate golden Eight-Handled Dharma Wheel.
- **The Wheel of Adaptation**:
  - Tracks 6 weapon & crowd-control categories: **Melee** (Shovel, Signs, Knife), **Shotgun**, **Zap Gun**, **Stun Grenade / Flashbang**, **Explosion** (Landmine, Jetpack, Easter Egg), and **Other** (Cruiser, modded weapons).
  - After enduring repeated hits or stuns from any single category (default threshold: `2`), the golden Dharma Wheel turns **+45°** with a resonant temple gong and radiant HDR gold flash, healing Mahoraga (`20%` Max HP), escalating chase speed, and granting `95%` damage resistance (or `100%` stun immunity) to that category for the rest of the round.
  - **Lethal-Blow Precedence**: Coordinated crews can still slay Mahoraga by rotating weapon categories and delivering lethal burst damage before the Wheel completes its turn.
- **Door Forcing & Ship Operator Synergy**:
  - Rapidly opens unlocked doors and forces key-locked interior doors (`DoorLock`) open after `2.2s` of heavy pounding.
  - Respects powered **Steel Blast Doors** controlled from the Ship Terminal, preserving Ship Operator teamwork.
- **Dynamic Proximity & Encounter Music**:
  - Smooth `35m -> 16m` proximity volume curve with real-time `AudioLowPassFilter` raycast wall muffling (`StartOfRound.Instance.collidersAndRoomMaskAndDefault`).
  - Spectate-aware inside/outside isolation (`isInsideFactory`), single-instance global mix (zero overlapping tracks), wheel-gong ducking, instant `0.35s` fade-out on death, and optional `CustomMusic/` audio override.
- **Full Multiplayer & Bestiary Support**:
  - 100% server-authoritative AI with NetcodePatcher-woven NGO `1.12.2` RPCs (all players must have the mod installed).
  - Red Danger `ScanNode`, Ship Monitor `MapDot`, and full Ship Terminal Bestiary entry (`info mahoraga`).

---

## Installation

> **Multiplayer Requirement:** Both the host and all joining clients must install this mod and its dependencies (`BepInEx-BepInExPack` and `Evaisa-LethalLib`).

### Windows — Thunderstore Mod Manager / r2modman
1. Open **Thunderstore Mod Manager** or **r2modman** and select **Lethal Company**.
2. Create or select a profile and install **BepInExPack** (`5.4.2305+`) and **LethalLib** (`1.2.0+`).
3. Click **Settings → Profile → Import local mod** and choose `Fede-MahoragaLC-1.0.0.zip` (or search for `MahoragaLC` once published on Thunderstore).
4. Launch Lethal Company via **Start modded**.

### Linux — r2modman (Steam / Proton)
1. Install and launch **r2modman** (`v3.2.21+`) and select **Lethal Company** (Proton).
2. In Steam, set Lethal Company's Launch Options to:
   ```bash
   WINEDLLOVERRIDES="winhttp=n,b" %command%
   ```
3. Ensure **BepInExPack** (`5.4.2305+`) and **LethalLib** (`1.2.0+`) are installed in your profile.
4. Click **Settings → Profile → Import local mod** and select `Fede-MahoragaLC-1.0.0.zip`, then launch with **Start modded**.
5. **Proton Stability Tip**: In your profile's `BepInEx/config/BepInEx.cfg`, set `[Logging.Console] Enabled = false` (keeping `[Logging.Disk] Enabled = true`). On Wine/Proton, the external Win32 console window (`conhost.exe`) can block the Unity main thread during `StartOfRound` scene transitions when hosting a lobby.

### Manual Installation
1. Ensure **BepInEx 5** and **LethalLib 1.2.0+** are installed in your Lethal Company game directory.
2. Extract `Fede-MahoragaLC-1.0.0.zip` so that BOTH `Fede.MahoragaLC.dll` and `mahoraga_assets` sit together inside:
   ```text
   BepInEx/plugins/Fede-MahoragaLC/
   ├── Fede.MahoragaLC.dll
   └── mahoraga_assets
   ```

---

## Configuration (`BepInEx/config/Fede.MahoragaLC.cfg`)

Generated automatically on first launch:
- **`[Spawning]`**: `EnableMod`, `DefaultInteriorRarity` (`20`), `MoonRarityOverrides` (e.g. `Experimentation:10,Titan:35,Artifice:40`), `AllowOutdoorSpawn` (`false`), `OutdoorRarity` (`12`), `PowerLevel` (`3.0`), `MaxCount` (`1`).
- **`[Combat]`**: `MaxHP` (`18`), `AdaptationThreshold` (`2`), `AdaptedDamageResistance` (`0.95`), `AdaptationHealPercent` (`0.20`), `CleaveDamage` (`55`), `SlamDamage` (`65`), `CanForceLockedDoors` (`true`).
- **`[Movement]`**: `PatrolSpeed` (`3.2`), `InvestigateSpeed` (`4.5`), `ChaseSpeed` (`6.2`), `ChaseSpeedPerAdaptation` (`0.35`).
- **`[Audio]`**: `MusicEnabled` (`true`), `MusicMasterVolume` (`0.75`), `MusicOuterRange` (`35.0`), `MusicInnerRange` (`16.0`), `EnableWallMuffling` (`true`), `UseCustomMusicOverride` (`false`).

### Optional Custom Music Override
To swap the bundled theme with your own `.ogg`, `.wav`, or `.mp3` track:
1. Set `UseCustomMusicOverride = true` in `BepInEx/config/Fede.MahoragaLC.cfg`.
2. Create a folder named `CustomMusic` next to `Fede.MahoragaLC.dll` (`BepInEx/plugins/Fede-MahoragaLC/CustomMusic/`) and place your audio file inside.

---

## Credits

- **Boss theme:** "「Sukuna vs Mahoraga」(Malevolent Shrine) Jujutsu Kaisen S2 EP17 OST [Epic Cover]" by **Pharozen** ([YouTube](https://www.youtube.com/watch?v=vxTLtmpnKn8) · [channel](https://www.youtube.com/@pharozen)), a cover of the *Jujutsu Kaisen* Season 2 original soundtrack. Used in-game from `0:51` onward. All rights belong to their respective owners; if you are the author and want it removed, open an issue at https://github.com/fedeclavero/MahoragaLC and it will be replaced.
- **Mahoraga** and *Jujutsu Kaisen* belong to Gege Akutami, Shueisha and MAPPA. *Lethal Company* belongs to Zeekerss. This is a non-commercial fan project, not affiliated with any of them.
