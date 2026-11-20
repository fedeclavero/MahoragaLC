using System;
using HarmonyLib;

namespace MahoragaLC.Patches
{
    [HarmonyPatch]
    internal static class ExplosionTrackingPatch
    {
        [ThreadStatic] private static int _explosionDepth;
        [ThreadStatic] private static uint _blastSequence;
        [ThreadStatic] private static RadMechAI? _missileSource;
        [ThreadStatic] private static StunGrenadeItem? _grenade;
        [ThreadStatic] private static int _flashDepth;

        internal static bool InExplosion => _explosionDepth > 0;
        internal static RadMechAI? MissileSource => _explosionDepth == 1 ? _missileSource : null;
        internal static uint MissileBlastId => MissileSource != null ? _blastSequence : 0;
        internal static uint LastOldBirdBlastId => _blastSequence;
        internal static StunGrenadeItem? FlashGrenade => _grenade;
        internal static bool InFlash => _flashDepth > 0;

        [HarmonyPatch(typeof(Landmine), nameof(Landmine.SpawnExplosion))]
        [HarmonyPrefix]
        private static void EnterExplosion()
        {
            if (_explosionDepth++ == 0 && _missileSource != null)
            {
                _blastSequence++;
                if (_blastSequence == 0)
                {
                    _blastSequence++;
                }
            }
        }

        [HarmonyPatch(typeof(Landmine), nameof(Landmine.SpawnExplosion))]
        [HarmonyFinalizer]
        private static void LeaveExplosion() => _explosionDepth--;

        [HarmonyPatch(typeof(RadMechAI), nameof(RadMechAI.SetExplosion))]
        [HarmonyPrefix]
        private static void EnterMissile(RadMechAI __instance, out RadMechAI? __state) => EnterOldBirdBlast(__instance, out __state);

        [HarmonyPatch(typeof(RadMechAI), nameof(RadMechAI.SetExplosion))]
        [HarmonyFinalizer]
        private static void LeaveMissile(RadMechAI? __state) => _missileSource = __state;

        [HarmonyPatch(typeof(RadMechAI), "DoFootstepCycle")]
        [HarmonyPrefix]
        private static void EnterCharge(RadMechAI __instance, out RadMechAI? __state) => EnterOldBirdBlast(__instance, out __state);

        [HarmonyPatch(typeof(RadMechAI), "DoFootstepCycle")]
        [HarmonyFinalizer]
        private static void LeaveCharge(RadMechAI? __state) => _missileSource = __state;

        private static void EnterOldBirdBlast(RadMechAI source, out RadMechAI? previous)
        {
            previous = _missileSource;
            _missileSource = source;
        }

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
