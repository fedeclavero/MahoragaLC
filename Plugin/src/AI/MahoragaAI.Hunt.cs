using System.Collections.Generic;
using GameNetcodeStuff;
using MahoragaLC.Config;
using UnityEngine;
using UnityEngine.AI;

namespace MahoragaLC.AI
{
    /// <summary>
    /// Hunt: while patrolling without line of sight, Mahoraga walks to the closest living player of its own domain
    /// (inside or outside the facility, as decided by EnemyAI.PlayerIsTargetable) instead of wandering between AI
    /// nodes. Server only. Seeing someone still switches to Chasing exactly as before.
    ///
    /// Robustness:
    ///  - target and path are re-evaluated every HuntRepathSeconds, among the 3 nearest players;
    ///  - only fully reachable targets are accepted; a PathPartial / unsampled target is blocked for a while so
    ///    another player (or plain patrol) is used instead;
    ///  - wedged against geometry (no progress for 3 s with a long way to go) or "arrived but never saw anyone"
    ///    for 6 s block that target temporarily, forcing a different choice;
    ///  - locked doors are forced with the existing breach logic (TryScanForLockedDoorServer).
    /// </summary>
    public partial class MahoragaAI
    {
        private const float HuntPartialBlockSeconds = 6f;
        private const float HuntStuckBlockSeconds = 10f;
        private const float HuntArrivedBlockSeconds = 12f;
        private const float HuntStuckWindowSeconds = 3f;
        private const float HuntArrivedGiveUpSeconds = 6f;
        private const int HuntMaxPathChecks = 3;

        private readonly Dictionary<int, float> _huntBlockedUntil = new Dictionary<int, float>();
        private readonly List<HuntLogic.Candidate> _huntCandidates = new List<HuntLogic.Candidate>(8);
        private readonly Vector3[] _huntCorners = new Vector3[64];
        private NavMeshPath? _huntPath;

        private int _huntTargetClientId = -1;
        private float _huntNextRepathTime;
        private float _huntLastRunTime = -999f;
        private float _huntStuckWindowStart;
        private Vector3 _huntStuckStartPos;
        private float _huntArrivedSince = -1f;
        private Vector3 _huntDestinationPlayerPos;

        /// <summary>
        /// Returns true when the hunt took over this patrol tick (destination/speed set); false means "nobody to
        /// hunt" and the caller falls back to the normal node-to-node patrol.
        /// </summary>
        private bool TryRunHuntServer(MahoragaConfig? cfg)
        {
            if (cfg == null || isEnemyDead ||
                !HuntLogic.ShouldHuntWithoutLineOfSight(isOutside, cfg.HuntPlayers.Value, cfg.HuntPlayersOutdoors.Value))
            {
                return false;
            }

            if (agent == null || !agent.enabled || !agent.isOnNavMesh ||
                StartOfRound.Instance == null || RoundManager.Instance == null)
            {
                return false;
            }

            float now = Time.time;
            if (now - _huntLastRunTime > 1.0f)
            {
                // Hunt was not running (chase, investigation, door breach...): restart the stuck/arrival trackers.
                _huntStuckWindowStart = now;
                _huntStuckStartPos = transform.position;
                _huntArrivedSince = -1f;
            }

            _huntLastRunTime = now;

            PlayerControllerB? current = ResolvePlayerByClientId(_huntTargetClientId);
            bool targetValid = current != null
                && PlayerIsTargetable(current)
                && !IsHuntBlocked(_huntTargetClientId, now);

            if (!targetValid || now >= _huntNextRepathTime)
            {
                _huntNextRepathTime = now + Mathf.Max(1f, cfg.HuntRepathSeconds.Value);
                if (!SelectHuntTargetServer(now))
                {
                    _huntTargetClientId = -1;
                    return false;
                }

                current = ResolvePlayerByClientId(_huntTargetClientId);
                _huntStuckWindowStart = now;
                _huntStuckStartPos = transform.position;
                _huntArrivedSince = -1f;
            }
            else if (current != null &&
                     (current.transform.position - _huntDestinationPlayerPos).sqrMagnitude > 9f)
            {
                // The player moved a lot since the last destination: follow without waiting for the next repath.
                SetHuntDestination(current);
            }

            agent.speed = cfg.InvestigateSpeed.Value;
            openDoorSpeedMultiplier = 1.1f;

            UpdateHuntStuckAndArrival(now);
            TryScanForLockedDoorServer(cfg);
            return true;
        }

        private void UpdateHuntStuckAndArrival(float now)
        {
            if (agent == null)
            {
                return;
            }

            float window = now - _huntStuckWindowStart;
            if (window >= HuntStuckWindowSeconds)
            {
                Vector3 a = transform.position;
                Vector3 b = _huntStuckStartPos;
                float moved = new Vector2(a.x - b.x, a.z - b.z).magnitude;
                float remaining = agent.pathPending ? float.MaxValue : agent.remainingDistance;

                if (_forcingDoor == null && HuntLogic.IsStuck(moved, window, remaining))
                {
                    BlockHuntTarget(_huntTargetClientId, now + HuntStuckBlockSeconds);
                    _huntNextRepathTime = 0f;
                }

                _huntStuckWindowStart = now;
                _huntStuckStartPos = transform.position;
            }

            if (!agent.pathPending && agent.hasPath && agent.remainingDistance <= 2.6f)
            {
                if (_huntArrivedSince < 0f)
                {
                    _huntArrivedSince = now;
                }
                else if (now - _huntArrivedSince > HuntArrivedGiveUpSeconds)
                {
                    // Standing at the destination for ages without ever seeing the player (above/below, behind glass...).
                    BlockHuntTarget(_huntTargetClientId, now + HuntArrivedBlockSeconds);
                    _huntNextRepathTime = 0f;
                    _huntArrivedSince = -1f;
                }
            }
            else
            {
                _huntArrivedSince = -1f;
            }
        }

        private bool SelectHuntTargetServer(float now)
        {
            if (StartOfRound.Instance == null || agent == null)
            {
                return false;
            }

            _huntCandidates.Clear();
            PlayerControllerB[] players = StartOfRound.Instance.allPlayerScripts;
            for (int i = 0; i < players.Length; i++)
            {
                PlayerControllerB p = players[i];
                if (p == null || !PlayerIsTargetable(p))
                {
                    continue;
                }

                _huntCandidates.Add(new HuntLogic.Candidate(
                    (int)p.playerClientId,
                    Vector3.Distance(transform.position, p.transform.position)));
            }

            if (_huntCandidates.Count == 0)
            {
                return false;
            }

            List<HuntLogic.Candidate> ordered = HuntLogic.OrderCandidates(
                _huntCandidates,
                id => IsHuntBlocked(id, now),
                HuntMaxPathChecks);

            _huntPath ??= new NavMeshPath();

            PlayerControllerB? best = null;
            Vector3 bestNavPos = Vector3.zero;
            float bestLength = float.MaxValue;

            for (int i = 0; i < ordered.Count; i++)
            {
                PlayerControllerB? p = ResolvePlayerByClientId(ordered[i].Id);
                if (p == null)
                {
                    continue;
                }

                if (!NavMesh.SamplePosition(p.transform.position, out NavMeshHit navHit, 2.7f, agent.areaMask) ||
                    !agent.CalculatePath(navHit.position, _huntPath) ||
                    _huntPath.status != NavMeshPathStatus.PathComplete)
                {
                    // Not reachable (other side of a gap, elevator, no navmesh under them): try someone else for a while.
                    BlockHuntTarget(ordered[i].Id, now + HuntPartialBlockSeconds);
                    continue;
                }

                float length = HuntPathLength(_huntPath, ordered[i].Distance);
                if (length < bestLength)
                {
                    bestLength = length;
                    best = p;
                    bestNavPos = navHit.position;
                }
            }

            if (best == null)
            {
                return false;
            }

            if (!SetSafeDestinationServer(bestNavPos, 2.7f))
            {
                return false;
            }

            _huntTargetClientId = (int)best.playerClientId;
            _huntDestinationPlayerPos = best.transform.position;

            if (currentSearch != null && currentSearch.inProgress)
            {
                StopSearch(currentSearch);
            }

            return true;
        }

        private void SetHuntDestination(PlayerControllerB player)
        {
            _huntDestinationPlayerPos = player.transform.position;
            SetSafeDestinationServer(player.transform.position, 2.7f);
        }

        private float HuntPathLength(NavMeshPath path, float straightLineFallback)
        {
            int count = path.GetCornersNonAlloc(_huntCorners);
            if (count < 2)
            {
                return straightLineFallback;
            }

            float length = 0f;
            for (int i = 1; i < count; i++)
            {
                length += Vector3.Distance(_huntCorners[i - 1], _huntCorners[i]);
            }

            // A path longer than the corner buffer is truncated: never report less than the straight line.
            return Mathf.Max(length, straightLineFallback);
        }

        private bool IsHuntBlocked(int clientId, float now)
        {
            return clientId >= 0 && _huntBlockedUntil.TryGetValue(clientId, out float until) && now < until;
        }

        private void BlockHuntTarget(int clientId, float until)
        {
            if (clientId >= 0)
            {
                _huntBlockedUntil[clientId] = until;
            }
        }
    }
}
