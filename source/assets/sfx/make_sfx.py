#!/usr/bin/env python3
"""Procedural sound effects for the mech duel - synthesized WAVs,
no external samples. Run: python3 make_sfx.py (writes next to itself).
22050 Hz mono 16-bit PCM.

NOTE (2026-11-22): laser, cannon, rocket and hit are the owner's
supplied MP3 recordings now (laser.mp3 / ppc.mp3 / cannon.mp3 /
rocket.mp3 / hit.mp3 in this folder) - the game no longer loads
this script's laser/cannon/rocket/hit WAVs. This script still
owns explosion, steps, thruster and tree."""
import math, random, struct, wave, os

SR = 22050
OUT = os.path.dirname(os.path.abspath(__file__))
rng = random.Random(20261030)

def save(name, samples):
    peak = max(1e-6, max(abs(x) for x in samples))
    gain = 0.88 / peak
    with wave.open(os.path.join(OUT, name), 'wb') as w:
        w.setnchannels(1); w.setsampwidth(2); w.setframerate(SR)
        w.writeframes(b''.join(struct.pack('<h', int(max(-1, min(1, x * gain)) * 32767)) for x in samples))
    print(name, f"{len(samples)/SR:.2f}s")

def env_exp(n, attack, decay_tau):
    return [ (1 - math.exp(-i / max(1, attack * SR))) * math.exp(-i / (decay_tau * SR)) for i in range(n) ]

def brown_noise(n, leak=0.985):
    out, acc = [], 0.0
    for _ in range(n):
        acc = acc * leak + (rng.uniform(-1, 1)) * (1 - leak) * 8
        out.append(acc)
    return out

def laser():
    # Fast whoosh (owner, 2026-10-31), pitched MUCH higher at his
    # word (2026-11-17): same noise-through-a-swelling-lowpass
    # recipe, but the cutoff arch now lives in the bright band
    # (floor 0.40 instead of 0.05, peak ~0.98) and the whole
    # gesture is compressed to 0.13s, so the spectral centre sits
    # several times higher. A LOCAL rng keeps the shared sequence
    # untouched, so every other WAV regenerates byte-identical.
    lr = random.Random(777)
    n = int(0.17 * SR); s = []; lp = 0.0; ph = 0.0
    for i in range(n):
        t = i / SR
        arch = math.sin(math.pi * min(1, t / 0.17))
        cut = 0.40 + 0.58 * arch ** 2
        lp += cut * (lr.uniform(-1, 1) - lp)
        # the whistle (owner, 2026-11-17: more whistling /
        # whooshing): a tonal glide 3100 -> 1500 Hz with a
        # breath wobble, riding the same arch envelope as the
        # noise so the two read as one gesture.
        f = 3100 - 1600 * (t / 0.17) + 90 * math.sin(2 * math.pi * 26 * t)
        ph += 2 * math.pi * f / SR
        s.append(lp * 2.0 * arch + 0.85 * math.sin(ph) * arch ** 1.5)
    return s

def cannon():
    n = int(0.42 * SR); bn = brown_noise(n)
    e = env_exp(n, 0.002, 0.10)
    return [ (bn[i] * 0.9 + math.sin(2 * math.pi * 62 * i / SR) * 0.8) * e[i] for i in range(n) ]

def rocket():
    n = int(0.6 * SR); s = []; lp = 0.0
    for i in range(n):
        t = i / SR
        cut = 0.03 + 0.5 * math.sin(math.pi * min(1, t / 0.6)) ** 2
        lp += cut * (rng.uniform(-1, 1) - lp)
        s.append(lp * 2.2 * math.sin(math.pi * min(1, t / 0.55)) )
    return s

def hit():
    n = int(0.30 * SR); s = [0.0] * n
    for f, a, d in [(196, 0.5, 9), (294, 0.35, 13), (523, 0.28, 17), (741, 0.18, 22)]:
        for i in range(n):
            t = i / SR
            s[i] += a * math.sin(2 * math.pi * f * t) * math.exp(-t * d)
    for i in range(int(0.03 * SR)):
        s[i] += rng.uniform(-0.5, 0.5) * (1 - i / (0.03 * SR))
    return s

def tree():
    n = int(0.55 * SR); s = [0.0] * n
    for start, amp in [(0.0, 1.0), (0.07, 0.7), (0.16, 0.5)]:
        i0 = int(start * SR)
        for i in range(int(0.03 * SR)):
            v = rng.uniform(-1, 1) * amp * (1 - i / (0.03 * SR))
            if i0 + i < n: s[i0 + i] += v
    for i in range(n):
        t = i / SR
        s[i] += 0.5 * math.sin(2 * math.pi * 105 * t) * math.exp(-t * 10) * (1 if i > int(0.02*SR) else 0)
        if i > int(0.2 * SR):
            s[i] += rng.uniform(-0.12, 0.12) * math.exp(-(t - 0.2) * 6)
    return s

def explosion():
    n = int(1.5 * SR); bn = brown_noise(n, 0.995)
    e = env_exp(n, 0.006, 0.34)
    s = []
    for i in range(n):
        t = i / SR
        f = 30 + 22 * math.exp(-t * 3)
        v = (bn[i] * 1.1 + math.sin(2 * math.pi * f * t) * 1.0) * e[i]
        s.append(math.tanh(v * 1.4))
    return s

def step(heavy):
    # STOMP (owner, 2026-11-17 - replaces the light step tap):
    # a deep ground thud (46 Hz body with a 30 Hz sub tail) under
    # a short lowpassed crunch of dirt/metal stress. 0.24s.
    # deeper and MUFFLED (owner, 2026-11-17): body drops to
    # 36 Hz, sub to 26 Hz, the crunch is lowpassed much harder
    # (0.07) and quieter, and a 6ms attack ramp removes the
    # click so the hit reads as weight through earth, not a tap.
    lr = random.Random(4242 if heavy else 4243)
    n = int(0.26 * SR); s = []; lp = 0.0
    for i in range(n):
        t = i / SR
        f = 36 + 20 * math.exp(-t * 34)
        e = math.exp(-t * 14) * min(1.0, t / 0.006)
        lp += 0.07 * (lr.uniform(-1, 1) - lp)
        crunch = lp * 1.7 * math.exp(-t * 30)
        sub = 0.6 * math.sin(2 * math.pi * 26 * t) * math.exp(-t * 8)
        s.append((math.sin(2 * math.pi * f * t) * (1.0 if heavy else 0.85) + crunch) * e + sub * e)
    return s

def thruster():
    # Looping jet roar (owner, 2026-11-17): white noise through a
    # wobbling lowpass over a brown-noise body. 1.4s, made
    # loop-clean by crossfading the tail into the head; the game
    # sets the stream's loop mode on load. LOCAL rng.
    lr = random.Random(909)
    n = int(1.4 * SR); s = []; lp = 0.0; acc = 0.0
    for i in range(n):
        t = i / SR
        cut = 0.20 + 0.07 * math.sin(2 * math.pi * 2.3 * t) + 0.03 * math.sin(2 * math.pi * 7.7 * t)  # softer band (owner, 2026-11-17: less noticeable)
        lp += cut * (lr.uniform(-1, 1) - lp)
        acc = acc * 0.97 + lr.uniform(-1, 1) * 0.03 * 6
        s.append(lp * 1.5 + acc * 0.9)
    fade = int(0.15 * SR)
    for i in range(fade):
        w = i / fade
        s[i] = s[i] * w + s[n - fade + i] * (1 - w)
    return s[:n - fade]

save('laser.wav', laser())
save('cannon.wav', cannon())
save('rocket.wav', rocket())
save('hit.wav', hit())
save('tree.wav', tree())
save('explosion.wav', explosion())
save('step_a.wav', step(True))
save('step_b.wav', step(False))
save('thruster.wav', thruster())

def wind():
    # Ambient wind bed (owner, 2026-11-24): one long seamless
    # loop - white noise through a slow-wandering lowpass over
    # a brown-noise body, with the swells on whole cycles per
    # loop so the tail-into-head crossfade cannot jump. Level
    # lives in the game's player volume, not here. LOCAL rng.
    lr = random.Random(4242)
    n = int(14 * SR); s = []; lp = 0.0; acc = 0.0
    for i in range(n):
        t = i / SR; ph = t / 14.0
        cut = 0.055 + 0.030 * math.sin(2 * math.pi * (2 * ph) + 1.1) + 0.016 * math.sin(2 * math.pi * (5 * ph) + 4.0)
        lp += max(0.012, cut) * (lr.uniform(-1, 1) - lp)
        acc = acc * 0.995 + lr.uniform(-1, 1) * 0.005 * 6
        swell = 0.72 + 0.20 * math.sin(2 * math.pi * (1 * ph) + 0.6) + 0.08 * math.sin(2 * math.pi * (3 * ph) + 2.9)
        s.append((lp * 2.2 + acc * 0.8) * swell)
    fade = int(0.6 * SR)
    for i in range(fade):
        w = i / fade
        s[i] = s[i] * w + s[n - fade + i] * (1 - w)
    return s[:n - fade]

def servo():
    # Own-rider servo/hydraulic bed (owner, 2026-11-24): a
    # short seamless loop - a low harmonic stack (48 Hz) with
    # a breathing lowpass plus a whisper of hydraulic hiss.
    # The game fades it by the rider's own activity; at rest
    # it sits at the floor, effectively silent. LOCAL rng.
    lr = random.Random(1313)
    n = int(2.8 * SR); s = []; lp = 0.0; ph1 = 0.0
    for i in range(n):
        t = i / SR; ph = t / 2.8
        ph1 += 2 * math.pi * 48 / SR
        tone = math.sin(ph1) + 0.5 * math.sin(2 * ph1) + 0.22 * math.sin(3 * ph1 + 0.4)
        lp += 0.05 * (lr.uniform(-1, 1) - lp)
        breath = 0.8 + 0.2 * math.sin(2 * math.pi * (2 * ph) + 1.9)
        s.append((tone * 0.55 + lp * 0.8) * breath)
    fade = int(0.25 * SR)
    for i in range(fade):
        w = i / fade
        s[i] = s[i] * w + s[n - fade + i] * (1 - w)
    return s[:n - fade]

def click():
    # UI tick (owner, 2026-11-24; MUFFLED at his word
    # 2026-11-25, after the whoosh detour): a short dull
    # tick - the noise snap runs through a one-pole
    # lowpass and the ping drops to ~1050 Hz with the
    # bright 2600 Hz partial gone, so it sits in the
    # background. Local rng, other sounds byte-identical.
    r = random.Random(20261124)
    n = int(0.05 * SR)
    out = []
    lp = 0.0
    for i in range(n):
        t = i / SR
        lp += 0.18 * (r.uniform(-1, 1) - lp)
        snap = lp * math.exp(-t / 0.004) * 1.5
        ping = math.sin(2 * math.pi * 1050 * t) * math.exp(-t / 0.007) * 0.34
        out.append(snap + ping)
    return out

save('wind.wav', wind())
save('servo.wav', servo())
save('click.wav', click())
