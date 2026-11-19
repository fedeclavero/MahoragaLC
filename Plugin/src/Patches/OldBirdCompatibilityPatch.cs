using HarmonyLib;
using MahoragaLC.AI;
using UnityEngine;

namespace MahoragaLC.Patches
{
    /// <summary>
    /// Fixes vanilla RadMechAI (Old Bird) and RadMechMissile incompatibilities against MahoragaAI without altering any
    /// vanilla enemy or player behavior. Every hook exits untouched unless the active threat or hit collider belongs
    /// to a living, non-summoning/non-submerging MahoragaAI.
    /// </summary>
    [HarmonyPatch]
    internal static class OldBirdCompatibilityPatch
    {
        private const int EnemyLayerMask = 1 << 19; // 524288: Enemies
        private const int SolidWorldAndEnemyMask = 526592; // (1 << 8) Room | (1 << 11) Colliders | (1 << 19) Enemies
        private const int PlayerLayerMask = 1 << 3; // 8: Player
        private const float ChestHeightMeters = 1.8f;

        private static readonly RaycastHit[] MissileHitsBuffer = new RaycastHit[8];
        private static readonly Collider[] MissileOverlapBuffer = new Collider[8];

        private static bool TryGetActiveMahoraga(IVisibleThreat? threat, out MahoragaAI mahoraga)
        {
            if (threat is MahoragaAI m && m != null && !m.isEnemyDead && !m.IsSummonOrSubmergeActive)
            {
                mahoraga = m;
                return true;
            }

            mahoraga = null!;
            return false;
        }

        private static Vector3 GetChestPosition(MahoragaAI mahoraga)
        {
            return mahoraga.transform.position + Vector3.up * (ChestHeightMeters * mahoraga.ScaleFactor);
        }

        private static Vector3 GetGroundApproachPosition(RadMechAI oldBird, MahoragaAI mahoraga)
        {
            Vector3 rawPos;
            if (oldBird.hasLOS)
            {
                rawPos = mahoraga.transform.position;
            }
            else
            {
                Vector3 lastSeen = oldBird.targetedThreat.lastSeenPosition;
                if (StartOfRound.Instance != null
                    && Physics.Raycast(lastSeen, Vector3.down, out RaycastHit hit, 10f, StartOfRound.Instance.collidersAndRoomMaskAndDefault, QueryTriggerInteraction.Ignore))
                {
                    rawPos = hit.point;
                }
                else
                {
                    rawPos = new Vector3(lastSeen.x, mahoraga.transform.position.y, lastSeen.z);
                }
            }

            if (RoundManager.Instance != null)
            {
                Vector3 sampled = RoundManager.Instance.GetNavMeshPosition(rawPos, RoundManager.Instance.navHit, 4f, oldBird.agentMask);
                if (RoundManager.Instance.GotNavMeshPositionResult)
                {
                    return sampled;
                }
            }

            return rawPos;
        }

        private static void UpdateMahoragaChestLastSeen(RadMechAI instance)
        {
            if (instance == null
                || !instance.hasLOS
                || instance.focusedThreatTransform == null
                || instance.targetedThreat == null
                || !TryGetActiveMahoraga(instance.targetedThreat.threatScript, out MahoragaAI m))
            {
                return;
            }

            instance.targetedThreat.lastSeenPosition = GetChestPosition(m) + instance.targetedThreat.threatScript.GetThreatVelocity();
        }

        /// <summary>
        /// Checks LOS against Mahoraga's chest with wider vertical angle (70°) and proximity awareness (12m) so
        /// Old Birds do not lose LOS inside their 8.85m downward blind spot, and paths along ground coordinates.
        /// </summary>
        [HarmonyPatch(typeof(RadMechAI), nameof(RadMechAI.MoveTowardsThreat))]
        [HarmonyPrefix]
        private static bool OnMoveTowardsThreat(RadMechAI __instance)
        {
            if (__instance == null
                || __instance.targetedThreat == null
                || __instance.focusedThreatTransform == null
                || !TryGetActiveMahoraga(__instance.targetedThreat.threatScript, out MahoragaAI m))
            {
                return true;
            }

            Vector3 chestPos = GetChestPosition(m);
            Vector3 navPos = GetGroundApproachPosition(__instance, m);

            if (!__instance.lostCreatureInChase)
            {
                if (__instance.lostCreatureInChaseDebounce)
                {
                    __instance.lostCreatureInChaseDebounce = false;
                    __instance.SetLostCreatureInChaseClientRpc(lostInChase: false);
                    __instance.losTimer = 0f;
                }

                if (__instance.searchForPlayers != null && __instance.searchForPlayers.inProgress)
                {
                    __instance.StopSearch(__instance.searchForPlayers);
                }

                float visibility = __instance.targetedThreat.threatScript.GetVisibility();
                __instance.hasLOS = visibility > 0f && __instance.CheckLineOfSightForPosition(chestPos, 70f, 60, 12f);

                if (__instance.hasLOS)
                {
                    __instance.targetedThreat.lastSeenPosition = chestPos + __instance.targetedThreat.threatScript.GetThreatVelocity();
                }

                if (__instance.hasLOS || (__instance.losTimer < 2f && Vector3.Distance(chestPos, __instance.transform.position) < 14f))
                {
                    __instance.losTimer = 0f;
                    __instance.alertTimer += __instance.AIIntervalTime * Mathf.Max(visibility, 0.3f);
                }
                else
                {
                    if (!__instance.aimingGun)
                    {
                        __instance.losTimer += __instance.AIIntervalTime;
                        __instance.alertTimer = Mathf.Max(__instance.alertTimer - __instance.AIIntervalTime, 0f);
                    }

                    if (__instance.CheckSightForThreat())
                    {
                        __instance.hasLOS = true;
                        return false;
                    }
                }

                if (__instance.hasLOS != __instance.SyncedLOSState)
                {
                    __instance.syncLOSInterval += __instance.AIIntervalTime;
                    if (__instance.syncLOSInterval > 0.6f)
                    {
                        __instance.SyncedLOSState = __instance.hasLOS;
                        __instance.SetHasLineOfSightClientRpc(__instance.hasLOS);
                    }
                }

                __instance.checkForPathInterval = (__instance.checkForPathInterval + 1) % 10;
                bool pathSet = __instance.SetDestinationToPosition(navPos, __instance.checkForPathInterval == 0);
                bool lostAtDest = !__instance.hasLOS
                    && __instance.agent != null
                    && Vector3.Distance(__instance.agent.destination, __instance.transform.position) < 1.75f;
                if (!pathSet || lostAtDest || __instance.losTimer > 10f)
                {
                    __instance.lostCreatureInChase = true;
                }

                return false;
            }

            if (!__instance.lostCreatureInChaseDebounce)
            {
                __instance.lostCreatureInChaseDebounce = true;
                __instance.losTimer = 0f;
                __instance.SetLostCreatureInChaseClientRpc(lostInChase: true);
                __instance.SetChargingForwardOnLocalClient(charging: false);
                if (__instance.searchForPlayers != null && !__instance.searchForPlayers.inProgress)
                {
                    __instance.StartSearch(__instance.transform.position, __instance.searchForPlayers);
                }
            }

            float regainVisibility = __instance.targetedThreat.threatScript.GetVisibility();
            if (regainVisibility > 0f
                && __instance.CheckLineOfSightForPosition(chestPos, 70f, 60, 12f)
                && __instance.SetDestinationToPosition(navPos, checkForPath: true))
            {
                __instance.targetedThreat.lastSeenPosition = chestPos;
                __instance.hasLOS = true;
                __instance.lostCreatureInChase = false;
            }
            else if (__instance.CheckSightForThreat())
            {
                __instance.lostCreatureInChase = false;
            }
            else if (!__instance.aimingGun)
            {
                __instance.losTimer += __instance.AIIntervalTime;
                if (__instance.losTimer > 10f || (__instance.losTimer > 5f && Vector3.Distance(__instance.transform.position, navPos) < 2f))
                {
                    __instance.losTimer = 0f;
                    __instance.SwitchToBehaviourState(0);
                }
            }

            return false;
        }

        /// <summary>
        /// Keeps targetedThreat.lastSeenPosition pointed at Mahoraga's chest right before AimGunArmTowardsTarget and
        /// TurnTorsoToTarget run inside RadMechAI.Update (vanilla resets it to feet Y=0 earlier in Update).
        /// </summary>
        [HarmonyPatch(typeof(RadMechAI), nameof(RadMechAI.AimGunArmTowardsTarget))]
        [HarmonyPrefix]
        private static void OnAimGunArmPrefix(RadMechAI __instance)
        {
            UpdateMahoragaChestLastSeen(__instance);
        }

        [HarmonyPatch(typeof(RadMechAI), nameof(RadMechAI.Update))]
        [HarmonyPostfix]
        private static void OnUpdatePostfix(RadMechAI __instance)
        {
            UpdateMahoragaChestLastSeen(__instance);
        }

        /// <summary>
        /// Lowers the close-range firing abort threshold from 6m to 1.5m when targeting Mahoraga so Old Birds keep
        /// firing missile salvos in melee combat instead of stalling for a player-only grab.
        /// </summary>
        [HarmonyPatch(typeof(RadMechAI), nameof(RadMechAI.AimAndShootCycle))]
        [HarmonyPrefix]
        private static bool OnAimAndShootCycle(RadMechAI __instance)
        {
            if (__instance == null
                || __instance.targetedThreat == null
                || !TryGetActiveMahoraga(__instance.targetedThreat.threatScript, out MahoragaAI _))
            {
                return true;
            }

            if (__instance.takingStep || !(__instance.stunNormalizedTimer <= 0f))
            {
                return false;
            }

            if (__instance.creatureAnimator != null)
            {
                __instance.creatureAnimator.SetBool("AimGun", value: true);
            }

            if (!__instance.IsServer)
            {
                return false;
            }

            if (__instance.shootTimer > __instance.fireRate)
            {
                float dist = Vector3.Distance(__instance.targetedThreat.lastSeenPosition, __instance.transform.position);
                if (__instance.shootCooldown > 4.75f || __instance.chargingForward)
                {
                    __instance.SetAimingGun(setAiming: false);
                    return false;
                }

                if (!__instance.hadLOSDuringLastShot && (!__instance.hasLOS || dist < 1.5f))
                {
                    __instance.SetAimingGun(setAiming: false);
                    return false;
                }

                __instance.shootTimer = 0f + Random.Range((0f - __instance.fireRateVariance) * 0.5f, __instance.fireRateVariance * 0.5f);
                __instance.StartShootGun();
                __instance.hadLOSDuringLastShot = __instance.hasLOS && dist < 40f;
            }
            else
            {
                __instance.shootTimer += Time.deltaTime;
            }

            return false;
        }

        /// <summary>
        /// Vanilla RadMechMissile raycasts Layer 19 with QueryTriggerInteraction.Ignore, causing missiles to pass through
        /// Mahoraga's trigger CollisionCollider. Detonates on living active MahoragaAI hits when no wall/player is closer.
        /// </summary>
        [HarmonyPatch(typeof(RadMechMissile), "CheckCollision")]
        [HarmonyPrefix]
        private static bool OnMissileCheckCollision(RadMechMissile __instance)
        {
            if (__instance == null || __instance.hitWall || __instance.RadMechScript == null)
            {
                return true;
            }

            float rayDist = 0.6f * __instance.currentMissileSpeed;
            Vector3 origin = __instance.transform.position;
            Vector3 forward = __instance.transform.forward;

            MahoragaAI? hitMahoraga = null;
            Collider? hitCollider = null;
            float closestMahoragaDist = float.MaxValue;

            int triggerHits = Physics.RaycastNonAlloc(origin, forward, MissileHitsBuffer, rayDist, EnemyLayerMask, QueryTriggerInteraction.Collide);
            for (int i = 0; i < triggerHits; i++)
            {
                RaycastHit hit = MissileHitsBuffer[i];
                if (hit.collider == null)
                {
                    continue;
                }

                EnemyAICollisionDetect? detect = hit.collider.GetComponent<EnemyAICollisionDetect>();
                if (detect != null
                    && detect.mainScript is MahoragaAI m
                    && !m.isEnemyDead
                    && !m.IsSummonOrSubmergeActive
                    && hit.distance < closestMahoragaDist)
                {
                    closestMahoragaDist = hit.distance;
                    hitMahoraga = m;
                    hitCollider = hit.collider;
                }
            }

            if (hitMahoraga == null)
            {
                int overlapCount = Physics.OverlapSphereNonAlloc(origin, 0.15f, MissileOverlapBuffer, EnemyLayerMask, QueryTriggerInteraction.Collide);
                for (int i = 0; i < overlapCount; i++)
                {
                    Collider col = MissileOverlapBuffer[i];
                    if (col == null)
                    {
                        continue;
                    }

                    EnemyAICollisionDetect? detect = col.GetComponent<EnemyAICollisionDetect>();
                    if (detect != null
                        && detect.mainScript is MahoragaAI m
                        && !m.isEnemyDead
                        && !m.IsSummonOrSubmergeActive)
                    {
                        hitMahoraga = m;
                        hitCollider = col;
                        closestMahoragaDist = 0f;
                        break;
                    }
                }
            }

            if (hitMahoraga == null || hitCollider == null)
            {
                return true;
            }

            if (closestMahoragaDist > 0f)
            {
                if (Physics.Raycast(origin, forward, out RaycastHit solidHit, closestMahoragaDist, SolidWorldAndEnemyMask, QueryTriggerInteraction.Ignore)
                    && solidHit.distance < closestMahoragaDist)
                {
                    return true;
                }

                if (Physics.Raycast(origin, forward, out RaycastHit playerHit, closestMahoragaDist, PlayerLayerMask, QueryTriggerInteraction.Collide)
                    && playerHit.distance < closestMahoragaDist)
                {
                    return true;
                }
            }

            __instance.hitWall = true;
            Vector3 startExplosionPos = origin - forward * 0.5f;
            __instance.RadMechScript.StartExplosion(startExplosionPos, forward, calledByClient: false);

            // Landmine.SpawnExplosion measures distance to CollisionCollider.transform.position (at Y=0 feet) and skips
            // enemies >= 4.5m away. When Mahoraga grows outdoors (up to ~7m tall), direct upper-torso hits exceed 4.5m
            // from feet, so forward the direct hit once if vanilla SpawnExplosion skipped it.
            if (hitMahoraga.IsOwner)
            {
                Vector3 actualExplosionPos = startExplosionPos - forward * 0.1f;
                float feetDist = Vector3.Distance(actualExplosionPos, hitCollider.transform.position);
                bool linecastBlocked = Physics.Linecast(
                        actualExplosionPos,
                        hitCollider.transform.position + Vector3.up * 0.3f,
                        out RaycastHit blockHit,
                        1073742080,
                        QueryTriggerInteraction.Ignore)
                    && (blockHit.collider.gameObject.layer == 30 || feetDist > 4f);

                if (linecastBlocked || feetDist >= 4.5f)
                {
                    hitMahoraga.HitEnemyOnLocalClient(6);
                    hitMahoraga.HitFromExplosion(Mathf.Min(feetDist, 4.4f));
                }
            }

            Object.Destroy(__instance.gameObject);
            return false;
        }
    }
}
