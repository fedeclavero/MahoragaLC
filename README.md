# Mahoraga LC

Mod de **Lethal Company** que agrega a **Mahoraga** (el General Divino de las Diez Sombras, de *Jujutsu Kaisen*) como enemigo: un gigante que te caza por la instalación, fuerza puertas con llave y se **adapta** a cada arma con la que lo golpeás girando su Rueda del Dharma.

> **En desarrollo activo.** La versión 1.1.1 ya incluye los sistemas principales del encuentro. Seguimos afinando el balance, la navegación y la compatibilidad con mapas y mods.

## Cómo funciona el encuentro

- **Percepción.** Patrulla zonas navegables en interiores y exteriores. Reacciona a sonidos del juego y detecta a un jugador silencioso a aproximadamente un metro, si hay paso libre. Agacharte silencia tus pasos. Las emboscadas tardías parten de una posición donde se oyó un ruido.
- **Movilidad.** Usa escaleras transitables y salta huecos cortos con aterrizaje validado. Tras crecer puede saltar al borde de la nave abierta si hay piso y despeje suficientes.
- **Otros enemigos.** Busca Old Birds en su zona. Contra otros mobs responde cuando un golpe permite identificar al agresor. En exterior, seis impactos distintos de misil llevan a Mahoraga a 1,5 veces la altura medida del Old Bird con la configuración por defecto.
- **Adaptación.** La pala, el cuchillo, la escopeta, los explosivos, los impactos directos del Cruiser y los misiles tienen progresiones propias. Cuatro jugadores pueden rematarlo con palas coordinadas en 0,75 s. Si la pala ya giró la rueda, el mismo equipo puede sumar un golpe adicional por etapa dentro de un total de 3 s. Antes de adaptarse a misiles, tres Old Birds pueden levantar su protección de vida. Cada etapa de misiles suma un Old Bird al requisito de la próxima ráfaga.
- **Aturdimiento.** Las dos primeras granadas aturdidoras duran lo que indica el juego. Desde la tercera, duran la mitad. Los aturdimientos temporales de equipo que llaman al método del juego tienen el mismo comportamiento por tipo de objeto identificable.

## Cuándo aparece

El host registra los perfiles interior y exterior en los pools de las lunas vanilla y modded que permiten encuentros. Los pesos base son 20 en interior y 14 en exterior. Cada aparición usa 3 puntos de poder y, por defecto, el juego permite una por dominio. La probabilidad exterior aumenta durante el día. El juego decide cuándo y dónde según la semilla, el presupuesto, el límite y los puntos válidos de cada mapa. Mahoraga puede aparecer en una luna sin Old Birds.

El host puede ajustar estos valores en `BepInEx/config/Fede.MahoragaLC.cfg`. `MoonRarityOverrides` permite cambiar el peso por luna. `OutdoorMoonRarityOverrides` tiene prioridad para la variante exterior. Un peso de cero elimina el peso normal de esa entrada.

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

El modelo de la edición pública se edita en `assets/model/public/Mahoraga.blend`. Su FBX listo para Unity está en `UnityProject/Assets/MahoragaMod/Model/`. El empaquetado de Thunderstore usa el bundle público preparado en `artifacts/thunderstore/mahoraga_assets`. El build de Release genera el ZIP cuando ese bundle está disponible. La edición pública usa el modelo original del mod y admite música local desde `CustomMusic/`.

## Qué no está en este repositorio

Por derechos de terceros, quedan fuera de git (hay que conseguirlos localmente):

- Los DLL del juego y de terceros en `UnityProject/Assets/Plugins/` (se copian desde `Lethal Company_Data/Managed/` y del perfil de BepInEx).
- Los scripts del juego en `UnityProject/Assets/Scripts/Assembly-CSharp/` (se regeneran con el Lethal Company Project Patcher; los `.meta` sí están versionados para conservar los GUID). Tienen que coincidir con la versión actual del juego (v81): en particular `IVisibleThreat` debe declarar `GetHeldObject()` e `IsThreatDead()` y `ThreatType` debe incluir `GiantKiwi = 9`; si no, Unity no puede cargar `MahoragaAI` y el build del AssetBundle falla con `TypeLoadException`.
- El código decompilado de referencia (`docs/re/`).
- La música `mahoraga_music_main.ogg`. El mod admite música propia en la carpeta `CustomMusic/`.

## Aviso

Proyecto de fans sin fines de lucro. *Jujutsu Kaisen* y Mahoraga pertenecen a Gege Akutami, Shueisha y MAPPA. *Lethal Company* pertenece a Zeekerss. Este proyecto no está afiliado a ninguno de ellos.
