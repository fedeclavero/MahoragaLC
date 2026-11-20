using System;
using System.Collections.Generic;
using System.Reflection;
using GameNetcodeStuff;
using HarmonyLib;

namespace MahoragaLC.Patches
{
    // RoundManager delivers sound synchronously but its listener signature has no source.
    [HarmonyPatch]
    internal static class PlayerFootstepPatch
    {
        [ThreadStatic]
        private static PlayerControllerB? _source;

        internal static PlayerControllerB? Source => _source;

        private static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(PlayerControllerB), nameof(PlayerControllerB.PlayFootstepLocal));
            yield return AccessTools.Method(typeof(PlayerControllerB), nameof(PlayerControllerB.PlayFootstepServer));
        }

        private static void Prefix(PlayerControllerB __instance, out PlayerControllerB? __state)
        {
            __state = _source;
            _source = __instance;
        }

        private static void Finalizer(PlayerControllerB? __state)
        {
            _source = __state;
        }
    }
}
