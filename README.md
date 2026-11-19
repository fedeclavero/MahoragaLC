# Mahoraga LC

Mod de **Lethal Company** que agrega a **Mahoraga** (el General Divino de las Diez Sombras, de *Jujutsu Kaisen*) como enemigo: un gigante que te caza por la instalación, fuerza puertas con llave y se **adapta** a cada arma con la que lo golpeás girando su Rueda del Dharma.

> Estado: **alfa** (1.0.0). En desarrollo activo: modelo nuevo basado en referencias, invocación desde las sombras, combate contra otros enemigos (Old Birds incluidos) y adaptación progresiva.

## Estructura

| Carpeta | Contenido |
|---|---|
| `Plugin/` | Código C# del plugin (BepInEx 5 + LethalLib 1.2.0 + Netcode for GameObjects con NetcodePatcher), pruebas unitarias y metadatos de Thunderstore |
| `UnityProject/` | Proyecto Unity 2022.3.62f2 (HDRP 14.0.12) que arma el AssetBundle `mahoraga_assets` con `MahoragaAssetBuilder.BuildAll` |
| `assets/` | Modelo de Blender, FBX, texturas, renders y efectos de sonido |
| `docs/` | Diseño (GDD), arquitectura, contrato de assets, hallazgos de ingeniería inversa y QA |

## Compilar

```bash
cd Plugin && dotnet tool restore && dotnet build -c Release
```

El `.csproj` espera la instalación de Lethal Company en `ManagedDirectory` y LethalLib en `LethalLibPath` (se pueden pasar por línea de comandos con `-p:ManagedDirectory=...`).

AssetBundle (Unity en modo batch):

```bash
Unity -batchmode -nographics -projectPath UnityProject -executeMethod MahoragaAssetBuilder.BuildAll -quit -logFile UnityProject/build.log
```

## Qué no está en este repositorio

Por derechos de terceros, quedan fuera de git (hay que conseguirlos localmente):

- Los DLL del juego y de terceros en `UnityProject/Assets/Plugins/` (se copian desde `Lethal Company_Data/Managed/` y del perfil de BepInEx).
- Los scripts del juego en `UnityProject/Assets/Scripts/Assembly-CSharp/` (se regeneran con el Lethal Company Project Patcher; los `.meta` sí están versionados para conservar los GUID). Tienen que coincidir con la versión actual del juego (v81): en particular `IVisibleThreat` debe declarar `GetHeldObject()` e `IsThreatDead()` y `ThreatType` debe incluir `GiantKiwi = 9`; si no, Unity no puede cargar `MahoragaAI` y el build del AssetBundle falla con `TypeLoadException`.
- El código decompilado de referencia (`docs/re/`).
- La música `mahoraga_music_main.ogg`. El mod admite música propia en la carpeta `CustomMusic/`.

## Aviso

Proyecto de fans sin fines de lucro. *Jujutsu Kaisen* y Mahoraga pertenecen a Gege Akutami, Shueisha y MAPPA. *Lethal Company* pertenece a Zeekerss. Este proyecto no está afiliado a ninguno de ellos.
