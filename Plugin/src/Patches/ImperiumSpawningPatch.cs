using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BepInEx.Bootstrap;
using HarmonyLib;
using MahoragaLC.AI;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;
using NetworkPrefabs = LethalLib.Modules.NetworkPrefabs;
using Object = UnityEngine.Object;

namespace MahoragaLC.Patches
{
    internal static class ImperiumSpawningPatch
    {
        internal const string PluginGuid = "giosuel.Imperium";

        private static EnemyType _indoorMahoraga = null!;
        private static EnemyType? _outdoorMahoraga;
        private static GameObject? _oldBirdPrefab;
        private static GameObject? _manualOldBirdPrefab;

        internal static void Install(Harmony harmony, EnemyType indoor, EnemyType? outdoor)
        {
            _indoorMahoraga = indoor;
            _outdoorMahoraga = outdoor;

            // All MahoragaLC peers need the same prefab hash, including peers without Imperium.
            harmony.Patch(
                AccessTools.Method(typeof(GameNetworkManager), "Start"),
                postfix: new HarmonyMethod(typeof(ImperiumSpawningPatch), nameof(RegisterManualOldBirdPrefab)));

            if (!Chainloader.PluginInfos.TryGetValue(PluginGuid, out var imperium) || imperium.Instance == null)
            {
                return;
            }

            Type? objectManager = imperium.Instance.GetType().Assembly.GetType("Imperium.Core.Lifecycle.ObjectManager");
            MethodInfo? spawn = objectManager == null ? null : AccessTools.DeclaredMethod(objectManager, "OnSpawnEntity");
            MethodInfo? catalog = objectManager == null ? null : AccessTools.DeclaredMethod(objectManager, "FetchGlobalSpawnLists");
            if (spawn == null || catalog == null)
            {
                Plugin.Logger.LogWarning("Imperium's entity spawning methods were not found.");
                return;
            }

            try
            {
                harmony.Patch(spawn, transpiler: new HarmonyMethod(typeof(ImperiumSpawningPatch), nameof(PatchSpawn)));
                harmony.Patch(catalog, transpiler: new HarmonyMethod(typeof(ImperiumSpawningPatch), nameof(PatchCatalog)));
            }
            catch (Exception ex)
            {
                harmony.Unpatch(spawn, HarmonyPatchType.Transpiler, harmony.Id);
                harmony.Unpatch(catalog, HarmonyPatchType.Transpiler, harmony.Id);
                Plugin.Logger.LogWarning($"Could not install Imperium spawning compatibility: {ex.Message}");
            }
        }

        private static void RegisterManualOldBirdPrefab()
        {
            NetworkManager manager = NetworkManager.Singleton;
            if (manager == null)
            {
                return;
            }

            if (_manualOldBirdPrefab == null)
            {
                _oldBirdPrefab = manager.NetworkConfig.Prefabs.Prefabs
                    .Select(entry => entry.Prefab)
                    .FirstOrDefault(prefab => prefab != null && prefab.TryGetComponent(out RadMechAI bird)
                        && bird.enemyType != null && bird.enemyType.enemyPrefab == prefab);
                if (_oldBirdPrefab == null)
                {
                    return;
                }

                // EnemyAI.Start moves a vanilla Old Bird to its dormant map object, or destroys it
                // if that object is required and absent. Only the manual prefab bypasses that logic.
                EnemyType manualType = Object.Instantiate(_oldBirdPrefab.GetComponent<RadMechAI>().enemyType);
                manualType.name = "MahoragaLC_ManualOldBirdType";
                manualType.hideFlags = HideFlags.HideAndDontSave;
                manualType.nestSpawnPrefab = null;
                manualType.requireNestObjectsToSpawn = false;
                manualType.PowerLevel = 0f;
                manualType.enemyPrefab = null; // Keep the internal type out of global spawn menus.

                _manualOldBirdPrefab = NetworkPrefabs.CloneNetworkPrefab(_oldBirdPrefab, "MahoragaLC_ManualOldBird");
                _manualOldBirdPrefab.GetComponent<RadMechAI>().enemyType = manualType;
            }

            if (!manager.NetworkConfig.Prefabs.Contains(_manualOldBirdPrefab))
            {
                manager.AddNetworkPrefab(_manualOldBirdPrefab);
            }
        }

        private static IEnumerable<CodeInstruction> PatchCatalog(IEnumerable<CodeInstruction> instructions)
        {
            Func<EnemyType[]> findEnemies = Resources.FindObjectsOfTypeAll<EnemyType>;
            return ReplaceCall(instructions, findEnemies.Method, nameof(FindManuallySpawnableEnemyTypes));
        }

        private static IEnumerable<CodeInstruction> PatchSpawn(IEnumerable<CodeInstruction> instructions)
        {
            Func<GameObject, Vector3, Quaternion, GameObject> instantiate = Object.Instantiate<GameObject>;
            return ReplaceCall(instructions, instantiate.Method, nameof(InstantiateManualEnemy));
        }

        private static IEnumerable<CodeInstruction> ReplaceCall(
            IEnumerable<CodeInstruction> instructions, MethodInfo original, string replacement)
        {
            var codes = instructions.ToList();
            var call = codes.SingleOrDefault(code => code.Calls(original));
            if (call == null)
            {
                throw new InvalidOperationException($"Imperium no longer calls {original.Name} at the expected spawn hook.");
            }

            call.operand = AccessTools.DeclaredMethod(typeof(ImperiumSpawningPatch), replacement);
            return codes;
        }

        private static EnemyType[] FindManuallySpawnableEnemyTypes()
        {
            // Imperium draws one row per EnemyType but its spawn request carries only enemyName.
            return Resources.FindObjectsOfTypeAll<EnemyType>().Where(type => type != _outdoorMahoraga).ToArray();
        }

        private static GameObject InstantiateManualEnemy(GameObject prefab, Vector3 position, Quaternion rotation)
        {
            if (prefab == _oldBirdPrefab && _manualOldBirdPrefab != null)
            {
                prefab = _manualOldBirdPrefab;
                if (prefab.TryGetComponent(out NavMeshAgent agent))
                {
                    var filter = new NavMeshQueryFilter { agentTypeID = agent.agentTypeID, areaMask = agent.areaMask };
                    if (NavMesh.SamplePosition(position, out NavMeshHit ground, 8f, filter))
                    {
                        position = ground.position;
                    }
                }
            }
            else if (prefab == _indoorMahoraga.enemyPrefab || prefab == _outdoorMahoraga?.enemyPrefab)
            {
                if (_outdoorMahoraga != null)
                {
                    prefab = HuntLogic.IsOutsideAt(position.y, currentlyOutside: false)
                        ? _outdoorMahoraga.enemyPrefab
                        : _indoorMahoraga.enemyPrefab;
                }

                GameObject mahoraga = Object.Instantiate(prefab, position, rotation);
                // Imperium does not charge the moon's power budget for manual spawns.
                mahoraga.GetComponent<MahoragaAI>().removedPowerLevel = true;
                return mahoraga;
            }

            return Object.Instantiate(prefab, position, rotation);
        }
    }
}
