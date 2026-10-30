#!/usr/bin/env python3
"""Procedural sound effects for the mech duel — synthesized WAVs,
no external samples. Run: python3 make_sfx.py (writes next to itself).
22050 Hz mono 16-bit PCM."""
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
    n = int(0.22 * SR); s = []
    for i in range(n):
        t = i / SR
        f = 240 + 1500 * math.exp(-t * 14)
        ph = 2 * math.pi * (240 * t + 1500 * (1 - math.exp(-t * 14)) / 14)
        e = math.exp(-t * 11)
        s.append((math.sin(ph) + 0.35 * math.sin(2 * ph)) * e)
    s[0] += 0.8; s[1] += 0.5
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
    n = int(0.15 * SR); s = []
    for i in range(n):
        t = i / SR
        f = 58 + 30 * math.exp(-t * 40)
        e = math.exp(-t * 26)
        s.append((math.sin(2 * math.pi * f * t) * (1.0 if heavy else 0.8) + rng.uniform(-0.25, 0.25) * math.exp(-t * 60)) * e)
    return s

save('laser.wav', laser())
save('cannon.wav', cannon())
save('rocket.wav', rocket())
save('hit.wav', hit())
save('tree.wav', tree())
save('explosion.wav', explosion())
save('step_a.wav', step(True))
save('step_b.wav', step(False))
