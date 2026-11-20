# Changelog

## 1.1.1

- Old Bird attacks now build toward 1.5 times the robot's standing height. Each distinct missile blast counts, including rapid bursts.
- Mahoraga approaches and strikes the Old Bird's body, keeping the same range and obstruction checks through windup and impact.
- The host commits defeated Old Birds to despawn before playing the explosion effects.

## 1.1.0

- Reworked player detection around audible events and close-range contact. Shadow ambushes follow a recorded noise.
- Added retaliation against mobs that actually land an identifiable hit. Old Bird hostility and missile-driven outdoor growth still work.
- Added a four-player coordinated shovel finisher and an escalating missile resistance requirement starting at three Old Birds.
- Flashbangs and identifiable timed stuns last half as long starting with the third application of the same type.
- Added checked jump traversal, an in-place jump animation and a low-intensity fill on the model's shaded side.

## 1.0.0

- First public release of Mahoraga LC for Lethal Company.
- Added progressive Dharma Wheel adaptation, shadow summoning, and late-day ambushes.
- Added separate natural indoor and outdoor spawns with configurable moon weights.
- Mahoraga prioritizes Old Birds and can grow as it adapts to their attacks outdoors.
- Fixed manual Old Bird spawning in Imperium on moons without a dormant nest.
- Imperium shows one Mahoraga entry and selects its prefab based on the requested location.
- Includes the original mod model, synthesized sound effects, and support for local custom music.
