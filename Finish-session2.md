# Finish — Sesión 2 (Memoria y Estado Completo del Proyecto Mahoraga LC)

**Fecha:** 19 de noviembre de 2026  
**Repositorio:** `/home/fede/Escritorio/Mahoraga LC`  
**Perfil de prueba local (r2modman):** `/home/fede/.config/r2modmanPlus-local/LethalCompany/profiles/MahoragaDev`

---

## 1. Resumen Ejecutivo — ¿Hasta dónde llegamos?

**El mod está 100% implementado, compilado, testeado (`36/36` tests en verde), empaquetado en AssetBundle de Unity (`BuildAll SUCCEEDED`, `ALL SELF-CHECK ASSERTIONS PASSED`, `0 FATAL`) y desplegado en el perfil `MahoragaDev` listo para jugar y probar.**

Durante esta sesión se completaron **dos grandes etapas** mediante un swarm de agentes especializados:

### Etapa A — Reemplazo del Modelo 3D por el Rig Oficial (`Mahoraga_-_RIG_V1.0.blend`)
1. Se descartó el modelo procedural anterior y se integró `/home/fede/Descargas/Mahoraga_-_RIG_V1.0.blend` en `assets/model/Mahoraga.blend` (`8,310` vértices, `12,106` triángulos, rig Auto-Rig Pro `Mahoraga_rig` con 390 huesos / 185 deform bones + `WheelBone` con `722/722` vértices exclusivos de la rueda + `SwordBone`).
2. Se preservó la escala nativa `1.0` dentro de `.blend` (para no romper los 261 constraints de Auto-Rig Pro) y se exportó `assets/model/Mahoraga.fbx` con `global_scale = 2.11560571` (`apply_unit_scale=True`, `apply_scale_options='FBX_SCALE_ALL'`, `use_armature_deform_only=True`), logrando:
   - Altura de corona del cuerpo: **`3.3748 m`** (`Idle` f1) / **`3.4146 m`** (rest pose).
   - Tope de la rueda Dharma: **`3.8134 m` <= `3.90 m`** (entra bajo techos interiores de Lethal Company de `3.916 u`).
3. Se hornearon y conectaron las 6 texturas PBR 2K (`assets/model/textures/` y `assets/model/Mahoraga.fbm/`) con material **`HDRP/Lit` Double-Sided** (`_DoubleSidedEnable = 1`, `_CullMode = 0`) en Unity para evitar caras invisibles en falda/alas/cola.

### Etapa B — Mejoras Post-Playtest #1 (Navegación, Old Birds, Rueda `+45°`, Percepción Exterior y Shadow Ambush)
Tras el Playtest #1 del usuario, se auditaron `Assembly-CSharp.dll`, `LethalLib.dll`, `globalgamemanagers` y `sharedassets1/3` y se implementaron todas las correcciones y mecánicas pedidas:

1. **Anti-Clipping (Nunca atravesar estructuras, paredes, barandas ni puertas cerradas)**:
   - **`m_WalkableMask` + `SizeLimit`**:
     - Interior (`MahoragaEnemyType` + `MahoragaPrefab`): `SizeLimit = NavSizeLimit.SmallSpaces`, `WaterType = EnemyWaterType.LandOnly`, `m_WalkableMask = 9753` (desactiva áreas `NotWalkable(1)`, `Jump(2)`, `SmallSpace(5)`, `MediumSpace(6)`, `Climb(7)`, `Overhead(8)`, `Nest(11)`, `Water(12)`), `radius = 0.55f`, `stoppingDistance = 0.35f`, `MedQualityObstacleAvoidance`, `updatePositionThreshold = 0.40f`.
     - Exterior (`MahoragaEnemyType_Outside` + `MahoragaPrefab_Outside`): `SizeLimit = NavSizeLimit.MediumSpaces`, `WaterType = EnemyWaterType.Amphibious`, `m_WalkableMask = 14873`, `radius = 0.85f` (escala dinámicamente hasta `1.45 m` cuando crece a `2x` en exterior), `stoppingDistance = 0.50f`, `HighQualityObstacleAvoidance`.
   - **`autoTraverseOffMeshLink = false` + `GuardOffMeshLinkServer()`**: Evita que salte/atraviese los 305 `OffMeshLink` de estanterías/barandas/camas de Lethal Company; si pisa un link, hace `Physics.Linecast` contra `collidersAndRoomMaskAndDefault` y aborta si hay pared/baranda.
   - **`SetSafeDestinationServer` y override de `NavigateTowardsTargetPlayer()`**: Valida todos los destinos con `NavMesh.SamplePosition(..., agent.areaMask)` y `agent.CalculatePath(...)` (evitando el bug vanilla de `GetNavMeshPosition(..., -1f)` que escribía coordenadas fuera del NavMesh).
   - **Freno en puertas cerradas no bloqueadas (`TryScanForLockedDoorServer`)**: Limita `agent.speed <= 0.8 m/s` a `< 1.8 m` de un `DoorLock` cerrado con `navMeshObstacle.enabled` para darle los `0.67 s` que requiere `DoorLock.OnTriggerStay` antes de cruzar el marco.
   - **Verificación de línea de golpe (`HasClearStrikePathToPlayer`)**: Exigida antes de entrar a `Attacking` y al resolver daño, evitando golpes a través de muros.
   - **Fix de `stunNormalizedTimer = -1f`**: En `EnemyAI.cs:355` el juego bloquea colisiones si `stunNormalizedTimer >= 0f`; ahora se limpia siempre a `-1f`.

2. **Spawn Natural tanto en Interior como en Exterior (4 Assets dedicados)**:
   - Se corrigió el bug de reutilizar un mismo prefab para dos `EnemyType` (que causaba `Error! Enemy-to-spawn 'Mahoraga' prefab does not have the same enemyType set` en `RoundManager.cs:2248` y restaba el `PowerLevel` exterior del pool interior).
   - Ahora `mahoraga_assets` contiene **4 assets de spawn distintos**:
     - `MahoragaEnemyType` + `MahoragaPrefab` (`GlobalObjectIdHash = 2847193651u`, `isOutsideEnemy = false`).
     - `MahoragaEnemyType_Outside` + `MahoragaPrefab_Outside` (`GlobalObjectIdHash = 2847193652u`, `isOutsideEnemy = true`, curva horaria `(0, 0.35) -> (0.5, 0.8) -> (1.0, 1.0)`).
   - Ambos se registran con `NetworkPrefabs.RegisterNetworkPrefab` y `Enemies.RegisterEnemy` (`AllowOutdoorSpawn = true` por defecto, `OutdoorRarity = 14`, `OutdoorMaxCount = 1`).
   - En `MahoragaAI.Start()`, se sincroniza `ScanNodeProperties.creatureScanID` desde `Plugin.BestiaryNode.creatureFileID` para que escanear al Mahoraga exterior también abra el Bestiario.

3. **Cero Bugs en Old Birds (`RadMechAI`) y Otros Monstruos Vanilla + Prioridad #1 Old Birds**:
   - **Nuevo parche Harmony `Plugin/src/Patches/OldBirdCompatibilityPatch.cs`**:
     - Todos los hooks hacen *no-op* inmediato si el objetivo/colisión no es un `MahoragaAI` vivo y visible (`!m.isEnemyDead && !m.IsSummonOrSubmergeActive`), garantizando **0 efectos secundarios** sobre peleas vanilla.
     - Corrige el punto ciego de `RadMechAI` a `< 8.85 m` (su `Eye` está a `Y = 8.85 m` con `forward.y = 0` y apuntaba a `GetThreatTransform()` en `Y = 0` pies, perdiendo LOS por ángulo `> 45°`): evalúa LOS y `lastSeenPosition` hacia el **pecho de Mahoraga** (`Y + 1.8f * ScaleFactor`) sin alterar el destino NavMesh en el suelo.
     - Baja el corte de puntería de `RadMechAI.AimAndShootCycle` de `6.0 m` a `1.5 m` cuando apunta a Mahoraga (evitando que el Old Bird se congele intentando hacer `AttemptGrabIfClose`, que en vanilla solo busca jugadores).
     - En `RadMechMissile.CheckCollision`, detecta el `CollisionCollider` (`Layer 19`, `isTrigger = true`) con `QueryTriggerInteraction.Collide` para que los misiles del Old Bird detonen directamente en el cuerpo de Mahoraga en vez de atravesarlo.
   - **Alerta y Limpieza Limpia en `MahoragaAI.OldBird.cs`**:
     - Al golpear a un Old Bird activo (`!inFlyingMode`, estado `!= 2`), lo alerta hacia Mahoraga (`SetTargetedThreat`, `targetedThreatCollider`, `SwitchToBehaviourStateOnLocalClient(1)`).
     - Al destruir un Old Bird (`OldBirdDestroyedClientRpc`), silencia sus audios LRAD/motor, apaga `agent.enabled` en servidor y marca `isEnemyDead = true` durante los `0.45 s` previos al `Despawn`, y reproduce la explosión mediante un `AudioSource` 3D ruteado al mixer `Diagetic` (`PlaySpatialClipAtPoint`).
   - **Prioridad #1 Old Birds y Exclusión Estricta de Otros Entes**:
     - Mahoraga **SOLO ataca a Old Birds (`RadMechAI`) y Jugadores** — jamás ataca a perros, gigantes, baboon hawks, arañas ni otros entes (ni siquiera por bloqueo o represalia; `FindSuspectedEnemyAttackerServer` solo clasifica `AdaptationCategory.EnemyMelee` para la rueda).
     - Si hay cualquier Old Bird vivo en el mismo dominio dentro de `OldBirdSeekRange` (`250 m` por defecto), Mahoraga **interrumpe inmediatamente** la persecución o patrulla de jugadores (`DoAIInterval` evalúa `EvaluateEnemyEngagementServer` antes que `GetRetaliationPlayer`) y va a cazar al Old Bird (manejando `inFlyingMode` con raycast vertical de `60 m` al NavMesh).

4. **Giro de la Rueda Dharma (`+45°` = 1 bola -> siguiente bola, NUNCA 360°)**:
   - Verificado en Blender (`Adapt_WheelSpin`): `WheelBone` rota exactamente **`+45.000°`** alrededor de su eje local `+Y` (`(0, 1, 0)`), pasando de la bola 1 a la bola 2 (8 bolas separadas cada `45°`).
   - `SummonTimeline.WheelTurnDegrees = 45f` (antes `360f`): al emerger gira `+45°` y pliega ese paso en `_summonWheelOffsetSteps` sin volver a `0°` (cero snap-back).
   - Cada golpe/misil recibido de un Old Bird (tanto en etapas de adaptación 1..3 como en golpes posteriores de crecimiento exterior mediante `AdvanceWheelStep()`) avanza `_wheelStepCount` en `+1` (`+45°`) y dispara `TriggerWheelClickClientRpc(..., playSpinAnim: true)` con animación `AdaptHash`, brillo dorado y gong.

5. **Percepción Realista en el Exterior (`HuntPlayersOutdoors = false` por defecto)**:
   - En el exterior (`isOutside == true`), Mahoraga **no sabe mágicamente dónde están los jugadores**. Deambula patrullando (`StartSearch`) y solo persigue cuando **ve** a un jugador por línea de visión real (LOS) o **escucha** ruido (`DetectNoise` -> `Investigating` -> LOS). Si pierde de vista al jugador en exterior, investiga su última posición vista y vuelve a deambular.

6. **Nueva Animación y Mecánica Tardía: Inmersión en Sombras y Emboscada (`Summon_Submerge` / `MahoragaState.Submerging = 7`)**:
   - **Animación Blender `Summon_Submerge`**: `78` frames a `30 fps` (`2.60 s`), in-place (`WheelBone = 0°`), reverso táctico de `Summon_Emerge` (postura erguida -> descenso agazapado plegando alas/brazos/cabeza bajo la rueda sin clipping). Contact sheet renderizado en `/home/fede/.gemini/antigravity/brain/a2d21268-c54e-4341-bf94-e6662e0c4bc3/scratch/summon_submerge_sheet.png`.
   - **Mecánica (`MahoragaAI.ShadowAmbush.cs` + `ShadowAmbushLogic.cs`)**:
     - Cuando la partida está avanzada (`normalizedTimeOfDay >= 0.62`, configurable con `ShadowAmbushMinTime`), Mahoraga lleva `>= 12 s` sin ver jugadores (`ShadowAmbushLostSightDelay`), pasó el cooldown (`45 s`), y **no hay ningún Old Bird vivo en su dominio** (`!HasLivingOldBirdInDomainServer()`):
     - Pre-valida un punto seguro de NavMesh a `4.5–8.0 m` frente a un jugador aleatorio vivo en su mismo dominio, reproduce `Summon_Submerge` (`Trigger "Submerge"`, `Float "SubmergeSpeed"`) hundiéndose `-4.4 m` en el charco de sombras (`MahoragaSummonEffect`), hace `Warp` frente al jugador y ejecuta `TryBeginSummonServer(..., forceSummon: true)` emergiendo desde cero frente a él.

---

## 2. Inventario Verificado de Compilación, Tests y Archivos Desplegados

| Artefacto | Ruta | Tamaño | SHA-256 / Estado |
| :--- | :--- | :--- | :--- |
| **Plugin DLL (Release, NetcodePatcher IL-woven)** | `Plugin/bin/Release/netstandard2.1/Fede.MahoragaLC.dll` | `178,688` bytes | `e7447026c785ebbe223c85e4ff0ab95b1d28dad331ea27c2e3dac28f3d42182f` |
| **AssetBundle Unity (19 assets, 10 clips, 4 spawn assets)** | `Plugin/bin/Release/netstandard2.1/mahoraga_assets` | `6,144,150` bytes | `f712202322e16d3eedd474f1c88ae5d4b3b50d36a1945dda7fa1592ea1e76176` |
| **FBX Exportado (10 clips @ 30fps)** | `assets/model/Mahoraga.fbx` | `11,027,068` bytes | `4deab0b55af3feee662e172fff7d0c46e144a9088c72228ee4cbefe55bb7324a` |
| **Paquete Thunderstore** | `Plugin/Thunderstore/Packages/MahoragaTeam-MahoragaLC-1.0.0.zip` | `6,416,682` bytes | Contiene `icon.png`, `README.md`, `CHANGELOG.md`, `manifest.json`, `Fede.MahoragaLC.dll`, `mahoraga_assets` |
| **Unit Tests (`MahoragaLC.Tests`)** | `Plugin/Tests/MahoragaLC.Tests.csproj` | 36 tests | **`36 passed, 0 failed, 0 skipped`** |
| **Despliegue en `MahoragaDev`** | `~/.config/r2modmanPlus-local/LethalCompany/profiles/MahoragaDev/BepInEx/plugins/Fede-MahoragaLC/` | Paridad SHA-256 exacta | Respaldo previo en `~/Respaldos/Fede-MahoragaLC-prev-20261119-164344` y `Fede.MahoragaLC.cfg` rotado a `.cfg.prev` |

---

## 3. Detalle Crítico para Cualquier Otra IA (Parches Manuales en UnityProject)

> **IMPORTANTE:** Tal como documentó Claude en `sesión 2.md`, para que Unity 2022.3.62f2 compile `UnityProject` contra los scripts decompilados de Lethal Company v81, hay dos archivos bajo `UnityProject/Assets/Scripts/Assembly-CSharp/` que están ignorados en `.gitignore` y fueron parchados a mano localmente:
> 1. `UnityProject/Assets/Scripts/Assembly-CSharp/IVisibleThreat.cs` -> incluye `GrabbableObject GetHeldObject();` y `bool IsThreatDead();`.
> 2. `UnityProject/Assets/Scripts/Assembly-CSharp/ThreatType.cs` -> incluye `GiantKiwi = 9`.
>
> **NUNCA sobrescribas ni borres esos dos archivos** si regenerás scripts del juego en `UnityProject`.

---

## 4. Checklist de Prueba en el Juego (Playtest #2) y Qué Queda por Hacer

### Checklist para probar en `MahoragaDev`:
1. **Spawn Interior y Exterior**: Al iniciar una luna (o con Imperium), verificar en `BepInEx/LogOutput.log` que `MahoragaLC` registra tanto el prefab interior (`MahoragaPrefab`, rarity `20`) como el exterior (`MahoragaPrefab_Outside`, `AllowOutdoorSpawn=True`, rarity `14`).
2. **Pelea contra Old Birds (`RadMech`)**: Spawnear un Old Bird y un Mahoraga en exterior:
   - Mahoraga debe priorizar inmediatamente al Old Bird por encima del jugador.
   - El Old Bird debe detectar a Mahoraga, apuntarle al torso (incluso de cerca a `< 8 m`) y sus misiles deben explotar al impactar contra el cuerpo de Mahoraga.
   - Cada impacto del Old Bird sobre Mahoraga debe girar la rueda Dharma **un solo rayo (`+45°`, de una bola a la siguiente)** con su animación y sonido, mientras Mahoraga crece en exterior.
3. **Percepción Exterior y Shadow Ambush**:
   - En exterior, si Mahoraga no te ve ni te escucha, solo patrulla sin wallhack.
   - Tarde en la partida (`>= 62%` del día, sin Old Birds vivos), tras perderte de vista por `12 s`, Mahoraga ejecuta `Summon_Submerge` (se hunde en su sombra) y emerge desde cero frente a un jugador aleatorio.
4. **Anti-Clipping**: Verificar en pasillos interiores, barandas y puertas cerradas que Mahoraga no atraviese paredes ni puertas antes de que se abran.

---

## 5. Estado de GitHub y Thunderstore

- **GitHub ()**: Sincronizado y pusheado en el commit `5727608`.
- **Thunderstore**: Token presente en ~/.config/thunderstore/token.env; binario tcli no instalado en PATH (ver sección 5 de Finish-session2.md para comando exacto o subida vía API/web con MahoragaTeam-MahoragaLC-1.0.0.zip)
  - Paquete listo y verificado: `Plugin/Thunderstore/Packages/MahoragaTeam-MahoragaLC-1.0.0.zip` (`6,416,682` bytes).
  - Credencial configurada en: `~/.config/thunderstore/token.env`.
