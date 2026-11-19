using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using GameNetcodeStuff;
using MahoragaLC.Config;
using UnityEngine;
using UnityEngine.Networking;

namespace MahoragaLC.Audio
{
    /// <summary>
    /// Local-client singleton managing Mahoraga's proximity/encounter boss theme.
    /// Guarantees zero overlapping tracks across multiple instances, spectate-aware
    /// isInsideFactory isolation, AudioLowPassFilter LOS wall occlusion, gong ducking,
    /// optional CustomMusic/ override, and crisp 0.35s death fade-out.
    /// </summary>
    public sealed class MahoragaMusicCoordinator : MonoBehaviour
    {
        private const float OcclusionPollInterval = 0.15f;
        private const float GongDuckDuration = 1.0f;
        private const float GongDuckMultiplier = 0.55f;
        private const float PatrolStateMultiplier = 0.65f;
        private const float ChaseStateMultiplier = 1.00f;
        private const float OccludedVolumeMultiplier = 0.70f;
        private const float ClearMinCutoffHz = 900f;
        private const float ClearMaxCutoffHz = 22000f;
        private const float OccludedPatrolCutoffHz = 1100f;
        private const float OccludedChaseCutoffHz = 2600f;
        private const float DeathFastFadeRate = 3.0f;   // 1.0 -> 0.0 in ~0.33s
        private const float NormalFadeOutRate = 2.2f;   // ~0.45s crossfade on leaving range/domain
        private const float NormalFadeInRate = 1.8f;

        private static MahoragaMusicCoordinator? _instance;
        private static readonly List<MahoragaAudioController> ActiveInstances = new List<MahoragaAudioController>(4);

        private AudioSource _musicSource = null!;
        private AudioLowPassFilter _lowPassFilter = null!;
        private AudioClip? _bundledMusicClip;
        private AudioClip? _customMusicClip;
        private bool _customMusicAttempted;

        private float _occlusionTimer;
        private bool _isOccluded;
        private float _gongDuckUntil = -999f;
        private bool _deathFastFade;

        public static void Register(MahoragaAudioController controller)
        {
            if (controller == null)
            {
                return;
            }

            if (!ActiveInstances.Contains(controller))
            {
                ActiveInstances.Add(controller);
            }

            MahoragaMusicCoordinator coord = EnsureInstance();
            if (controller.musicMainClip != null && coord._bundledMusicClip == null)
            {
                coord._bundledMusicClip = controller.musicMainClip;
            }

            coord.TryStartCustomMusicLoad();
        }

        public static void Unregister(MahoragaAudioController controller)
        {
            ActiveInstances.Remove(controller);
        }

        public static void NotifyWheelTurn()
        {
            if (_instance != null)
            {
                _instance._gongDuckUntil = Time.time + GongDuckDuration;
            }
        }

        public static void NotifyMahoragaDied()
        {
            if (_instance != null)
            {
                _instance._deathFastFade = true;
            }
        }

        private static MahoragaMusicCoordinator EnsureInstance()
        {
            if (_instance != null)
            {
                return _instance;
            }

            var go = new GameObject("MahoragaMusicCoordinator");
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<MahoragaMusicCoordinator>();
            return _instance;
        }

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }

            _instance = this;

            _musicSource = gameObject.AddComponent<AudioSource>();
            _musicSource.spatialBlend = 0f;
            _musicSource.loop = true;
            _musicSource.playOnAwake = false;
            _musicSource.priority = 32;
            _musicSource.volume = 0f;

            _lowPassFilter = gameObject.AddComponent<AudioLowPassFilter>();
            _lowPassFilter.cutoffFrequency = ClearMaxCutoffHz;
            _lowPassFilter.lowpassResonanceQ = 1.0f;

            if (Plugin.CachedAudioClips.TryGetValue("mahoraga_music_main", out AudioClip cachedClip))
            {
                _bundledMusicClip = cachedClip;
            }
        }

        private void OnDestroy()
        {
            if (_instance == this)
            {
                _instance = null;
            }
        }

        private void TryStartCustomMusicLoad()
        {
            if (_customMusicAttempted)
            {
                return;
            }

            MahoragaConfig? cfg = MahoragaConfig.Current;
            if (cfg == null || !cfg.UseCustomMusicOverride.Value)
            {
                return;
            }

            _customMusicAttempted = true;
            if (string.IsNullOrEmpty(Plugin.PluginDirectory))
            {
                return;
            }

            string customDir = Path.Combine(Plugin.PluginDirectory, "CustomMusic");
            if (!Directory.Exists(customDir))
            {
                return;
            }

            string? chosenFile = null;
            AudioType audioType = AudioType.UNKNOWN;

            foreach (string file in Directory.GetFiles(customDir))
            {
                string ext = Path.GetExtension(file).ToLowerInvariant();
                if (ext == ".ogg")
                {
                    chosenFile = file;
                    audioType = AudioType.OGGVORBIS;
                    break;
                }
                if (ext == ".wav")
                {
                    chosenFile = file;
                    audioType = AudioType.WAV;
                    break;
                }
                if (ext == ".mp3")
                {
                    chosenFile = file;
                    audioType = AudioType.MPEG;
                    break;
                }
            }

            if (chosenFile != null)
            {
                StartCoroutine(LoadCustomMusicCoroutine(chosenFile, audioType));
            }
        }

        private IEnumerator LoadCustomMusicCoroutine(string filePath, AudioType audioType)
        {
            string uri = new Uri(filePath).AbsoluteUri;
            using UnityWebRequest request = UnityWebRequestMultimedia.GetAudioClip(uri, audioType);
            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                Plugin.Logger.LogWarning($"Failed to load CustomMusic override '{Path.GetFileName(filePath)}': {request.error}. Using bundled mahoraga_music_main.");
                yield break;
            }

            AudioClip? loaded = DownloadHandlerAudioClip.GetContent(request);
            if (loaded == null || loaded.length <= 0.1f)
            {
                Plugin.Logger.LogWarning($"CustomMusic override '{Path.GetFileName(filePath)}' produced an empty AudioClip. Using bundled mahoraga_music_main.");
                yield break;
            }

            loaded.name = "mahoraga_custom_music";
            _customMusicClip = loaded;
            Plugin.Logger.LogInfo($"Loaded CustomMusic override: {Path.GetFileName(filePath)} ({loaded.length:F1}s)");

            if (_musicSource != null)
            {
                bool wasPlaying = _musicSource.isPlaying;
                float prevVol = _musicSource.volume;
                _musicSource.Stop();
                _musicSource.clip = _customMusicClip;
                if (wasPlaying && prevVol > 0.001f)
                {
                    _musicSource.volume = prevVol;
                    _musicSource.Play();
                }
            }
        }

        private AudioClip? GetActiveMusicClip()
        {
            if (_customMusicClip != null)
            {
                return _customMusicClip;
            }

            if (_bundledMusicClip != null)
            {
                return _bundledMusicClip;
            }

            if (Plugin.CachedAudioClips.TryGetValue("mahoraga_music_main", out AudioClip cached))
            {
                _bundledMusicClip = cached;
                return cached;
            }

            return null;
        }

        private void Update()
        {
            MahoragaConfig? cfg = MahoragaConfig.Current;
            bool musicEnabled = cfg == null || cfg.MusicEnabled.Value;
            float masterVolume = cfg != null ? cfg.MusicMasterVolume.Value : 0.75f;
            float outerRange = cfg != null ? cfg.MusicOuterRange.Value : 35.0f;
            float innerRange = cfg != null ? cfg.MusicInnerRange.Value : 16.0f;
            bool enableWallMuffling = cfg == null || cfg.EnableWallMuffling.Value;

            if (outerRange < innerRange + 1f)
            {
                outerRange = innerRange + 1f;
            }

            PlayerControllerB? localPlayer = GameNetworkManager.Instance != null
                ? GameNetworkManager.Instance.localPlayerController
                : null;

            StartOfRound? round = StartOfRound.Instance;
            if (!musicEnabled || localPlayer == null || round == null || round.inShipPhase)
            {
                FadeOutAndStop(_deathFastFade ? DeathFastFadeRate : NormalFadeOutRate);
                return;
            }

            // Spectate-aware listener resolution.
            PlayerControllerB effectiveListener = (localPlayer.isPlayerDead && localPlayer.spectatedPlayerScript != null)
                ? localPlayer.spectatedPlayerScript
                : localPlayer;

            Vector3 listenerPos = effectiveListener.gameplayCamera != null
                ? effectiveListener.gameplayCamera.transform.position
                : effectiveListener.transform.position + Vector3.up * 2.36f;

            bool listenerInside = effectiveListener.isInsideFactory;

            // Zero-alloc selection of the single closest alive Mahoraga in the same domain.
            MahoragaAudioController? closest = null;
            float minDistSq = outerRange * outerRange;

            for (int i = ActiveInstances.Count - 1; i >= 0; i--)
            {
                MahoragaAudioController inst = ActiveInstances[i];
                if (inst == null || inst.AI == null)
                {
                    ActiveInstances.RemoveAt(i);
                    continue;
                }

                if (!inst.IsAlive)
                {
                    continue;
                }

                // Strict inside/outside isolation.
                bool enemyInside = !inst.AI.isOutside;
                if (listenerInside != enemyInside)
                {
                    continue;
                }

                float distSq = (inst.transform.position - effectiveListener.transform.position).sqrMagnitude;
                if (distSq < minDistSq)
                {
                    minDistSq = distSq;
                    closest = inst;
                }
            }

            if (closest == null || closest.AI == null)
            {
                FadeOutAndStop(_deathFastFade ? DeathFastFadeRate : NormalFadeOutRate);
                return;
            }

            // Alive Mahoraga in range clears any pending death fast-fade flag.
            _deathFastFade = false;

            AudioClip? activeClip = GetActiveMusicClip();
            if (activeClip == null)
            {
                FadeOutAndStop(NormalFadeOutRate);
                return;
            }

            if (_musicSource.clip != activeClip)
            {
                _musicSource.clip = activeClip;
            }

            if (!_musicSource.isPlaying)
            {
                _musicSource.volume = 0f;
                _musicSource.Play();
            }

            float minDist = Mathf.Sqrt(minDistSq);
            float t = Mathf.InverseLerp(outerRange, innerRange, minDist);
            float proximity = t * t * (3f - 2f * t);

            bool isChasing = closest.IsChasingOrAttacking;
            float stateMult = isChasing ? ChaseStateMultiplier : PatrolStateMultiplier;
            if (Time.time < _gongDuckUntil)
            {
                stateMult = Mathf.Min(stateMult, GongDuckMultiplier);
            }

            // Throttled LOS wall occlusion check using verified LC room/collider mask.
            _occlusionTimer -= Time.deltaTime;
            if (!enableWallMuffling)
            {
                _isOccluded = false;
            }
            else if (_occlusionTimer <= 0f)
            {
                _occlusionTimer = OcclusionPollInterval;
                Vector3 targetPos = closest.AI.eye != null
                    ? closest.AI.eye.position
                    : closest.transform.position + Vector3.up * 3.30f;

                _isOccluded = Physics.Linecast(
                    listenerPos,
                    targetPos,
                    round.collidersAndRoomMaskAndDefault,
                    QueryTriggerInteraction.Ignore);
            }

            float occlusionVolMult = _isOccluded ? OccludedVolumeMultiplier : 1.0f;
            float targetVolume = Mathf.Clamp01(proximity * stateMult * occlusionVolMult * masterVolume);
            float fadeRate = targetVolume >= _musicSource.volume ? NormalFadeInRate : NormalFadeOutRate;
            _musicSource.volume = Mathf.MoveTowards(_musicSource.volume, targetVolume, fadeRate * Time.deltaTime);

            float targetCutoff = _isOccluded
                ? (isChasing ? OccludedChaseCutoffHz : OccludedPatrolCutoffHz)
                : Mathf.Lerp(ClearMinCutoffHz, ClearMaxCutoffHz, proximity);

            float cutoffLerp = 1f - Mathf.Exp(-7f * Time.deltaTime);
            _lowPassFilter.cutoffFrequency = Mathf.Lerp(_lowPassFilter.cutoffFrequency, targetCutoff, cutoffLerp);
        }

        private void FadeOutAndStop(float fadeRate)
        {
            if (_musicSource == null)
            {
                return;
            }

            if (_musicSource.volume > 0f)
            {
                _musicSource.volume = Mathf.MoveTowards(_musicSource.volume, 0f, fadeRate * Time.deltaTime);
            }

            if (_musicSource.volume <= 0.001f)
            {
                _musicSource.volume = 0f;
                if (_musicSource.isPlaying)
                {
                    _musicSource.Stop();
                }
                _deathFastFade = false;
            }
        }
    }
}
