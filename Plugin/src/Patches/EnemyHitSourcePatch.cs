using System;
using HarmonyLib;
using MahoragaLC.AI;
using UnityEngine;

namespace MahoragaLC.Patches
{
    [HarmonyPatch(typeof(EnemyAICollisionDetect), "OnTriggerStay")]
    internal static class EnemyHitSourcePatch
    {
        [ThreadStatic] private static EnemyAI? _attacker;
        [ThreadStatic] private static MahoragaAI? _victim;

        internal static EnemyAI? GetAttacker(MahoragaAI victim) => _victim == victim ? _attacker : null;

        private static void Prefix(EnemyAICollisionDetect __instance, Collider other,
            out (EnemyAI? Attacker, MahoragaAI? Victim) __state)
        {
            __state = (_attacker, _victim);
            if (__instance.mainScript != null && __instance.mainScript is not MahoragaAI &&
                __instance.canCollideWithEnemies && other != null &&
                other.TryGetComponent(out EnemyAICollisionDetect detector) &&
                detector.mainScript is MahoragaAI victim && victim.IsServer)
            {
                _attacker = __instance.mainScript;
                _victim = victim;
            }
            else
            {
                _attacker = null;
                _victim = null;
            }
        }

        private static void Finalizer((EnemyAI? Attacker, MahoragaAI? Victim) __state)
        {
            (_attacker, _victim) = __state;
        }
    }
}
