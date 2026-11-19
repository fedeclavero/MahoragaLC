using UnityEngine;

namespace MahoragaLC.Effects
{
    /// <summary>
    /// Visual layer of the summoning: a glossy opaque black "ink" pool on the floor plus black droplets that
    /// splash upward while Mahoraga climbs out. Lives on the prefab child "SummonEffect" (built by
    /// MahoragaAssetBuilder). Everything is opaque HDRP/Lit geometry on purpose: no decals (they depend on the
    /// game's HDRP asset enabling decal support) and no transparent shader variants (they may be missing from the
    /// bundle), so it renders exactly like Mahoraga's own material in the game's dark facility lighting.
    /// All members are null-safe: an older bundle without this child simply plays the sequence without the pool.
    /// </summary>
    public class MahoragaSummonEffect : MonoBehaviour
    {
        public Transform? poolTransform;
        public MeshRenderer? poolRenderer;
        public ParticleSystem? droplets;

        private const float GroundLift = 0.018f;
        private const float PoolSpinDegPerSecond = 18f;

        private bool _active;
        private bool _dropletsPlaying;
        private float _lastRate = -1f;
        private float _baseRadius = 2.7f;
        private float _lastTime;

        private void Awake()
        {
            ResolveReferences();
            SetPoolVisible(false);
        }

        private void ResolveReferences()
        {
            if (poolTransform == null)
            {
                poolTransform = transform.Find("ShadowPool");
            }

            if (poolRenderer == null && poolTransform != null)
            {
                poolRenderer = poolTransform.GetComponent<MeshRenderer>();
            }

            if (droplets == null)
            {
                Transform? d = transform.Find("Droplets");
                if (d != null)
                {
                    droplets = d.GetComponent<ParticleSystem>();
                }
            }
        }

        /// <summary>Starts the effect: snaps the pool to the floor under Mahoraga and enables the renderers.</summary>
        public void Begin(float poolRadius)
        {
            ResolveReferences();
            _baseRadius = poolRadius;
            _active = true;
            _lastRate = -1f;
            _lastTime = 0f;

            Transform root = transform.parent != null ? transform.parent : transform;
            Vector3 up = Vector3.up;
            Vector3 position = root.position;

            StartOfRound? round = StartOfRound.Instance;
            if (round != null)
            {
                if (Physics.Raycast(
                        root.position + Vector3.up * 0.8f,
                        Vector3.down,
                        out RaycastHit hit,
                        3.5f,
                        round.collidersAndRoomMaskAndDefault,
                        QueryTriggerInteraction.Ignore))
                {
                    position = hit.point;
                    if (hit.normal.y >= 0.6f)
                    {
                        up = hit.normal;
                    }
                }
            }

            Quaternion align = Quaternion.FromToRotation(Vector3.up, up) * Quaternion.Euler(0f, root.eulerAngles.y + 37f, 0f);
            transform.SetPositionAndRotation(position + up * GroundLift, align);

            if (poolTransform != null)
            {
                poolTransform.localScale = new Vector3(0.001f, 1f, 0.001f);
            }

            SetPoolVisible(false);
        }

        /// <summary>Drives the effect from SummonTimeline values (poolScale 0..1, droplets per second).</summary>
        public void Evaluate(float poolScale, float dropletRate, float time)
        {
            if (!_active)
            {
                return;
            }

            float dt = Mathf.Max(0f, time - _lastTime);
            _lastTime = time;

            if (poolTransform != null)
            {
                bool visible = poolScale > 0.002f;
                SetPoolVisible(visible);
                if (visible)
                {
                    // Slow breathing ripple so the liquid never looks like a static decal.
                    float wobble = 1f + 0.035f * Mathf.Sin(time * 3.1f) + 0.02f * Mathf.Sin(time * 7.3f + 1.2f);
                    float r = _baseRadius * poolScale * wobble;
                    poolTransform.localScale = new Vector3(r, 1f, r);
                    poolTransform.Rotate(0f, PoolSpinDegPerSecond * dt, 0f, Space.Self);
                }
            }

            UpdateDroplets(dropletRate);
        }

        private void UpdateDroplets(float rate)
        {
            if (droplets == null)
            {
                return;
            }

            if (Mathf.Abs(rate - _lastRate) > 0.01f)
            {
                _lastRate = rate;
                ParticleSystem.EmissionModule emission = droplets.emission;
                emission.rateOverTime = rate;
            }

            if (rate > 0.01f && !_dropletsPlaying)
            {
                droplets.Play();
                _dropletsPlaying = true;
            }
            else if (rate <= 0.01f && _dropletsPlaying)
            {
                droplets.Stop(withChildren: false, ParticleSystemStopBehavior.StopEmitting);
                _dropletsPlaying = false;
            }
        }

        /// <summary>Ends the effect and hides everything (safe to call repeatedly).</summary>
        public void End()
        {
            _active = false;
            SetPoolVisible(false);
            if (droplets != null)
            {
                droplets.Stop(withChildren: false, ParticleSystemStopBehavior.StopEmittingAndClear);
            }

            _dropletsPlaying = false;
        }

        private void SetPoolVisible(bool visible)
        {
            if (poolRenderer != null && poolRenderer.enabled != visible)
            {
                poolRenderer.enabled = visible;
            }
        }
    }
}
