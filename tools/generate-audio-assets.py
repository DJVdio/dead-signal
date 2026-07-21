#!/usr/bin/env python3
"""Deterministically render Dead Signal's original audio library.

The game consumes the rendered files under godot/assets/audio.  This script is
the reproducible source for those project-owned masters; it is not run by the
game and has no gameplay dependencies.
"""

from __future__ import annotations

import math
import wave
from pathlib import Path

import numpy as np


ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / "godot" / "assets" / "audio"
RATE = 44_100
TAU = math.tau

MUSIC = {
    "menu": (55.0, (0, 3, 7), 54, 0.18),
    "camp-day": (65.406, (0, 4, 7), 62, 0.10),
    "camp-night": (49.0, (0, 3, 7), 48, 0.15),
    "exploration": (46.249, (0, 3, 6), 58, 0.20),
    "combat": (43.654, (0, 2, 7), 92, 0.32),
    "horde": (36.708, (0, 1, 6), 76, 0.42),
    "ending": (41.203, (0, 3, 7), 44, 0.12),
}

AMBIENCE = ("camp-day", "camp-night", "outdoor", "interior", "sewer")

SFX = (
    "footstep-human", "footstep-dog", "footstep-zombie",
    "pistol-shot", "rifle-shot", "shotgun-shot",
    "bow-release", "crossbow-release", "melee-light", "melee-heavy",
    "armor-impact", "flesh-sharp-impact", "flesh-blunt-impact",
    "fatal-impact", "structure-impact", "door-open", "door-close",
    "lockpick", "work", "loot", "death", "zombie-groan",
)


def time_axis(seconds: float) -> np.ndarray:
    return np.arange(round(seconds * RATE), dtype=np.float64) / RATE


def normalize(samples: np.ndarray, peak: float = 0.88) -> np.ndarray:
    maximum = float(np.max(np.abs(samples)))
    if maximum > 1e-9:
        samples = samples * (peak / maximum)
    return np.clip(samples, -1.0, 1.0)


def stereo(mono: np.ndarray, width: float = 0.0, seed: int = 0) -> np.ndarray:
    if width <= 0:
        return np.column_stack((mono, mono))
    rng = np.random.default_rng(seed)
    delay = int(rng.integers(17, 91))
    shifted = np.roll(mono, delay)
    shifted[:delay] = mono[:delay]
    return np.column_stack((mono * (1 - width) + shifted * width,
                            shifted * (1 - width) + mono * width))


def write_wav(path: Path, samples: np.ndarray) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    if samples.ndim == 1:
        samples = stereo(samples)
    pcm = (normalize(samples) * 32767).astype("<i2")
    with wave.open(str(path), "wb") as out:
        out.setnchannels(2)
        out.setsampwidth(2)
        out.setframerate(RATE)
        out.writeframes(pcm.tobytes())


def periodic_pulse(t: np.ndarray, bpm: int, sharpness: float = 8.0) -> np.ndarray:
    phase = (t * bpm / 60.0) % 1.0
    return np.exp(-phase * sharpness)


def render_music(name: str, root: float, chord: tuple[int, ...], bpm: int,
                 grit: float) -> np.ndarray:
    seconds = 24.0
    t = time_axis(seconds)
    rng = np.random.default_rng(0xD34D_5100 + list(MUSIC).index(name))
    loop = TAU * t / seconds
    mix = np.zeros_like(t)

    # Low detuned pad: all modulation is loop-periodic, so the asset loops cleanly.
    for index, semitone in enumerate(chord):
        hz = root * 2 ** (semitone / 12)
        phase = TAU * hz * t + index * 1.61 + np.sin(loop * (index + 1)) * 0.12
        voice = np.sin(phase) + 0.28 * np.sin(phase * 2 + 0.4)
        pan_l = 0.45 + index * 0.08
        mix += voice * (0.11 + pan_l * 0.015)

    # Sparse four-note motif gives each state a composed identity without becoming busy.
    motifs = {
        "menu": (0, 3, 7, 3), "camp-day": (0, 7, 4, 7),
        "camp-night": (0, 3, -2, 3), "exploration": (0, 6, 3, -2),
        "combat": (0, 2, 0, 7), "horde": (0, 1, -5, 1),
        "ending": (7, 3, 0, -2),
    }
    step_seconds = seconds / 8
    for step in range(8):
        start = step * step_seconds
        age = t - start
        mask = (age >= 0) & (age < step_seconds)
        note = motifs[name][step % 4]
        hz = root * 4 * 2 ** (note / 12)
        env = np.where(mask, (1 - np.exp(-np.maximum(age, 0) * 7))
                       * np.exp(-np.maximum(age, 0) * 1.25), 0)
        mix += np.sin(TAU * hz * np.maximum(age, 0) + 0.2) * env * 0.055

    beat = periodic_pulse(t, bpm, 9.0)
    bass = np.sin(TAU * root * 0.5 * t) * beat
    mix += bass * (0.045 if name not in ("combat", "horde") else 0.17)

    if name in ("combat", "horde"):
        kick = np.sin(TAU * (72 - 40 * np.minimum((t * bpm / 60) % 1, 0.3)) * t)
        mix += kick * beat * (0.15 if name == "combat" else 0.20)
        half = periodic_pulse(t + 30 / bpm, bpm, 17)
        noise = rng.normal(0, 1, len(t))
        mix += noise * half * (0.035 if name == "combat" else 0.052)

    air = rng.normal(0, 1, len(t))
    air = np.convolve(air, np.ones(96) / 96, mode="same")
    mix += air * grit * 0.055
    # Cross-match both edges so a full-file loop has no silent dip or click.
    edge = int(RATE * 0.22)
    blend = np.linspace(0, 1, edge)
    shared = mix[:edge] * (1 - blend) + mix[-edge:] * blend
    mix[:edge] = shared
    mix[-edge:] = shared
    return normalize(stereo(mix, 0.18, seed=bpm), 0.82)


def colored_noise(length: int, rng: np.random.Generator, window: int) -> np.ndarray:
    raw = rng.normal(0, 1, length + window)
    cumulative = np.cumsum(np.insert(raw, 0, 0.0))
    smoothed = (cumulative[window:] - cumulative[:-window]) / window
    return smoothed[:length]


def render_ambience(name: str) -> np.ndarray:
    seconds = 16.0
    t = time_axis(seconds)
    rng = np.random.default_rng(0xA6B1_E000 + AMBIENCE.index(name))
    bed = colored_noise(len(t), rng, 180 if name in ("outdoor", "camp-day") else 420)
    bed += colored_noise(len(t), rng, 1800) * 1.8
    mix = bed * 0.13

    if name == "camp-day":
        for at, hz in ((2.1, 2350), (6.7, 2780), (11.4, 2180), (14.1, 2520)):
            age = t - at
            mix += np.sin(TAU * hz * age) * np.exp(-np.maximum(age, 0) * 7) \
                   * ((age >= 0) & (age < 0.45)) * 0.08
    elif name == "camp-night":
        chirp = np.maximum(0, np.sin(TAU * 0.73 * t)) ** 24
        mix += np.sin(TAU * 3180 * t) * chirp * 0.07
    elif name == "outdoor":
        gust = 0.45 + 0.35 * np.sin(TAU * t / seconds) + 0.2 * np.sin(TAU * t * 3 / seconds)
        mix *= gust
    elif name == "interior":
        mix *= 0.5
        mix += np.sin(TAU * 58 * t) * 0.025
        for at in (3.6, 9.8):
            age = t - at
            mix += np.sin(TAU * 710 * age) * np.exp(-np.maximum(age, 0) * 22) \
                   * ((age >= 0) & (age < 0.22)) * 0.05
    elif name == "sewer":
        mix *= 0.62
        mix += np.sin(TAU * 43 * t) * 0.032
        for at in (0.8, 2.35, 4.9, 7.15, 10.4, 13.25, 15.1):
            for delay, gain in ((0, 0.25), (0.105, 0.09), (0.19, 0.035)):
                age = t - at - delay
                active = (age >= 0) & (age < 0.26)
                drop = np.sin(TAU * (980 - 1500 * np.maximum(age, 0)) * age)
                mix += drop * np.exp(-np.maximum(age, 0) * 20) * active * gain

    # Equal ends prevent clicks; Godot's imported OGG loops continuously.
    edge = int(RATE * 0.18)
    blend = np.linspace(0, 1, edge)
    shared = mix[:edge] * (1 - blend) + mix[-edge:] * blend
    mix[:edge] = shared
    mix[-edge:] = shared
    return normalize(stereo(mix, 0.28, seed=AMBIENCE.index(name) + 80), 0.72)


def exp_env(t: np.ndarray, decay: float, attack: float = 0.004) -> np.ndarray:
    return np.minimum(1, t / attack) * np.exp(-t * decay)


def sweep(t: np.ndarray, start: float, end: float) -> np.ndarray:
    duration = max(float(t[-1]), 1 / RATE)
    rate = (end - start) / duration
    phase = TAU * (start * t + 0.5 * rate * t * t)
    return np.sin(phase)


def render_sfx(name: str, variant: int) -> np.ndarray:
    seed = 0x5F00_0000 + SFX.index(name) * 31 + variant
    rng = np.random.default_rng(seed)
    durations = {
        "footstep-human": .20, "footstep-dog": .16, "footstep-zombie": .27,
        "pistol-shot": .48, "rifle-shot": .62, "shotgun-shot": .82,
        "bow-release": .34, "crossbow-release": .38,
        "melee-light": .32, "melee-heavy": .48, "armor-impact": .48,
        "flesh-sharp-impact": .32, "flesh-blunt-impact": .42,
        "fatal-impact": .72, "structure-impact": .55,
        "door-open": .72, "door-close": .48, "lockpick": .28,
        "work": .42, "loot": .36, "death": .90, "zombie-groan": 1.35,
    }
    t = time_axis(durations[name])
    noise = rng.normal(0, 1, len(t))
    variance = 1 + (variant - 2) * 0.045
    mix = np.zeros_like(t)

    if name.startswith("footstep"):
        base = {"footstep-human": 105, "footstep-dog": 155, "footstep-zombie": 76}[name]
        env = exp_env(t, 19 if name != "footstep-zombie" else 11)
        mix = (sweep(t, base * variance, base * .45) * .42 + noise * .58) * env
        if name == "footstep-zombie":
            mix += noise * exp_env(np.maximum(t - .09, 0), 17) * (t > .09) * .22
    elif name.endswith("shot"):
        decay = {"pistol-shot": 12, "rifle-shot": 8, "shotgun-shot": 5.2}[name]
        low = {"pistol-shot": 115, "rifle-shot": 82, "shotgun-shot": 57}[name]
        crack = np.sign(np.sin(TAU * (900 + variant * 71) * t))
        mix = (noise * .72 + sweep(t, low * variance, 31) * .45 + crack * .22) * exp_env(t, decay, .0008)
        echo_age = np.maximum(t - .105, 0)
        mix += noise * np.exp(-echo_age * 13) * (t > .105) * .09
    elif name in ("bow-release", "crossbow-release"):
        start = 530 if name == "bow-release" else 360
        mix = sweep(t, start * variance, 105) * exp_env(t, 15, .002) * .62
        mix += noise * exp_env(t, 23, .001) * (.16 if name == "bow-release" else .28)
    elif name.startswith("melee"):
        heavy = name == "melee-heavy"
        whoosh = colored_noise(len(t), rng, 18 if heavy else 10)
        arc = np.sin(np.pi * np.minimum(1, t / (t[-1] * .72))) ** 2
        mix = whoosh * arc * (1.0 if heavy else .72)
        mix += sweep(t, 160 if heavy else 310, 62) * exp_env(t, 7 if heavy else 11) * .22
    elif name == "armor-impact":
        mix = noise * exp_env(t, 18, .0005) * .35
        for hz, gain in ((680, .55), (1030, .31), (1470, .18)):
            mix += np.sin(TAU * hz * variance * t) * np.exp(-t * (7 + hz / 400)) * gain
    elif "flesh" in name or name in ("fatal-impact", "death"):
        sharp = name == "flesh-sharp-impact"
        decay = 15 if sharp else (5 if name in ("fatal-impact", "death") else 9)
        mix = noise * exp_env(t, decay, .001) * (.72 if sharp else .52)
        mix += sweep(t, 145 if sharp else 92, 34) * exp_env(t, decay * .65) * .48
    elif name == "structure-impact":
        mix = (noise * .68 + sweep(t, 118, 36) * .52) * exp_env(t, 7, .001)
    elif name.startswith("door"):
        if name == "door-open":
            creak = sweep(t, 185 * variance, 72)
            mix = creak * (np.sin(np.pi * t / t[-1]) ** 1.5) * .55 + noise * exp_env(t, 6) * .12
        else:
            mix = (noise * .62 + sweep(t, 104, 39) * .65) * exp_env(t, 12, .001)
    elif name == "lockpick":
        for at in (.02, .085, .16):
            age = np.maximum(t - at, 0)
            mix += np.sin(TAU * (1120 + variant * 80) * age) * np.exp(-age * 42) * (t >= at) * .5
    elif name == "work":
        for at in (.02, .14, .27):
            age = np.maximum(t - at, 0)
            mix += (noise * .55 + np.sin(TAU * 135 * age) * .45) * np.exp(-age * 25) * (t >= at)
    elif name == "loot":
        mix = sweep(t, 540 * variance, 1080) * exp_env(t, 7, .006) * .5
        mix += np.sin(TAU * 1320 * t) * exp_env(t, 14, .004) * .23
    elif name == "zombie-groan":
        vibrato = 1 + .035 * np.sin(TAU * (5.1 + variant * .3) * t)
        phase = np.cumsum(TAU * 71 * variance * vibrato / RATE)
        voice = np.sin(phase) + .45 * np.sin(phase * 2.03) + .20 * np.sin(phase * 3.07)
        body = np.sin(np.pi * np.minimum(1, t / t[-1])) ** .7
        mix = voice * body * .48 + colored_noise(len(t), rng, 28) * body * .4

    return normalize(stereo(mix, 0.08, seed=seed), 0.86)


def main() -> None:
    for name, values in MUSIC.items():
        print(f"music/{name}.wav")
        write_wav(OUT / "music" / f"{name}.wav", render_music(name, *values))
    for name in AMBIENCE:
        print(f"ambience/{name}.wav")
        write_wav(OUT / "ambience" / f"{name}.wav", render_ambience(name))
    for name in SFX:
        for variant in range(1, 4):
            destination = OUT / "sfx" / name / f"{variant:02}.wav"
            print(destination.relative_to(OUT))
            write_wav(destination, render_sfx(name, variant))


if __name__ == "__main__":
    main()
