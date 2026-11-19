#!/usr/bin/env python3
"""
Synthesizes assets/audio/mahoraga_summon.ogg (original, no third-party material).

Sound design for the Ten Shadows style summoning sequence (see docs/MAHORAGA_GDD.md):
  0.00-2.60 s  rising sub drone + viscous black-liquid churn with bubble pops
  0.90-2.60 s  heavy wet drag while the body climbs out of the shadow
  2.60 s       deep emergence boom (sub sweep + low thud)
  3.70-5.00 s  shadow dissolving: falling airy hiss + sparse drips + reverb tail
Timings match SummonTimeline (reference duration 4.8 s); the clip is 5.0 s so the tail
rings out past the end of the sequence.

Usage: python3 synth_summon.py <out.wav>    (then encode with ffmpeg, see AUDIO_MANIFEST.md)
"""
import sys
import numpy as np
from scipy import signal
from scipy.io import wavfile

SR = 48000
DUR = 5.0
N = int(SR * DUR)
t = np.arange(N) / SR
rng = np.random.default_rng(20261119)


def smoothstep(a, b, x):
    u = np.clip((x - a) / (b - a), 0.0, 1.0)
    return u * u * (3 - 2 * u)


def bandpass(x, lo, hi, order=2):
    sos = signal.butter(order, [lo, hi], btype="band", fs=SR, output="sos")
    return signal.sosfilt(sos, x)


def lowpass(x, fc, order=2):
    sos = signal.butter(order, fc, btype="low", fs=SR, output="sos")
    return signal.sosfilt(sos, x)


def highpass(x, fc, order=2):
    sos = signal.butter(order, fc, btype="high", fs=SR, output="sos")
    return signal.sosfilt(sos, x)


def brown_noise(n):
    w = rng.standard_normal(n)
    b = np.cumsum(w)
    b -= signal.sosfilt(signal.butter(1, 20, btype="high", fs=SR, output="sos"), b)
    b = b - np.mean(b)
    return b / (np.max(np.abs(b)) + 1e-9)


# ---------------------------------------------------------------- 1. sub drone
swell = smoothstep(0.15, 2.6, t) * (1.0 - smoothstep(3.7, 4.9, t))
pitch = 1.0 + 0.28 * smoothstep(0.0, 2.6, t)
phase1 = 2 * np.pi * np.cumsum(38.0 * pitch) / SR
phase2 = 2 * np.pi * np.cumsum(57.0 * pitch) / SR
phase3 = 2 * np.pi * np.cumsum(76.5 * pitch) / SR
drone = 0.55 * np.sin(phase1) + 0.28 * np.sin(phase2 + 0.7) + 0.14 * np.sin(phase3 + 1.9)
drone *= 1.0 + 0.30 * np.sin(2 * np.pi * 4.6 * t) * smoothstep(0.5, 2.0, t)
drone *= swell

# ---------------------------------------------------------------- 2. liquid churn
churn_src = brown_noise(N)
churn = bandpass(churn_src, 90, 900, 2) * (0.6 + 0.4 * np.sin(2 * np.pi * 1.7 * t + 0.8))
churn *= smoothstep(0.0, 2.2, t) * (1.0 - smoothstep(3.4, 4.6, t))

# bubble pops: exponentially decaying rising chirps, density grows with time
bubbles = np.zeros(N)
n_b = 70
times = np.sort(rng.beta(1.8, 1.4, n_b) * 3.9)
for tb in times:
    f0 = rng.uniform(180, 520)
    f1 = f0 * rng.uniform(1.6, 2.6)
    d = rng.uniform(0.05, 0.14)
    i0 = int(tb * SR)
    L = int(d * SR * 3)
    if i0 + L >= N:
        continue
    tt = np.arange(L) / SR
    f = f0 + (f1 - f0) * (tt / (d * 3))
    ph = 2 * np.pi * np.cumsum(f) / SR
    amp = rng.uniform(0.25, 1.0) * (0.35 + 0.65 * smoothstep(0.0, 2.4, tb))
    bubbles[i0:i0 + L] += amp * np.sin(ph) * np.exp(-tt / d)
bubbles = lowpass(bubbles, 1800, 2)

# ---------------------------------------------------------------- 3. wet drag (body climbing out)
drag_src = brown_noise(N)
drag = lowpass(drag_src, 420, 2)
am = 0.55 + 0.45 * np.abs(np.sin(2 * np.pi * (6.5 + 4 * smoothstep(0.9, 2.6, t)) * t + 1.3 * np.sin(2 * np.pi * 0.9 * t)))
drag = drag * am * smoothstep(0.85, 1.4, t) * (1.0 - smoothstep(2.5, 2.9, t))

# ---------------------------------------------------------------- 4. emergence boom at 2.6 s
tb0 = 2.6
tt = np.clip(t - tb0, 0, None)
on = (t >= tb0).astype(float)
boom_f = 28.0 + 30.0 * np.exp(-tt / 0.22)
boom = np.sin(2 * np.pi * np.cumsum(boom_f) / SR) * np.exp(-tt / 0.75) * on
thud = lowpass(rng.standard_normal(N), 260, 2) * np.exp(-tt / 0.16) * on
crack = bandpass(rng.standard_normal(N), 700, 3200, 2) * np.exp(-tt / 0.05) * on
boom_total = 1.0 * boom + 0.9 * thud + 0.18 * crack

# ---------------------------------------------------------------- 5. dissolve tail
hiss_src = rng.standard_normal(N)
hiss = np.zeros(N)
seg = 1024
for i in range(0, N, seg):
    tc = i / SR
    if tc < 3.5:
        continue
    fc = 2400 - 1900 * smoothstep(3.5, 5.0, tc)
    chunk = hiss_src[i:i + seg]
    if len(chunk) > 32:
        hiss[i:i + seg] = bandpass(chunk, max(120, fc * 0.6), min(SR / 2 - 100, fc * 1.4), 1)[:len(chunk)]
hiss *= smoothstep(3.55, 3.9, t) * (1.0 - smoothstep(4.0, 5.0, t)) * 0.8

drips = np.zeros(N)
for tb in [3.95, 4.18, 4.31, 4.52, 4.66, 4.83]:
    f0 = rng.uniform(900, 1700)
    L = int(0.18 * SR)
    i0 = int(tb * SR)
    if i0 + L >= N:
        continue
    tt2 = np.arange(L) / SR
    drips[i0:i0 + L] += 0.55 * np.sin(2 * np.pi * (f0 + 900 * tt2) * tt2) * np.exp(-tt2 / 0.035)

# ---------------------------------------------------------------- mix (dry)
dry = (1.00 * drone
       + 0.55 * churn
       + 0.50 * bubbles
       + 0.75 * drag
       + 1.25 * boom_total
       + 0.38 * hiss
       + 0.30 * drips)

# ---------------------------------------------------------------- dark reverb (synthetic exponential-noise IR)
ir_len = int(1.6 * SR)
tir = np.arange(ir_len) / SR
ir = rng.standard_normal(ir_len) * np.exp(-tir / 0.42)
ir = lowpass(ir, 1500, 2)
ir /= np.sqrt(np.sum(ir ** 2))
wet = signal.fftconvolve(dry, ir)[:N]
mix = dry + 0.45 * wet * (0.4 + 0.6 * smoothstep(1.0, 3.0, t))

# ---------------------------------------------------------------- master
mix = highpass(mix, 22, 2)                      # no DC / infrasonic waste
mix = np.tanh(1.35 * mix / (np.max(np.abs(mix)) + 1e-9)) / np.tanh(1.35)
fade_in = np.clip(t / 0.01, 0, 1)
fade_out = np.clip((DUR - t) / 0.35, 0, 1)
fade_out = np.sin(fade_out * np.pi / 2)
mix *= fade_in * fade_out
peak = np.max(np.abs(mix))
mix *= (10 ** (-2.5 / 20)) / peak               # -2.5 dBFS sample peak before Vorbis

wavfile.write(sys.argv[1], SR, (mix * 32767).astype(np.int16))
print(f"wrote {sys.argv[1]}: {N / SR:.3f}s peak={20 * np.log10(np.max(np.abs(mix))):.2f} dBFS")
