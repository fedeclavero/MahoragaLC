using HarmonyLib;
using UnityEngine;

namespace MahoragaLC.Patches
{
    [HarmonyPatch]
    internal static class ExplosionTrackingPatch
    {
        private struct TimedEvent
        {
            public Vector3 Position;
            public float Time;
            public float Range;
        }

        private const int BufferCapacity = 16;
        private static readonly TimedEvent[] ExplosionRing = new TimedEvent[BufferCapacity];
        private static int _explosionCursor;

        private static readonly TimedEvent[] FlashRing = new TimedEvent[BufferCapacity];
        private static int _flashCursor;

        [HarmonyPatch(typeof(Landmine), nameof(Landmine.SpawnExplosion))]
        [HarmonyPrefix]
        private static void OnSpawnExplosion(Vector3 explosionPosition, float damageRange)
        {
            ExplosionRing[_explosionCursor] = new TimedEvent
            {
                Position = explosionPosition,
                Time = Time.time,
                Range = Mathf.Max(damageRange, 4.5f)
            };
            _explosionCursor = (_explosionCursor + 1) % BufferCapacity;
        }

        [HarmonyPatch(typeof(StunGrenadeItem), "ExplodeStunGrenade")]
        [HarmonyPrefix]
        private static void OnExplodeStunGrenade(StunGrenadeItem __instance)
        {
            if (__instance == null || __instance.hasExploded)
            {
                return;
            }

            FlashRing[_flashCursor] = new TimedEvent
            {
                Position = __instance.transform.position,
                Time = Time.time,
                Range = 13.0f
            };
            _flashCursor = (_flashCursor + 1) % BufferCapacity;
        }

        public static bool HasRecentExplosionNear(Vector3 worldPos, float now, float maxAge = 0.25f, float extraMargin = 5.0f)
        {
            for (int i = 0; i < BufferCapacity; i++)
            {
                TimedEvent evt = ExplosionRing[i];
                if (evt.Range <= 0f)
                {
                    continue;
                }

                float age = now - evt.Time;
                if (age < 0f || age > maxAge)
                {
                    continue;
                }

                float maxDist = evt.Range + extraMargin;
                if ((worldPos - evt.Position).sqrMagnitude <= maxDist * maxDist)
                {
                    return true;
                }
            }

            return false;
        }

        public static bool HasRecentFlashNear(Vector3 worldPos, float now, float maxAge = 0.25f, float extraMargin = 4.0f)
        {
            for (int i = 0; i < BufferCapacity; i++)
            {
                TimedEvent evt = FlashRing[i];
                if (evt.Range <= 0f)
                {
                    continue;
                }

                float age = now - evt.Time;
                if (age < 0f || age > maxAge)
                {
                    continue;
                }

                float maxDist = evt.Range + extraMargin;
                if ((worldPos - evt.Position).sqrMagnitude <= maxDist * maxDist)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
