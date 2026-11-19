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

        // Old Bird (RadMechAI) missile blasts, tracked separately so Mahoraga can give them their own adaptation category.
        private struct MissileEvent
        {
            public Vector3 Position;
            public float Time;
            public float Range;
            public EnemyAI? Source;
        }

        private static readonly MissileEvent[] MissileRing = new MissileEvent[BufferCapacity];
        private static int _missileCursor;

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

        // RadMechAI.SetExplosion runs on the host machine for every missile impact (server path and client-reported path)
        // right before Landmine.SpawnExplosion, which is what damages enemies via HitEnemyOnLocalClient(6).
        [HarmonyPatch(typeof(RadMechAI), nameof(RadMechAI.SetExplosion))]
        [HarmonyPrefix]
        private static void OnRadMechSetExplosion(RadMechAI __instance, Vector3 explosionPosition, Vector3 forwardRotation)
        {
            if (__instance == null)
            {
                return;
            }

            MissileRing[_missileCursor] = new MissileEvent
            {
                Position = explosionPosition - forwardRotation * 0.1f,
                Time = Time.time,
                Range = 7f,
                Source = __instance
            };
            _missileCursor = (_missileCursor + 1) % BufferCapacity;
        }

        /// <summary>True when an Old Bird missile detonated close to <paramref name="worldPos"/> in the last <paramref name="maxAge"/> seconds.</summary>
        public static bool HasRecentMissileNear(Vector3 worldPos, float now, out EnemyAI? source, float maxAge = 0.25f, float extraMargin = 2.0f)
        {
            source = null;
            float bestDistSq = float.MaxValue;
            bool found = false;
            for (int i = 0; i < BufferCapacity; i++)
            {
                MissileEvent evt = MissileRing[i];
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
                float distSq = (worldPos - evt.Position).sqrMagnitude;
                if (distSq <= maxDist * maxDist && distSq < bestDistSq)
                {
                    bestDistSq = distSq;
                    source = evt.Source;
                    found = true;
                }
            }

            return found;
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
