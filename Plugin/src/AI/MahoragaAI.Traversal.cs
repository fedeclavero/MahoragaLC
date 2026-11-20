using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;

namespace MahoragaLC.AI
{
    public partial class MahoragaAI
    {
        private static readonly int JumpHash = Animator.StringToHash("Jump");
        private const float JumpDuration = 0.70f;
        private const float JumpRecovery = 1.25f;
        private Vector3 _jumpStart;
        private Vector3 _jumpEnd;
        private float _jumpHeight;
        private float _jumpTime;
        private float _nextJumpTime;
        private float _stopStridesUntil;
        private bool _jumping;
        private bool _jumpIsLink;
        private bool _hasJumpAnimation;
        private int _jumpAreaBit;
        private float _restoreJumpAreaAt;
        private float _nextPatrolGapCheck;
        private bool _landingOnShipDeck;
        private bool _leavingShipDeck;
        private bool _onShipDeck;
        private Vector3 _shipDeckReturnPoint;
        private float _leaveShipDeckAt;
        private float _mayBoardShipAt;

        private void ConfigureTraversal()
        {
            if (agent != null)
            {
                agent.autoTraverseOffMeshLink = false;
                int jumpArea = NavMesh.GetAreaFromName("Jump");
                if (jumpArea >= 0)
                {
                    _jumpAreaBit = 1 << jumpArea;
                    agent.areaMask |= _jumpAreaBit;
                }
            }

            if (creatureAnimator != null)
            {
                foreach (AnimatorControllerParameter parameter in creatureAnimator.parameters)
                {
                    if (parameter.nameHash == JumpHash && parameter.type == AnimatorControllerParameterType.Trigger)
                    {
                        _hasJumpAnimation = true;
                        break;
                    }
                }
            }
        }

        private void GuardOffMeshLinkServer()
        {
            if (_restoreJumpAreaAt > 0f && Time.time >= _restoreJumpAreaAt && agent != null && agent.enabled)
            {
                agent.areaMask |= _jumpAreaBit;
                _restoreJumpAreaAt = 0f;
            }
            if (_jumping || agent == null || !agent.enabled || !agent.isOnNavMesh || !agent.isOnOffMeshLink)
            {
                return;
            }

            OffMeshLinkData link = agent.currentOffMeshLinkData;
            Vector3 from = agent.nextPosition;
            Vector3 to = link.endPos + Vector3.up * agent.baseOffset;
            if (link.valid && link.linkType != OffMeshLinkType.LinkTypeDropDown &&
                TryBeginJumpServer(from, to, useLink: true))
            {
                return;
            }

            // Unusable links are often clipped prop links or drops. Return to the approach side.
            RebindAgentAfterJump(from);
            agent.areaMask &= ~_jumpAreaBit;
            _restoreJumpAreaAt = Time.time + 4f;
            moveTowardsDestination = false;
            if (currentBehaviourStateIndex == (int)MahoragaState.Patrolling &&
                currentSearch != null && currentSearch.inProgress)
            {
                StopSearch(currentSearch);
            }
        }

        private bool TryBeginJumpServer(Vector3 from, Vector3 to, bool useLink)
        {
            if (!IsServer || _jumping || Time.time < _nextJumpTime || StartOfRound.Instance == null ||
                stunNormalizedTimer > 0f || stunnedIndefinitely > 0 || IsSummonOrSubmergeActive ||
                currentBehaviourStateIndex == (int)MahoragaState.Attacking ||
                currentBehaviourStateIndex == (int)MahoragaState.Adapting)
            {
                return false;
            }

            Vector3 difference = to - from;
            float rise = difference.y;
            difference.y = 0f;
            float span = difference.magnitude;
            bool largeOutside = isOutside && ScaleFactor >= 1.3f;
            float maxSpan = largeOutside ? 6f : 3.5f;
            float maxRise = largeOutside ? 2.6f : 0.9f;
            if (span < 0.6f || span > maxSpan || rise < -0.8f || rise > maxRise)
            {
                return false;
            }

            int mask = StartOfRound.Instance.collidersAndRoomMaskAndDefault;
            if (!NavMesh.SamplePosition(to, out NavMeshHit landing, 0.35f, agent.areaMask & ~_jumpAreaBit) ||
                !HasLandingSupport(landing.position, difference / span, mask))
            {
                return false;
            }

            if (!useLink && !NavMesh.Raycast(from, landing.position, out _, agent.areaMask & ~_jumpAreaBit))
            {
                return false;
            }

            Vector3 landingRoot = landing.position + Vector3.up * agent.baseOffset;
            float height = Mathf.Clamp(span * 0.28f, 0.45f, largeOutside ? 1.25f : 0.85f);
            if (!HasJumpClearance(from, landingRoot, height, mask))
            {
                return false;
            }

            BeginJumpServer(from, landingRoot, height, useLink);
            return true;
        }

        private void BeginJumpServer(Vector3 from, Vector3 to, float height, bool useLink)
        {
            _jumpStart = from;
            _jumpEnd = to;
            _jumpHeight = height;
            _jumpTime = 0f;
            _jumpIsLink = useLink;
            _jumping = true;
            if (agent.enabled && agent.isOnNavMesh)
            {
                agent.isStopped = true;
                agent.updatePosition = false;
            }

            Vector3 direction = Vector3.ProjectOnPlane(to - from, Vector3.up);
            if (direction.sqrMagnitude > 0.001f)
            {
                transform.rotation = Quaternion.LookRotation(direction, Vector3.up);
            }
            if (NetworkObject != null && NetworkObject.IsSpawned)
            {
                PresentJumpClientRpc();
            }
            else
            {
                PresentJump();
            }
        }

        private bool TryBoardOpenShipServer(Vector3 heardAt)
        {
            if (!IsBoardingTarget(heardAt) || _jumping || _onShipDeck || Time.time < _mayBoardShipAt ||
                Time.time < _nextJumpTime || (heardAt - transform.position).sqrMagnitude > 64f)
            {
                return false;
            }

            StartOfRound round = StartOfRound.Instance;
            Vector3 door = round.shipDoorNode.position;
            Vector3 outward = Vector3.ProjectOnPlane(door - round.shipBounds.bounds.center, Vector3.up).normalized;
            Vector3 side = Vector3.Cross(outward, Vector3.up);
            Vector3 from = transform.position;
            int mask = round.collidersAndRoomMaskAndDefault;

            // Check the outer deck first. If the door frame is too low, try a clear landing on the ship roof.
            return TryShipLandingServer(round, door, outward, side, from, mask, rooftop: false)
                || TryShipLandingServer(round, door, outward, side, from, mask, rooftop: true);
        }

        private bool IsBoardingTarget(Vector3 target)
        {
            StartOfRound? round = StartOfRound.Instance;
            return isOutside && ScaleFactor >= 1.3f && round != null && round.shipBounds != null &&
                round.shipDoorNode != null && round.shipHasLanded && !round.shipIsLeaving &&
                !round.hangarDoorsClosed && round.shipBounds.bounds.Contains(target + Vector3.up * 0.25f);
        }

        private bool TryGetShipApproachPoint(Vector3 target, out Vector3 approach)
        {
            approach = target;
            if (!IsBoardingTarget(target))
            {
                return false;
            }

            StartOfRound round = StartOfRound.Instance;
            Vector3 door = round.shipDoorNode.position;
            Vector3 outward = Vector3.ProjectOnPlane(door - round.shipBounds.bounds.center, Vector3.up).normalized;
            Vector3 edge = door + outward * 2.6f;
            if (!Physics.Raycast(edge + Vector3.up * 0.8f, Vector3.down, out RaycastHit floor, 5f,
                round.collidersAndRoomMaskAndDefault, QueryTriggerInteraction.Ignore))
            {
                return false;
            }

            approach = floor.point;
            return true;
        }

        private bool TryShipLandingServer(StartOfRound round, Vector3 door, Vector3 outward, Vector3 side,
            Vector3 from, int mask, bool rooftop)
        {
            int rows = rooftop ? 6 : 5;
            for (int offset = 0; offset < rows; offset++)
            {
                float alongDeck = rooftop ? 0.6f - offset * 0.45f : 1.6f - offset * 0.45f;
                for (int lane = -2; lane <= 2; lane++)
                {
                    Vector3 probe = door + outward * alongDeck + side * (lane * 0.75f);
                    float top = rooftop ? 8f : 0.15f;
                    if (!Physics.Raycast(probe + Vector3.up * top, Vector3.down, out RaycastHit floor,
                        rooftop ? 7f : 3.2f, mask, QueryTriggerInteraction.Ignore) ||
                        floor.normal.y < 0.75f || floor.collider == null ||
                        !floor.collider.transform.IsChildOf(round.elevatorTransform))
                    {
                        continue;
                    }

                    Vector3 landing = floor.point;
                    Vector3 heading = Vector3.ProjectOnPlane(landing - from, Vector3.up);
                    float span = heading.magnitude;
                    float rise = landing.y - from.y;
                    if (span < 0.8f || span > 5.5f || rise < -0.8f || rise > (rooftop ? 5.8f : 2.1f) ||
                        (rooftop && rise < 1.5f) || !HasLandingSupport(landing, heading / span, mask))
                    {
                        continue;
                    }

                    float lift = rooftop ? Mathf.Clamp(rise * 0.78f, 1.7f, 3.5f) : Mathf.Clamp(span * 0.25f, 0.45f, 1.15f);
                    if (!HasJumpClearance(from, landing, lift, mask) ||
                        !HasJumpClearance(landing, from, lift, mask))
                    {
                        continue;
                    }

                    _landingOnShipDeck = true;
                    _shipDeckReturnPoint = from;
                    movingTowardsTargetPlayer = false;
                    moveTowardsDestination = false;
                    BeginJumpServer(from, landing, lift, useLink: false);
                    return true;
                }
            }
            return false;
        }

        private void TickShipDeckServer()
        {
            if (!_onShipDeck || _jumping || isEnemyDead || StartOfRound.Instance == null)
            {
                return;
            }

            StartOfRound round = StartOfRound.Instance;
            if (!round.shipHasLanded || round.shipIsLeaving)
            {
                // The short boarding route ends before the ship takes off.
                _onShipDeck = false;
                RebindAgentAfterJump(_shipDeckReturnPoint);
                SyncPositionToClients();
                EnterPatrolStateServer();
                return;
            }

            if (Time.time < _leaveShipDeckAt)
            {
                return;
            }

            int mask = round.collidersAndRoomMaskAndDefault;
            if (HasJumpClearance(transform.position, _shipDeckReturnPoint, _jumpHeight, mask))
            {
                _leavingShipDeck = true;
                BeginJumpServer(transform.position, _shipDeckReturnPoint, _jumpHeight, useLink: false);
            }
        }

        private bool HasLandingSupport(Vector3 point, Vector3 direction, int mask)
        {
            Vector3 side = Vector3.Cross(direction, Vector3.up) * Mathf.Max(0.24f, agent.radius * 0.42f);
            return Physics.Raycast(point + Vector3.up * 0.4f + side, Vector3.down, out RaycastHit first, 0.75f, mask, QueryTriggerInteraction.Ignore)
                && first.normal.y >= 0.65f
                && Physics.Raycast(point + Vector3.up * 0.4f - side, Vector3.down, out RaycastHit second, 0.75f, mask, QueryTriggerInteraction.Ignore)
                && second.normal.y >= 0.65f;
        }

        private bool HasJumpClearance(Vector3 from, Vector3 to, float height, int mask)
        {
            Vector3 previous = from;
            for (int step = 1; step <= 12; step++)
            {
                Vector3 next = JumpPosition(from, to, height, step / 12f);
                if (!HasJumpSegmentClearance(previous, next, mask))
                {
                    return false;
                }
                previous = next;
            }
            return true;
        }

        private bool HasJumpSegmentClearance(Vector3 from, Vector3 to, int mask)
        {
            float radius = Mathf.Min(0.65f * ScaleFactor, agent.radius * 0.78f);
            Vector3 lower = from + Vector3.up * (radius + 0.24f);
            Vector3 upper = from + Vector3.up * Mathf.Max(radius + 0.24f, 3.20f * ScaleFactor - radius);
            Vector3 delta = to - from;
            float length = delta.magnitude;
            if (length < 0.001f)
            {
                return true;
            }

            return !Physics.CheckCapsule(lower + delta, upper + delta, radius, mask, QueryTriggerInteraction.Ignore)
                && !Physics.CapsuleCast(lower, upper, radius, delta / length, length, mask, QueryTriggerInteraction.Ignore);
        }

        private bool TryPatrolGapServer()
        {
            if (Time.time < _nextPatrolGapCheck || agent == null || !agent.enabled || !agent.isOnNavMesh ||
                agent.pathPending || agent.hasPath && agent.remainingDistance > 1.2f ||
                StartOfRound.Instance == null)
            {
                return false;
            }

            _nextPatrolGapCheck = Time.time + 3f;
            int areas = agent.areaMask & ~_jumpAreaBit;
            Vector3 from = transform.position;
            Vector3 heading = Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized;
            if (!NavMesh.Raycast(from, from + heading * 1.5f, out NavMeshHit edge, areas) ||
                edge.distance < 0.1f)
            {
                return false;
            }

            Vector3 gap = edge.position + heading * 0.8f;
            int mask = StartOfRound.Instance.collidersAndRoomMaskAndDefault;
            if (Physics.Raycast(gap + Vector3.up * 0.3f, Vector3.down, 1.15f, mask, QueryTriggerInteraction.Ignore))
            {
                return false;
            }

            Vector3 landingHint = edge.position + heading * 2.2f;
            return NavMesh.SamplePosition(landingHint, out NavMeshHit landing, 0.6f, areas)
                && Vector3.Dot(landing.position - gap, heading) > 0.75f
                && TryBeginJumpServer(from, landing.position, useLink: false);
        }

        private static Vector3 JumpPosition(Vector3 from, Vector3 to, float height, float time)
        {
            // The animation crouches and recovers at the ends. The root follows a short parabolic flight between them.
            float airborne = Mathf.InverseLerp(0.16f, 0.86f, time);
            return Vector3.Lerp(from, to, airborne) + Vector3.up * (height * 4f * airborne * (1f - airborne));
        }

        private void TickJumpServer(float dt)
        {
            if (!_jumping)
            {
                return;
            }

            _jumpTime += dt;
            if (agent == null || (!_leavingShipDeck && (!agent.enabled || !agent.isOnNavMesh)))
            {
                _jumping = false;
                if (agent != null)
                {
                    agent.updatePosition = true;
                }
                transform.position = _jumpStart;
                return;
            }

            Vector3 next = JumpPosition(_jumpStart, _jumpEnd, _jumpHeight, _jumpTime / JumpDuration);
            if (isEnemyDead || !HasJumpSegmentClearance(transform.position, next, StartOfRound.Instance.collidersAndRoomMaskAndDefault))
            {
                FinishJumpServer(landed: false);
                return;
            }

            transform.position = next;
            SyncPositionToClients();
            if (_jumpTime >= JumpDuration)
            {
                FinishJumpServer(landed: true);
            }
        }

        private void FinishJumpServer(bool landed)
        {
            _jumping = false;
            _nextJumpTime = Time.time + JumpRecovery;
            Vector3 position = landed ? _jumpEnd : _jumpStart;
            if (_landingOnShipDeck && landed)
            {
                _onShipDeck = true;
                _leaveShipDeckAt = Time.time + 2.3f;
                agent.enabled = false;
            }
            else if (_leavingShipDeck)
            {
                if (landed)
                {
                    _onShipDeck = false;
                    _mayBoardShipAt = Time.time + 8f;
                    RebindAgentAfterJump(position);
                }
                else
                {
                    transform.position = position;
                    _leaveShipDeckAt = Time.time + 0.75f;
                }
            }
            else if (landed && _jumpIsLink && agent.isOnOffMeshLink)
            {
                agent.CompleteOffMeshLink();
                agent.updatePosition = true;
                agent.isStopped = false;
                transform.position = agent.nextPosition;
            }
            else
            {
                RebindAgentAfterJump(position);
            }

            _landingOnShipDeck = false;
            _leavingShipDeck = false;
            SyncPositionToClients();
        }

        private void RebindAgentAfterJump(Vector3 position)
        {
            agent.enabled = false;
            agent.updatePosition = true;
            transform.position = position;
            agent.enabled = true;
            if (agent.isOnNavMesh)
            {
                agent.isStopped = false;
            }
        }

        [ClientRpc]
        private void PresentJumpClientRpc() => PresentJump();

        private void PresentJump()
        {
            _stopStridesUntil = Time.time + JumpDuration;
            if (_hasJumpAnimation)
            {
                creatureAnimator.SetTrigger(JumpHash);
            }
        }
    }
}
