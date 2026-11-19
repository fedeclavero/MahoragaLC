# Mahoraga LC — Terminal Bestiary Specification (`TERMINAL_BESTIARY.md`)

> **Status:** **FINALIZED v2** — Text, `3.75 u` stature, `nodeType = 1` danger node, and LethalLib 1.2.0 ScriptableObject bindings are **LOCKED** and verified against [`docs/RE_FINDINGS.md`](file:///home/fede/Escritorio/Mahoraga%20LC/docs/RE_FINDINGS.md).

---

## 1. ScriptableObject Asset Bindings (`mahoraga_assets`)

### 1.1 `MahoragaTerminalKeyword` (`TerminalKeyword`)
| Field | Exact Value |
|---|---|
| Asset Name | `MahoragaTerminalKeyword` |
| `word` | `"mahoraga"` *(lowercase; Terminal parser matches case-insensitively)* |
| `isVerb` | `false` |
| `defaultVerb` | `info` *(bound by LethalLib `Enemies.RegisterEnemy`)* |
| `specialKeywordResult` | `MahoragaTerminalNode` *(wired automatically or on asset)* |

### 1.2 `MahoragaTerminalNode` (`TerminalNode`)
| Field | Exact Value |
|---|---|
| Asset Name | `MahoragaTerminalNode` |
| `creatureName` | `"Mahoraga"` |
| `creatureFileID` | `-1` *(assigned dynamically by LethalLib on registration to prevent ID collisions)* |
| `clearPreviousText` | `true` |
| `maxCharactersToType` | `35` |
| `displayText` | Exact string in Section 2 below |

### 1.3 `ScanNodeProperties` (on `MahoragaPrefab/ScanNode`)
| Field | Exact Value |
|---|---|
| `headerText` | `"Mahoraga"` |
| `subText` | `"Divine General"` |
| `nodeType` | `1` *(Verified `ScanNodeProperties.nodeType`: `0` = green/default, **`1` = red danger/enemy**, `2` = blue/scrap)* |
| `minRange` | `2` |
| `maxRange` | `18` |
| `requiresLineOfSight` | `true` |
| `BoxCollider.isTrigger` | `false` *(Verified vanilla LC `Flowerman`/`SpringMan`/`Nutcracker` `ScanNode` colliders on Layer `22`)* |

---

## 2. Exact `displayText` for `MahoragaTerminalNode`

*(Formatted specifically for the Lethal Company Ship Terminal green-phosphor CRT viewport, authentic to Sigurd's Bestiary entries.)*

```text
MAHORAGA

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

"We hit it twice with the shotgun. The thing above its
head clicked like a giant clock, lit up gold, and the
third shell did nothing. Leave the scrap. Run."
  -- Recovered voice log, Moon 8-Titan
```

---

## 3. Terminal UX & Localization Checklist

- [ ] Typing `bestiary` on the Ship Terminal after scanning Mahoraga lists `Mahoraga`.
- [ ] Typing `info mahoraga` or `mahoraga` opens `MahoragaTerminalNode` cleanly with zero clipped lines or missing newlines.
- [ ] `ScanNode` unlocks the Bestiary entry once per save file on first scan, synchronized across multiplayer clients via LethalLib registration.
- [x] Verified exact stature (`3.75m` = `1.5 × 2.50m`), `ScanNodeProperties.nodeType = 1`, and `Layer 22` / `isTrigger = false` from `docs/RE_FINDINGS.md`.
