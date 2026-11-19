using MahoragaLC.AI;
using UnityEngine;

namespace MahoragaLC.Audio
{
    /// <summary>
    /// Per-instance 3D SFX &amp; stride footstep controller attached to MahoragaPrefab root.
    /// Exact serialized fields match ASSET_CONTRACT.md Section 7.3.
    /// </summary>
    public class MahoragaAudioController : MonoBehaviour
    {
        private const float WalkStrideMeters = 1.75f;
        private const float RunStrideMeters = 2.15f;

        // Exact SerializedField names required by ASSET_CONTRACT.md Section 7.3
        public AudioSource? voiceSource;
        public AudioSource? bodySource;
        public AudioClip? musicMainClip;
        public AudioClip? wheelTurnClip;
        public AudioClip? footstep1Clip;
        public AudioClip? footstep2Clip;
        public AudioClip? slashClip;
        public AudioClip? impactClip;
        public AudioClip? roarClip;
        public AudioClip? deathClip;

        internal MahoragaAI? AI { get; private set; }

        public bool IsAlive => AI != null && !AI.isEnemyDead;

        public bool IsChasingOrAttacking =>
            AI != null && (AI.currentBehaviourStateIndex == 2 || AI.currentBehaviourStateIndex == 3);

        private float _strideAccum;
        private bool _useSecondFootstep;

        private void Awake()
        {
            AI = GetComponent<MahoragaAI>();
            ResolveFallbackReferences();
        }

        private void OnEnable()
        {
            if (AI == null)
            {
                AI = GetComponent<MahoragaAI>();
            }
            ResolveFallbackReferences();
            MahoragaMusicCoordinator.Register(this);
        }

        private void OnDisable()
        {
            MahoragaMusicCoordinator.Unregister(this);
        }

        private void OnDestroy()
        {
            MahoragaMusicCoordinator.Unregister(this);
        }

        private void ResolveFallbackReferences()
        {
            if (voiceSource == null)
            {
                Transform? t = transform.Find("VoiceSFXSource");
                if (t != null)
                {
                    voiceSource = t.GetComponent<AudioSource>();
                }
            }

            if (bodySource == null)
            {
                Transform? t = transform.Find("BodySFXSource");
                if (t != null)
                {
                    bodySource = t.GetComponent<AudioSource>();
                }
            }

            musicMainClip ??= GetCachedClip("mahoraga_music_main");
            wheelTurnClip ??= GetCachedClip("mahoraga_wheel_turn");
            footstep1Clip ??= GetCachedClip("mahoraga_footstep_1");
            footstep2Clip ??= GetCachedClip("mahoraga_footstep_2");
            slashClip ??= GetCachedClip("mahoraga_slash");
            impactClip ??= GetCachedClip("mahoraga_impact");
            roarClip ??= GetCachedClip("mahoraga_roar");
            deathClip ??= GetCachedClip("mahoraga_death");
        }

        private static AudioClip? GetCachedClip(string clipName)
        {
            return Plugin.CachedAudioClips.TryGetValue(clipName, out AudioClip clip) ? clip : null;
        }

        public void TickStrideFootsteps(float horizontalDistanceMoved, bool isChasing)
        {
            if (horizontalDistanceMoved <= 0.0001f)
            {
                return;
            }

            _strideAccum += horizontalDistanceMoved;
            float threshold = isChasing ? RunStrideMeters : WalkStrideMeters;
            if (_strideAccum < threshold)
            {
                return;
            }

            _strideAccum -= threshold;
            PlayFootstep(isChasing ? 1.0f : 0.78f);
        }

        public void ResetStride()
        {
            _strideAccum = 0f;
        }

        public void PlayFootstep(float volume = 0.85f)
        {
            AudioClip? clip = _useSecondFootstep ? (footstep2Clip ?? footstep1Clip) : (footstep1Clip ?? footstep2Clip);
            _useSecondFootstep = !_useSecondFootstep;
            if (bodySource == null || clip == null)
            {
                return;
            }

            bodySource.pitch = Random.Range(0.94f, 1.06f);
            bodySource.PlayOneShot(clip, volume);
        }

        public void PlayRoar(float volume = 1.0f)
        {
            if (voiceSource == null || roarClip == null)
            {
                return;
            }

            voiceSource.pitch = Random.Range(0.96f, 1.04f);
            voiceSource.PlayOneShot(roarClip, volume);
            WalkieTalkie.TransmitOneShotAudio(voiceSource, roarClip, volume);
        }

        public void PlayWheelTurn(float volume = 1.0f)
        {
            MahoragaMusicCoordinator.NotifyWheelTurn();
            if (voiceSource == null || wheelTurnClip == null)
            {
                return;
            }

            voiceSource.pitch = 1.0f;
            voiceSource.PlayOneShot(wheelTurnClip, volume);
            WalkieTalkie.TransmitOneShotAudio(voiceSource, wheelTurnClip, volume);
        }

        public void PlayDeflectChime()
        {
            if (voiceSource == null || wheelTurnClip == null)
            {
                return;
            }

            voiceSource.pitch = Random.Range(1.42f, 1.52f);
            voiceSource.PlayOneShot(wheelTurnClip, 0.45f);
        }

        public void PlaySlash(float volume = 1.0f)
        {
            if (bodySource == null || slashClip == null)
            {
                return;
            }

            bodySource.pitch = Random.Range(0.95f, 1.05f);
            bodySource.PlayOneShot(slashClip, volume);
        }

        public void PlayImpact(float volume = 1.0f)
        {
            if (bodySource == null || impactClip == null)
            {
                return;
            }

            bodySource.pitch = Random.Range(0.94f, 1.04f);
            bodySource.PlayOneShot(impactClip, volume);
            WalkieTalkie.TransmitOneShotAudio(bodySource, impactClip, volume);
        }

        public void OnMahoragaDied(bool playDeathAudio)
        {
            MahoragaMusicCoordinator.NotifyMahoragaDied();
            if (playDeathAudio && voiceSource != null && deathClip != null)
            {
                voiceSource.pitch = 1.0f;
                voiceSource.PlayOneShot(deathClip, 1.0f);
                WalkieTalkie.TransmitOneShotAudio(voiceSource, deathClip, 1.0f);
            }
        }
    }
}
