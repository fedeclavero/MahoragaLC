# Mahoraga LC | Jujutsu Kaisen Boss for Lethal Company

Mahoraga brings the **Dharma Wheel** from *Jujutsu Kaisen* to *Lethal Company*. The Divine General rises from shadows, adapts to repeated attacks, and hunts crews and Old Birds. Swap weapons when you hear the wheel turn.

> **Work in progress.** The core encounter is playable. Balance, traversal, and compatibility with modded moons and equipment are still being refined.

## What you'll encounter

- **Sound-based hunting.** Mahoraga patrols until it hears noise or gets within about one meter of a player. Crouch to keep your footsteps quiet. Late in the day it can emerge from the shadows near a recent sound.
- **Progressive adaptation.** Shovels, knives, shotguns, explosions, direct Cruiser impacts, and Old Bird missiles are tracked separately. Four players can land a coordinated shovel finisher before it adapts to shovels. After a wheel turn for shovels, the same team needs an extra blow per stage. From the third flashbang onward, its stun lasts half as long.
- **Old Bird fights.** Mahoraga automatically hunts Old Birds and grows outdoors when their missiles hit it. Three different Old Birds can overcome its health protection before missile adaptation. Other creatures face retaliation when the mod can identify who hit Mahoraga.
- **Indoor and outdoor spawns.** Each uses a separate weighted pool on eligible vanilla and modded moons. Default weights are 20 indoors and 14 outdoors. Moon budgets, spawn points, and the spawn roll determine whether it appears.
- **Traversal.** Mahoraga can jump across supported gaps. After growing it can also jump onto the edge of the landed ship when the door is open and the route is clear. Mahoraga can also force key-locked doors. Scan it or type `info mahoraga` in the ship terminal for the bestiary entry.

## Install and play

1. Select **Lethal Company** in Thunderstore Mod Manager or r2modman.
2. Search for **MahoragaLC** by **MahoragaTeam** and install it. The manager installs **BepInExPack** and **LethalLib** as dependencies.
3. Choose **Start modded**. For co-op, the host and all players need the same version of the mod.

Spawn and gameplay checks for this release target **Lethal Company v81**. Imperium is optional.

## Configuration

After the first launch, open `BepInEx/config/Fede.MahoragaLC.cfg` in your mod profile. Tune `DefaultInteriorRarity`, `OutdoorRarity`, and the moon overrides under `[Spawning]`. Use `HitsPerAdaptationStage`, `FightOldBirds`, and `EnableShadowAmbush` to adjust how encounters play out.

### Custom encounter music

Set `UseCustomMusicOverride = true` under `[Audio]`. Put an `.ogg`, `.wav`, or `.mp3` in a `CustomMusic` folder beside `Fede.MahoragaLC.dll`. The public release includes the mod's sound effects and supports your own local music.

## Credits

Fan-made mod inspired by *Jujutsu Kaisen* by Gege Akutami. *Lethal Company* is by Zeekerss. Cover art created with ImageGen.

[Report an issue](https://github.com/fedeclavero/MahoragaLC/issues)
