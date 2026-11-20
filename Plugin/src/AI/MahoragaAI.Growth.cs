using System.Collections.Generic;
using MahoragaLC.Config;
using Unity.Netcode;
using UnityEngine;

namespace MahoragaLC.AI
{
    /// <summary>
    /// Old Bird "growth" (lore): every time an Old Bird hurts Mahoraga it grows toward 1.5 times that Old Bird's measured height,
    /// outdoors only. Design: the NetworkObject root (NavMeshAgent, NGO, AI) is NEVER scaled; instead the visual
    /// "ModelContainer", the "CollisionCollider" (hit box), the "ScanNode" and the eye anchor are scaled/offset together.
    /// That keeps the NavMesh agent, NGO transform sync and audio sources intact. Attack reach, knockback and
    /// engagement distances multiply by <see cref="ScaleFactor"/>.
    ///
    /// Network: the server owns the earned growth and broadcasts the EFFECTIVE target scale (already 1.0 indoors) via
    /// SyncGrowthClientRpc (and in ApplyFullSyncClientRpc for late joiners). Every machine eases toward it with the same
    /// exponential approach, so everybody sees the same size.
    /// </summary>
    public partial class MahoragaAI
    {
        private const float FallbackBaseVisualHeight = 3.94f;

        private float _currentScale = 1f;
        private float _targetScale = 1f;
        private float _growthEarned = 1f;
        private float _lastBroadcastScale = 1f;
        private uint _lastGrowthBlast;
        private float _baseVisualHeight;
        private float _growthTransitionSeconds = 2f;
        private bool _scaleRefsResolved;
        private Transform? _modelContainer;
        private Transform? _scanNodeTransform;
        private Transform? _collisionTransform;
        private Vector3 _modelBaseScale = Vector3.one;
        private Vector3 _scanBaseScale = Vector3.one;
        private Vector3 _scanBasePos;
        private Vector3 _collisionBaseScale = Vector3.one;
        private Vector3 _collisionBasePos;
        private Vector3 _eyeBasePos;
        private bool _eyeBaseCaptured;
        private float _appliedScale = 1f;
        private readonly Dictionary<int, float> _oldBirdHeights = new Dictionary<int, float>();

        /// <summary>Current size multiplier (always &gt;= 1) used to scale reach / knockback.</summary>
        internal float ScaleFactor => _currentScale < 1f ? 1f : _currentScale;

        private void ResolveScaleRefs()
        {
            if (_scaleRefsResolved)
            {
                return;
            }

            _modelContainer = FindDescendantByName(transform, "ModelContainer");
            _scanNodeTransform = FindDescendantByName(transform, "ScanNode");
            EnemyAICollisionDetect? detect = GetComponentInChildren<EnemyAICollisionDetect>(includeInactive: true);
            _collisionTransform = detect != null && detect.transform != transform ? detect.transform : null;

            if (_modelContainer != null)
            {
                _modelBaseScale = _modelContainer.localScale;
            }
            if (_scanNodeTransform != null)
            {
                _scanBaseScale = _scanNodeTransform.localScale;
                _scanBasePos = _scanNodeTransform.localPosition;
            }
            if (_collisionTransform != null)
            {
                _collisionBaseScale = _collisionTransform.localScale;
                _collisionBasePos = _collisionTransform.localPosition;
            }
            _scaleRefsResolved = true;
        }

        private void ApplyScale(float s)
        {
            ResolveScaleRefs();
            if (_modelContainer != null)
            {
                _modelContainer.localScale = _modelBaseScale * s;
            }
            if (_collisionTransform != null)
            {
                _collisionTransform.localScale = _collisionBaseScale * s;
            }
            if (_scanNodeTransform != null)
            {
                _scanNodeTransform.localScale = _scanBaseScale * s;
                _scanNodeTransform.localPosition = new Vector3(_scanBasePos.x, _scanBasePos.y * s, _scanBasePos.z);
            }
            if (eye != null && eye != transform)
            {
                if (!_eyeBaseCaptured)
                {
                    // The eye anchor may be resolved after the first scale: derive its unscaled position from what was applied so far.
                    _eyeBasePos = eye.localPosition / Mathf.Max(0.01f, _appliedScale);
                    _eyeBaseCaptured = true;
                }

                eye.localPosition = _eyeBasePos * s;
            }

            if (IsServer && isOutside && agent != null)
            {
                float baseRadius = _baseAgentRadius > 0.01f ? _baseAgentRadius : 0.85f;
                agent.radius = baseRadius * Mathf.Lerp(1f, s, 0.7f);
                agent.stoppingDistance = 0.50f * s;
            }

            _appliedScale = s;
        }

        /// <summary>Runs on EVERY machine each frame: eases the visible size toward the synced target.</summary>
        private void TickScale(float dt)
        {
            if (Mathf.Approximately(_currentScale, _targetScale))
            {
                return;
            }

            _currentScale = GrowthMath.Approach(_currentScale, _targetScale, dt, _growthTransitionSeconds);
            ApplyScale(_currentScale);
        }

        // ---------------------------- server ----------------------------

        private void TickGrowthServer(MahoragaConfig? cfg)
        {
            bool enabled = cfg == null || cfg.GrowthEnabled.Value;
            float desired = enabled ? GrowthMath.EffectiveScale(_growthEarned, isOutside) : 1f;
            if (Mathf.Abs(desired - _lastBroadcastScale) > 0.001f)
            {
                _lastBroadcastScale = desired;
                float transition = cfg?.GrowthTransitionSeconds.Value ?? 2f;
                SetGrowthTarget(desired, transition);
                SyncGrowthClientRpc(desired, transition);
            }
        }

        private void OnOldBirdDamagedMe(RadMechAI oldBird, uint blast)
        {
            MahoragaConfig? cfg = MahoragaConfig.Current;
            if (!isOutside || blast == 0 || blast == _lastGrowthBlast ||
                (cfg != null && !cfg.GrowthEnabled.Value))
            {
                return;
            }

            if (_lastGrowthBlast == 0 && mainRenderer != null)
            {
                float standing = mainRenderer.bounds.size.y;
                if (standing > 0.01f && (_baseVisualHeight < 0.01f ||
                    (standing >= _baseVisualHeight * 0.85f && standing <= _baseVisualHeight * 1.15f)))
                {
                    _baseVisualHeight = standing;
                }
            }

            _lastGrowthBlast = blast;
            float heightMultiplier = cfg?.OldBirdHeightMultiplier.Value ?? GrowthMath.DefaultOldBirdHeightMultiplier;
            int steps = cfg?.GrowthStepsToMatch.Value ?? 6;
            float baseHeight = _baseVisualHeight > 0.01f ? _baseVisualHeight : FallbackBaseVisualHeight;
            float match = GrowthMath.ScaleForOldBird(MeasureOldBirdHeight(oldBird), baseHeight, heightMultiplier);
            _growthEarned = GrowthMath.NextEarnedScale(_growthEarned, match, steps);
        }

        private void MeasureBaseVisualHeight()
        {
            if (mainRenderer is not SkinnedMeshRenderer renderer || renderer.sharedMesh == null)
            {
                return;
            }

            Vector3 size = renderer.sharedMesh.bounds.size;
            Matrix4x4 modelToWorld = renderer.localToWorldMatrix;
            _baseVisualHeight =
                Mathf.Abs(modelToWorld.MultiplyVector(Vector3.right * size.x).y) +
                Mathf.Abs(modelToWorld.MultiplyVector(Vector3.up * size.y).y) +
                Mathf.Abs(modelToWorld.MultiplyVector(Vector3.forward * size.z).y);
        }

        /// <summary>Measure the grounded skinned body. Flying poses and temporary effects do not set its height.</summary>
        private float MeasureOldBirdHeight(RadMechAI oldBird)
        {
            int id = oldBird.GetInstanceID();
            if (_oldBirdHeights.TryGetValue(id, out float cached))
            {
                return cached;
            }

            if (!oldBird.inFlyingMode)
            {
                Bounds body = default;
                bool hasBody = false;
                foreach (SkinnedMeshRenderer renderer in oldBird.GetComponentsInChildren<SkinnedMeshRenderer>())
                {
                    if (renderer == null || !renderer.enabled || renderer.sharedMesh == null)
                    {
                        continue;
                    }

                    if (hasBody)
                    {
                        body.Encapsulate(renderer.bounds);
                    }
                    else
                    {
                        body = renderer.bounds;
                        hasBody = true;
                    }
                }

                if (hasBody && body.size.y >= 2f)
                {
                    _oldBirdHeights[id] = body.size.y;
                    return body.size.y;
                }
            }

            return 9.8f; // Vanilla standing height if the body cannot be measured yet.
        }

        // ---------------------------- RPC ----------------------------

        [ClientRpc]
        public void SyncGrowthClientRpc(float targetScale, float transitionSeconds)
        {
            if (!IsServer)
            {
                SetGrowthTarget(targetScale, transitionSeconds);
            }
        }

        private void SetGrowthTarget(float targetScale, float transitionSeconds)
        {
            _targetScale = GrowthMath.SanitizeScale(targetScale);
            _growthTransitionSeconds = Mathf.Clamp(transitionSeconds, 0.2f, 10f);
        }
    }
}
