# Mahoraga LC — Audio Asset Manifest & Verification Report

> **Owner:** `@audio`  
> **Directory:** `/home/fede/Escritorio/Mahoraga LC/assets/audio/`  
> **Contract Alignment:** 100% compliant with `docs/ASSET_CONTRACT.md` §2 & §6 (all 8 exact clip names, formats, sample rates, channel layouts, duration windows, and loudness/peak targets).  
> **Bundle Isolation Rule:** Raw source files (`raw_source.wav`, `raw_source.info.json`) are stored **exclusively** inside `assets/audio/source/` and MUST NOT be imported into `UnityProject/Assets/` or bundled into `mahoraga_assets`.

---

## 1. Verified Audio Deliverables (8 `.ogg` Files)

All 8 production clips were verified with `ffprobe`, `ffmpeg` EBU R128 (`ebur128=peak=true`), and Python (`numpy` / `scipy`) sample-level DSP analysis:

- **Zero silent files** (RMS between `-16.77 dBFS` and `-12.61 dBFS`)
- **Zero clipped samples** (`0` samples reaching `>= 0.9999` across all 8 files)
- **All True Peaks strictly `<= -1.0 dBTP`** (`-5.30 dBTP` to `-1.50 dBTP` post-Vorbis decode)
- **Exact `44100 Hz` Ogg Vorbis (`libvorbis`, `-q:a 6`)** across all 8 clips
- **Click-free boundaries** (`EndMax <= 0.000003` on every clip)

| Exact Clip Name | File | Duration | Contract Target | SR / Ch | Codec / Bitrate | Size | Integrated LUFS | True Peak (dBTP) | RMS (dBFS) | Sample Peak (dBFS) | Clipped Samples |
|---|---|---:|---|---|---|---:|---:|---:|---:|---:|---:|
| `mahoraga_music_main` | `mahoraga_music_main.ogg` | **130.219 s** (`02:10.22`) | Strict `0:51+` trim | 44.1 kHz / **Stereo (2 ch)** | Vorbis q6 (177.5 kb/s) | 2,889,391 B (2.76 MiB) | **-14.00 LUFS** | **-5.30 dBTP** | -16.77 dB | -5.33 dB | 0 |
| `mahoraga_wheel_turn` | `mahoraga_wheel_turn.ogg` | **2.199 s** | `~2.2s` | 44.1 kHz / **Mono (1 ch)** | Vorbis q6 (75.7 kb/s) | 20,801 B (20.3 KiB) | **-13.90 LUFS** | **-1.70 dBTP** | -13.48 dB | -1.75 dB | 0 |
| `mahoraga_footstep_1` | `mahoraga_footstep_1.ogg` | **0.480 s** | `0.35s – 0.55s` | 44.1 kHz / **Mono (1 ch)** | Vorbis q6 (123.9 kb/s) | 7,436 B (7.3 KiB) | **-16.20 LUFS** | **-1.90 dBTP** | -12.63 dB | -1.91 dB | 0 |
| `mahoraga_footstep_2` | `mahoraga_footstep_2.ogg` | **0.518 s** | `0.35s – 0.55s` | 44.1 kHz / **Mono (1 ch)** | Vorbis q6 (121.3 kb/s) | 7,853 B (7.7 KiB) | **-18.50 LUFS** | **-1.80 dBTP** | -14.75 dB | -1.77 dB | 0 |
| `mahoraga_slash` | `mahoraga_slash.ogg` | **0.780 s** | `0.55s – 0.85s` | 44.1 kHz / **Mono (1 ch)** | Vorbis q6 (126.3 kb/s) | 12,311 B (12.0 KiB) | **-11.90 LUFS** | **-1.70 dBTP** | -13.90 dB | -1.72 dB | 0 |
| `mahoraga_impact` | `mahoraga_impact.ogg` | **0.742 s** | `0.50s – 0.85s` | 44.1 kHz / **Mono (1 ch)** | Vorbis q6 (121.7 kb/s) | 11,291 B (11.0 KiB) | **-17.30 LUFS** | **-1.60 dBTP** | -15.80 dB | -1.68 dB | 0 |
| `mahoraga_roar` | `mahoraga_roar.ogg` | **2.048 s** | `1.4s – 2.2s` | 44.1 kHz / **Mono (1 ch)** | Vorbis q6 (97.6 kb/s) | 24,995 B (24.4 KiB) | **-11.90 LUFS** | **-1.50 dBTP** | -12.61 dB | -1.54 dB | 0 |
| `mahoraga_death` | `mahoraga_death.ogg` | **2.480 s** | `2.0s – 2.6s` | 44.1 kHz / **Mono (1 ch)** | Vorbis q6 (112.6 kb/s) | 34,895 B (34.1 KiB) | **-13.80 LUFS** | **-1.70 dBTP** | -12.64 dB | -1.71 dB | 0 |

**Total bundled audio payload:** `3,008,973 bytes` (~`2.87 MiB`).

---

## 2. Music Track (`mahoraga_music_main.ogg`) — Strict `0:51` Trim & Loudness Verification

### 2.1 Source Acquisition (`assets/audio/source/raw_source.wav`)
- **URL:** `https://youtu.be/vxTLtmpnKn8`
- **YouTube ID:** `vxTLtmpnKn8`
- **Title:** `「Sukuna vs Mahoraga」(Malevolent Shrine) Jujutsu Kaisen S2 EP17 OST 呪術廻戦 [Epic Cover]`
- **Uploader / Channel:** `Pharozen` (uploaded `2023-11-16`)
- **Source Stream:** YouTube format `251` (Opus 48 kHz, ~127.4 kb/s) extracted to `pcm_s16le` WAV via `yt-dlp 2026.10.26`
- **Raw `ffprobe` Metrics:** `48000 Hz`, `2 ch` (stereo), `16-bit PCM`, `8,766,555 frames`, exact duration **`182.636563 s`** (`03:02.64`), `35,066,298 bytes`
- **Raw SHA-256:** `761cb812d7620092a38a4d4875ba215ca95b936e7ccd19c5526b70fb84b374cc`

### 2.2 Processing Pipeline
1. **Strict Start Trim (`-ss 00:00:51`):** Discarded `00:00.000 – 00:50.999` (`51.000 s`), retaining strictly `00:51.000` onward (`131.636563 s` before trailing silence strip).
2. **Trailing Silence Removal:** Stripped dead noise/reverb tail below `-50 dBFS` (`areverse,silenceremove=start_periods=1:start_duration=0.05:start_threshold=-50dB,areverse`), removing `1.417563 s` of trailing silence -> **`130.219000 s`** (`02:10.22`).
3. **Click-Free Loop Boundaries:** Applied `80 ms` half-sine fade-in (`st=0:d=0.08:curve=hsin`) and `500 ms` half-sine fade-out (`st=129.719:d=0.50:curve=hsin`) across the track's natural outro decay so Unity looping (`end -> start`) passes smoothly through zero with zero click/pop.
4. **Two-Pass Linear EBU R128 Loudness Normalization (`loudnorm`):**
   - Pass 1 measured input: `I = -7.65 LUFS`, `TP = +0.25 dBTP`, `LRA = 8.20 LU`, `Threshold = -17.85 LUFS`.
   - Pass 2 applied linear gain (`linear=true`, `I=-14:TP=-1.0:LRA=11`), preserving full orchestral dynamic range (`LRA = 7.8 LU`) while eliminating source inter-sample clipping.
5. **Resample & Encode:** `44100 Hz`, Stereo (2 ch), `libvorbis -q:a 6`.

### 2.3 Mathematical Verification of `0:51` Trim & Loop Seam
- **Cross-Correlation Alignment vs `raw_source.wav`:** Matched decoded `[0.2s .. 8.2s]` of `mahoraga_music_main.ogg` against `[49.0s .. 61.0s]` of `raw_source.wav` via FFT cross-correlation:
  - **Measured start offset in `raw_source.wav`:** **`51.000000 s`** (**`0.000 ms` error**)
  - **Pearson correlation:** **`r = 0.997216`**
- **Loop Boundary Check:**
  - First 5 samples max abs: `0.000010`
  - Last 10 samples max abs: `0.000000` (`1.05e-7`)
  - Direct loop-wrap delta `|sample[0] - sample[-1]|`: **`0.000003`** (inaudible, zero click)
- **Final EBU R128 Verification:** `I = -14.00 LUFS`, `TP = -5.30 dBTP` (`<= -1.0 dBTP` target), `LRA = 7.8 LU`.

---

## 3. Synthesized Horror/Anime SFX — Sound Design & Spectral Verification

All 7 SFX were synthesized at `48 kHz` 32-bit float in Python (`numpy` + `scipy.signal`) using physical/acoustic modeling (inharmonic beating bronze temple bell partials, time-varying state-variable Doppler/formant bandpass filters, glottal pulse + subharmonic vocal fry synthesis, multi-transient fracture clusters, and Schroeder-Moorer corridor reverb), followed by per-asset `ffmpeg` mastering (parametric EQ, subtle early-reflection `aecho`, punch compression, `-2.0 dB` pre-encode limiter so post-Vorbis True Peak remains strictly `<= -1.0 dBTP`, half-sine tail fade, `44.1 kHz` mono Vorbis q6 export).

### 3.1 Per-Clip Sound Design Notes
- **`mahoraga_wheel_turn.ogg` (`2.199 s`):** Heavy metallic-bronze ratchet disengagement click (`t=0.005s`) -> toothed bronze grind -> 45° detent lock-in snap (`t=0.115s`) -> resonant Japanese Bonsho bronze temple gong/chime (`196 Hz` G3 fundamental + `98 Hz` sub-octave + 12 beating inharmonic bronze partials up to `4.0 kHz`) in shrine hall reverb. Syncs cleanly to the `2.20 s` `Adapt_WheelSpin` clip.
- **`mahoraga_footstep_1.ogg` (`0.480 s`) & `mahoraga_footstep_2.ogg` (`0.518 s`):** Ground-shaking barefoot titan thuds with sub-bass floor impact (`32–37 Hz` settle), heavy flesh-on-concrete slap (`180 Hz – 4.2 kHz`), and subtle industrial metal catwalk grit. `footstep_1` is a sharp single-strike heel slam; `footstep_2` adds a distinct `28 ms` heel-to-forefoot weight roll and shifted floor resonances (`Pearson r = 0.0001` between variations — completely independent). Sized cleanly for both `Walk` (`~0.65 s` step interval) and `Run` (`~0.40 s` step interval).
- **`mahoraga_slash.ogg` (`0.780 s`):** Massive Sword of Extermination (`Taima no Tsurugi`) Doppler SVF air-cleaving whoosh (`190 Hz -> 2740 Hz -> 180 Hz`) + razor apex air-tear (`2.8–9.8 kHz`) + shimmering cursed-steel metallic blade ring (`1.18–7.12 kHz`) with `-7%` downward Doppler bend.
- **`mahoraga_impact.ogg` (`0.742 s`):** Concussive chest-punch sub shockwave (`204 Hz -> 34 Hz`) + 11 staggered bone/visor fracture micro-transients (`550 Hz – 8.2 kHz`) + dented steel armor clang + low debris rumble. Doubles cleanly for player hits, slam impact, and locked-door forcing.
- **`mahoraga_roar.ogg` (`2.048 s`):** Inhuman shikigami aggro roar built from jittered asymmetric glottal pulses (`54 -> 86 -> 36 Hz`) + subharmonic vocal fry growl, routed through 3 giant time-varying vocal tract formants (`F1 240–435 Hz`, `F2 640–1090 Hz`, `F3 1550–2340 Hz`), layered with ring-modulated minor-second hollow cursed-spirit metallic overtones and cavernous facility reverb.
- **`mahoraga_death.ogg` (`2.480 s`):** Heavy two-stage titan collapse crash (`t=0.03s`, `t=0.28s`) + downward-sagging broken bronze Dharma wheel toll (`196 Hz -> 122 Hz`) + abyssal sub-rumble + dying hollow guttural exhale + sweeping cursed-energy ash dissipation & ember crackle. Syncs cleanly to the `2.20 s` `Death` animation + reverb tail.

### 3.2 Spectral Energy Distribution & Technical Integrity

| File | Active Span (`>-45 dBFS`) | Peak Time | Crest Factor | DC Offset | Sub `<80 Hz` | Low `80–300 Hz` | Mid `300–2k Hz` | HiMid `2k–6k Hz` | Air `>6k Hz` | SHA-256 (first 16 hex) |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---|
| `mahoraga_music_main.ogg` | `0.013s – 129.830s` | `3.909s` | 11.44 dB | `+0.00002` | 28.1% | 29.8% | 32.1% | 9.6% | 0.4% | `7605155c6617ed86` |
| `mahoraga_wheel_turn.ogg` | `0.004s – 2.078s` | `0.290s` | 11.73 dB | `+0.00008` | 3.3% | 58.2% | 34.6% | 3.6% | 0.2% | `2e3e5832f49ab7d7` |
| `mahoraga_footstep_1.ogg` | `0.003s – 0.412s` | `0.037s` | 10.72 dB | `-0.00003` | 75.7% | 16.9% | 7.0% | 0.3% | 0.0% | `3c0a91fa92669ed8` |
| `mahoraga_footstep_2.ogg` | `0.003s – 0.427s` | `0.031s` | 12.99 dB | `-0.00051` | 47.2% | 43.8% | 8.7% | 0.3% | 0.0% | `3cd6e42e59c0cccc` |
| `mahoraga_slash.ogg` | `0.003s – 0.703s` | `0.086s` | 12.18 dB | `-0.00029` | 1.2% | 25.8% | 39.4% | 32.2% | 1.3% | `ed1da68276579186` |
| `mahoraga_impact.ogg` | `0.001s – 0.639s` | `0.036s` | 14.12 dB | `+0.00001` | 48.5% | 9.2% | 24.6% | 16.1% | 1.6% | `82e773677eefa4ff` |
| `mahoraga_roar.ogg` | `0.003s – 1.855s` | `0.817s` | 11.07 dB | `+0.00082` | 10.5% | 21.2% | 60.5% | 7.7% | 0.1% | `908aa535d7505259` |
| `mahoraga_death.ogg` | `0.003s – 2.302s` | `0.244s` | 10.92 dB | `+0.00042` | 35.1% | 49.8% | 13.9% | 1.2% | 0.1% | `0d7844f8e17c1aea` |

---

## 4. Unity Import & `MahoragaAudioController` Integration Notes

For `unity-builder` importing into `UnityProject/Assets/Mahoraga/Audio/` (tagged into `mahoraga_assets`):
- **`mahoraga_music_main.ogg`**: `Force To Mono = false` (preserve Stereo), `Load Type = Streaming` (or `CompressedInMemory`), `Compression Format = Vorbis`, `Quality = 0.7` (or keep native Vorbis q6 quality). Played on dedicated 2D music `AudioSource` (`loop = true`, `spatialBlend = 0.0`) driven by `MahoragaAudioController` distance curve + `AudioLowPassFilter`.
- **All 7 SFX clips** (`mahoraga_wheel_turn`, `mahoraga_footstep_1`, `mahoraga_footstep_2`, `mahoraga_slash`, `mahoraga_impact`, `mahoraga_roar`, `mahoraga_death`): Already native **Mono (44.1 kHz)** for pinpoint 3D Unity spatialization (`spatialBlend = 1.0`). Set `Force To Mono = true`, `Load Type = DecompressOnLoad` (for footsteps/slash/impact) or `CompressedInMemory` (for wheel/roar/death), `Compression Format = Vorbis`.
- **DO NOT** copy or import `assets/audio/source/` (`raw_source.wav`, `raw_source.info.json`) into the Unity project.

---

## 5. Added clip: `mahoraga_summon` (summoning sequence)

| Exact Clip Name | File | Duration | SR / Ch | Codec | Size | Integrated LUFS | True Peak | Clipped |
|---|---|---:|---|---|---:|---:|---:|---:|
| `mahoraga_summon` | `mahoraga_summon.ogg` | **5.000 s** | 44.1 kHz / Mono | Vorbis q6 | 40,822 B | **-16.2 LUFS** | **-2.6 dBTP** | 0 |

- **Original synthesis** (numpy/scipy, no third-party audio): `assets/audio/tools/synth_summon.py <out.wav>`, encoded with
  `ffmpeg -i out.wav -ac 1 -ar 44100 -c:a libvorbis -q:a 6 mahoraga_summon.ogg`.
- **Design (timeline of the 4.8 s sequence, clip is 5.0 s so the tail rings out):** rising sub drone (38/57/76 Hz, pitch +28 %) and
  viscous black-liquid churn with bubble pops (0-2.6 s) -> wet drag while the body climbs out (0.9-2.6 s) -> deep emergence boom at
  **2.60 s** (sub sweep 58->28 Hz + low thud) -> falling airy hiss, sparse drips and dark reverb as the shadow dissolves (3.7-5.0 s).
- **Runtime alignment:** the boom is at 2.6 s inside the clip; `MahoragaAI.Summon` starts it later (longer durations) or seeks into it
  (shorter durations) so the boom always lands on the end of the emergence for any `SummonDuration`.
- **Unity import:** `ForceToMono = true`, `CompressedInMemory`, Vorbis q0.7 (same as roar/death). Bundled in `mahoraga_assets`, played by
  `MahoragaAudioController.PlaySummonRumble` on a runtime clone of the voice source (same Diagetic mixer group and 3D curve).
