using System;
using HarmonyLib;

namespace MahoragaLC.Patches
{
    [HarmonyPatch]
    internal static class ExplosionTrackingPatch
    {
        [ThreadStatic] private static int _explosionDepth;
        [ThreadStatic] private static RadMechAI? _missileSource;
        [ThreadStatic] private static StunGrenadeItem? _grenade;
        [ThreadStatic] private static int _flashDepth;

        internal static bool InExplosion => _explosionDepth > 0;
        internal static RadMechAI? MissileSource => _explosionDepth == 1 ? _missileSource : null;
        internal static StunGrenadeItem? FlashGrenade => _grenade;
        internal static bool InFlash => _flashDepth > 0;

        [HarmonyPatch(typeof(Landmine), nameof(Landmine.SpawnExplosion))]
        [HarmonyPrefix]
        private static void EnterExplosion() => _explosionDepth++;

        [HarmonyPatch(typeof(Landmine), nameof(Landmine.SpawnExplosion))]
        [HarmonyFinalizer]
        private static void LeaveExplosion() => _explosionDepth--;

        [HarmonyPatch(typeof(RadMechAI), nameof(RadMechAI.SetExplosion))]
        [HarmonyPrefix]
        private static void EnterMissile(RadMechAI __instance, out RadMechAI? __state)
        {
            __state = _missileSource;
            _missileSource = __instance;
        }

        [HarmonyPatch(typeof(RadMechAI), nameof(RadMechAI.SetExplosion))]
        [HarmonyFinalizer]
        private static void LeaveMissile(RadMechAI? __state) => _missileSource = __state;

        [HarmonyPatch(typeof(StunGrenadeItem), nameof(StunGrenadeItem.StunExplosion))]
        [HarmonyPrefix]
        private static void EnterFlash() => _flashDepth++;

        [HarmonyPatch(typeof(StunGrenadeItem), nameof(StunGrenadeItem.StunExplosion))]
        [HarmonyFinalizer]
        private static void LeaveFlash() => _flashDepth--;

        [HarmonyPatch(typeof(StunGrenadeItem), "ExplodeStunGrenade")]
        [HarmonyPrefix]
        private static void EnterGrenade(StunGrenadeItem __instance, out StunGrenadeItem? __state)
        {
            __state = _grenade;
            _grenade = __instance;
        }

        [HarmonyPatch(typeof(StunGrenadeItem), "ExplodeStunGrenade")]
        [HarmonyFinalizer]
        private static void LeaveGrenade(StunGrenadeItem? __state) => _grenade = __state;
    }
}
