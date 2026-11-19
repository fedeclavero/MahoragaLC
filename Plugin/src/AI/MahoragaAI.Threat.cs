using MahoragaLC.Config;
using UnityEngine;

namespace MahoragaLC.AI
{
    /// <summary>
    /// IVisibleThreat support so other enemies (Old Birds, Baboon Hawks, Giant Kiwis, ...) can detect Mahoraga.
    ///
    /// Verified against Assembly-CSharp:
    ///  - RadMechAI.CheckSightForThreat overlaps layers 3|19 (mask 524296) and calls
    ///    collider.transform.TryGetComponent&lt;IVisibleThreat&gt;: the interface must live on the SAME GameObject as the
    ///    layer-19 collider. Mahoraga's collider sits on the child "CollisionCollider", so a proxy is added there at runtime
    ///    (no prefab change needed). RPC paths (RadMechAI.SetTargetToThreatClientRpc, Giant Kiwi, Baboon) resolve the interface on the
    ///    NetworkObject root, which is MahoragaAI itself.
    ///  - GetThreatTransform must be the NetworkObject root (Baboon/RadMech read its NetworkObject).
    ///  - ThreatType.RadMech is used on purpose: nothing branches on it except Giant Kiwi (which ignores Bees, ForestGiant,
    ///    long-range EyelessDog) and MaskedPlayerEnemy already reports RadMech. ThreatType.Player is deliberately avoided:
    ///    Giant Kiwi then calls GetComponent&lt;PlayerControllerB&gt;() on the threat and would throw a NullReferenceException.
    /// </summary>
    public partial class MahoragaAI : IVisibleThreat
    {
        private const int BaseThreatLevel = 16;

        ThreatType IVisibleThreat.type => ThreatType.RadMech;

        bool IVisibleThreat.IsThreatDead() => isEnemyDead || IsSummonOrSubmergeActive;

        GrabbableObject IVisibleThreat.GetHeldObject() => null!;

        int IVisibleThreat.SendSpecialBehaviour(int id) => 0;

        int IVisibleThreat.GetThreatLevel(Vector3 seenByPosition)
        {
            // Forest Keeper reports 18, Old Bird 7..10; Mahoraga is the apex (16..20, +1 per fully adapted category).
            return Mathf.Min(20, BaseThreatLevel + _adaptation.AdaptedCategoryCount);
        }

        int IVisibleThreat.GetInterestLevel() => 0;

        Transform IVisibleThreat.GetThreatLookTransform() => eye != null ? eye : transform;

        Transform IVisibleThreat.GetThreatTransform() => transform;

        Vector3 IVisibleThreat.GetThreatVelocity()
        {
            if (IsOwner && agent != null && agent.enabled)
            {
                return agent.velocity;
            }
            return Vector3.zero;
        }

        float IVisibleThreat.GetVisibility()
        {
            if (isEnemyDead || IsSummonOrSubmergeActive)
            {
                return 0f;
            }

            MahoragaConfig? cfg = MahoragaConfig.Current;
            if (cfg != null && !cfg.VisibleToOtherEnemies.Value)
            {
                return 0f;
            }

            bool active = _smoothedHorizontalSpeed > 0.5f
                || currentBehaviourStateIndex == (int)MahoragaState.Chasing
                || currentBehaviourStateIndex == (int)MahoragaState.Attacking
                || currentBehaviourStateIndex == (int)MahoragaState.EngagingEnemy;
            return active ? 1f : 0.8f;
        }

        /// <summary>Adds the interface proxy to the layer-19 hit collider so collider-based sight checks find it.</summary>
        private void InstallThreatProxy()
        {
            EnemyAICollisionDetect[] detectors = GetComponentsInChildren<EnemyAICollisionDetect>(includeInactive: true);
            for (int i = 0; i < detectors.Length; i++)
            {
                GameObject go = detectors[i].gameObject;
                if (go == gameObject)
                {
                    continue; // the root already implements the interface itself
                }

                if (go.GetComponent<MahoragaThreatProxy>() == null)
                {
                    MahoragaThreatProxy proxy = go.AddComponent<MahoragaThreatProxy>();
                    proxy.Owner = this;
                }
            }
        }
    }

    /// <summary>
    /// Lives next to Mahoraga's hit collider and forwards every IVisibleThreat call to the AI on the root. Needed because
    /// vanilla enemies look the interface up on the collider's own GameObject (see MahoragaAI.Threat.cs).
    /// </summary>
    public sealed class MahoragaThreatProxy : MonoBehaviour, IVisibleThreat
    {
        public MahoragaAI? Owner;

        private IVisibleThreat? Target
        {
            get
            {
                if (Owner == null)
                {
                    Owner = GetComponentInParent<MahoragaAI>();
                }
                return Owner;
            }
        }

        ThreatType IVisibleThreat.type => ThreatType.RadMech;

        bool IVisibleThreat.IsThreatDead() => Target?.IsThreatDead() ?? true;

        GrabbableObject IVisibleThreat.GetHeldObject() => null!;

        int IVisibleThreat.SendSpecialBehaviour(int id) => 0;

        int IVisibleThreat.GetThreatLevel(Vector3 seenByPosition) => Target?.GetThreatLevel(seenByPosition) ?? 0;

        int IVisibleThreat.GetInterestLevel() => 0;

        Transform IVisibleThreat.GetThreatLookTransform() => Target?.GetThreatLookTransform() ?? transform;

        Transform IVisibleThreat.GetThreatTransform() => Target?.GetThreatTransform() ?? transform;

        Vector3 IVisibleThreat.GetThreatVelocity() => Target?.GetThreatVelocity() ?? Vector3.zero;

        float IVisibleThreat.GetVisibility() => Target?.GetVisibility() ?? 0f;
    }
}
