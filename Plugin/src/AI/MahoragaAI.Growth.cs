using System.Collections.Generic;
using MahoragaLC.Config;
using Unity.Netcode;
using UnityEngine;

namespace MahoragaLC.AI
{
    /// <summary>
    /// Old Bird "growth" (lore): every time an Old Bird hurts Mahoraga it grows a step toward that Old Bird's measured height,
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
        private const float BaseHeightMeters = 3.75f; // ASSET_CONTRACT: feet-to-skull height at scale 1.

        private float _currentScale = 1f;
        private float _targetScale = 1f;
        private float _growthEarned = 1f;
        private float _lastBroadcastScale = 1f;
        private float _lastGrowthEventTime = -999f;
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
        private float ScaleFactor => _currentScale < 1f ? 1f : _currentScale;

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
            float max = cfg != null ? cfg.GrowthMaxScale.Value : 2f;
            float desired = enabled ? GrowthMath.EffectiveScale(_growthEarned, isOutside, max) : 1f;
            if (Mathf.Abs(desired - _lastBroadcastScale) > 0.001f)
            {
                _lastBroadcastScale = desired;
                float transition = cfg != null ? cfg.GrowthTransitionSeconds.Value : 2f;
                SyncGrowthClientRpc(desired, transition);
            }
        }

        /// <summary>An Old Bird just hurt us: advance one growth step toward its height (outdoors only).</summary>
        private void OnOldBirdDamagedMe(RadMechAI oldBird, float now)
        {
            MahoragaConfig? cfg = MahoragaConfig.Current;
            if ((cfg != null && !cfg.GrowthEnabled.Value) || !isOutside || now - _lastGrowthEventTime < 0.5f)
            {
                return;
            }

            _lastGrowthEventTime = now;
            float max = cfg != null ? cfg.GrowthMaxScale.Value : 2f;
            int steps = cfg != null ? cfg.GrowthStepsToMatch.Value : 6;
            float height = MeasureOldBirdHeight(oldBird);
            float match = GrowthMath.ScaleToMatch(height, BaseHeightMeters, max);
            _growthEarned = GrowthMath.NextEarnedScale(_growthEarned, match, steps);
        }

        /// <summary>
        /// Real height of the Old Bird from its enabled mesh renderers (SkinnedMeshRenderer / MeshRenderer bounds), measured at runtime.
        /// Ignored while it is flying (stretched pose); the largest grounded measurement is cached per instance.
        /// </summary>
        private float MeasureOldBirdHeight(RadMechAI oldBird)
        {
            int id = oldBird.GetInstanceID();
            _oldBirdHeights.TryGetValue(id, out float cached);

            bool flying = false;
            try
            {
                flying = oldBird.inFlyingMode;
            }
            catch
            {
                // field renamed in a game update: measure anyway
            }

            if (!flying)
            {
                Renderer[] renderers = oldBird.GetComponentsInChildren<Renderer>(includeInactive: false);
                bool any = false;
                Bounds bounds = default;
                for (int i = 0; i < renderers.Length; i++)
                {
                    Renderer r = renderers[i];
                    if (r == null || !r.enabled || !(r is SkinnedMeshRenderer || r is MeshRenderer))
                    {
                        continue;
                    }

                    if (!any)
                    {
                        bounds = r.bounds;
                        any = true;
                    }
                    else
                    {
                        bounds.Encapsulate(r.bounds);
                    }
                }

                if (any)
                {
                    float h = Mathf.Clamp(bounds.size.y, 2f, 25f);
                    if (h > cached)
                    {
                        cached = h;
                    }
                }
            }

            if (cached <= 0f)
            {
                cached = 7f; // no usable renderer yet: conservative typical Old Bird height
            }

            _oldBirdHeights[id] = cached;
            return cached;
        }

        // ---------------------------- RPC ----------------------------

        [ClientRpc]
        public void SyncGrowthClientRpc(float targetScale, float transitionSeconds)
        {
            _targetScale = Mathf.Clamp(targetScale, 1f, 3.5f);
            _growthTransitionSeconds = Mathf.Clamp(transitionSeconds, 0.2f, 10f);
        }
    }
}
