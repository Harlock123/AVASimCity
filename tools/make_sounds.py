#!/usr/bin/env python3
"""Synthesises AVASimCity's sound effects and title tune into ../src/AVASimCity/Sounds as 16-bit mono WAV.

Nothing is sampled from any game: every effect is built here from square, triangle, sine, saw and noise voices.
The title plays the Minuet in G major (BWV Anh. 114) from the Notebook for Anna Magdalena Bach (1725), now
attributed to Christian Petzold and long in the public domain, on a plucked harpsichord voice over a bass.
Run it from anywhere:
    python3 tools/make_sounds.py
"""
import math
import os
import random
import struct
import wave


RATE = 22050
OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "src", "AVASimCity", "Sounds")
RNG = random.Random(1989)


def write(name, samples, volume=0.7):
    peak = max(1e-9, max(abs(s) for s in samples))
    with wave.open(os.path.join(OUT, name), "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(RATE)
        w.writeframes(b"".join(struct.pack("<h", int(s / peak * volume * 32767)) for s in samples))


def voice(kind, phase):
    p = phase % 1.0
    if kind == "square":
        return 1.0 if p < 0.5 else -1.0
    if kind == "pulse":
        return 1.0 if p < 0.25 else -1.0
    if kind == "tri":
        return 4 * p - 1 if p < 0.5 else 3 - 4 * p
    if kind == "saw":
        return 2 * p - 1
    if kind == "sine":
        return math.sin(2 * math.pi * p)
    return RNG.uniform(-1, 1)


def tone(seconds, f0, f1=None, kind="square", attack=0.004, release=1.0, vibrato=0.0, vib_rate=8.0, gain=1.0):
    """A note gliding from f0 to f1 (exponentially), with a short attack and a shaped decay."""
    n = int(RATE * seconds)
    f1 = f1 or f0
    out, phase, held = [], 0.0, 0.0
    for i in range(n):
        t = i / RATE
        f = f0 * (f1 / f0) ** (i / max(1, n - 1))
        if vibrato:
            f *= 1 + vibrato * math.sin(2 * math.pi * vib_rate * t)
        phase += f / RATE
        if kind == "noise":
            # Noise re-sampled at the voice's rate: a lower 'pitch' sounds rougher.
            if int(phase) != int(phase - f / RATE):
                held = RNG.uniform(-1, 1)
            s = held
        else:
            s = voice(kind, phase)
        env = min(1.0, t / attack) * (1 - i / n) ** release
        out.append(s * env * gain)
    return out


def swell(samples, rise=0.4):
    """Fades a sound in over the first part of it."""
    n = len(samples)
    return [s * min(1.0, i / (n * rise)) for i, s in enumerate(samples)]


def silence(seconds):
    return [0.0] * int(RATE * seconds)


def cat(*parts):
    out = []
    for p in parts:
        out.extend(p)
    return out


def mix(*parts):
    n = max(len(p) for p in parts)
    out = [0.0] * n
    for p in parts:
        for i, v in enumerate(p):
            out[i] += v
    return out


NOTES = {"C": 0, "D": 2, "E": 4, "F": 5, "G": 7, "A": 9, "B": 11}


def hz(note):
    n = NOTES[note[0]]
    rest = note[1:]
    while rest and rest[0] in "#b":
        n += 1 if rest[0] == "#" else -1
        rest = rest[1:]
    return 440.0 * 2 ** ((12 * (int(rest) + 1) + n - 69) / 12)


def bell(seconds, f, gain=0.6):
    """A bell: a sine with its bright partials, all dying away, the high ones first."""
    return mix(tone(seconds, f, kind="sine", attack=0.002, release=2.0, gain=gain),
               tone(seconds * 0.6, f * 2.76, kind="sine", attack=0.002, release=3.0, gain=gain * 0.35),
               tone(seconds * 0.4, f * 5.4, kind="sine", attack=0.002, release=3.0, gain=gain * 0.15))


def pluck(seconds, note, gain=0.6):
    """A harpsichord string: bright at first, dying away."""
    f = hz(note)
    return mix(tone(seconds, f, kind="tri", attack=0.002, release=1.2, gain=gain),
               tone(min(seconds, 0.12), f * 2, kind="pulse", attack=0.001, release=1.0, gain=gain * 0.25),
               tone(min(seconds, 0.05), f * 3, kind="square", attack=0.001, release=1.0, gain=gain * 0.08))


def play(bpm, notes, gain=0.6):
    beat = 60.0 / bpm
    out = []
    for note, beats in notes:
        out.extend(silence(beats * beat) if note == "-" else pluck(beats * beat, note, gain))
    return out


def bass(bpm, notes, gain=0.5):
    beat = 60.0 / bpm
    out = []
    for note, beats in notes:
        out.extend(silence(beats * beat) if note == "-" else tone(beats * beat, hz(note), kind="tri", release=0.7, gain=gain))
    return out


def minuet():
    """The Minuet in G (BWV Anh. 114): its first strain, sixteen bars of 3/4 in crotchets, with a simple bass."""
    e, q, h = 0.5, 1, 3
    first = [("D5", q), ("G4", e), ("A4", e), ("B4", e), ("C5", e), ("D5", q), ("G4", q), ("G4", q),
             ("E5", q), ("C5", e), ("D5", e), ("E5", e), ("F#5", e), ("G5", q), ("G4", q), ("G4", q),
             ("C5", q), ("D5", e), ("C5", e), ("B4", e), ("A4", e), ("B4", q), ("C5", e), ("B4", e), ("A4", e), ("G4", e)]
    lead = first + [("F#4", q), ("G4", e), ("A4", e), ("B4", e), ("G4", e), ("A4", h)] + \
        first + [("A4", q), ("B4", e), ("A4", e), ("G4", e), ("F#4", e), ("G4", h)]
    low_first = [("G3", 2), ("A3", 1), ("B3", 3), ("C4", 3), ("B3", 3), ("A3", 3), ("G3", 3)]
    low = low_first + [("D4", 1), ("B3", 1), ("G3", 1), ("D4", 1), ("D3", 1), ("C4", 1)] + \
        low_first + [("C4", 1), ("D4", 1), ("D3", 1), ("G3", 2), ("G2", 1)]
    return lead, low


def main():
    os.makedirs(OUT, exist_ok=True)
    for f in os.listdir(OUT):
        if f.endswith(".wav"):
            os.remove(os.path.join(OUT, f))
    # Building: the bulldozer's engine and scrape, a building going up, a zone marked out, a road laid, no.
    write("Bulldoze.wav", mix(tone(0.45, 55, 48, "square", attack=0.01, release=0.6, vibrato=0.25, vib_rate=18, gain=0.5),
                              tone(0.45, 900, 300, "noise", attack=0.02, release=0.8, gain=0.7)), 0.35)
    write("Build.wav", cat(*[mix(tone(0.07, 160, 90, "sine", attack=0.001, release=1.5), tone(0.04, 2000, 900, "noise", attack=0.001, release=1.0, gain=0.4), silence(0.09))
                             for _ in range(2)], bell(0.3, hz("G5"), 0.3)), 0.3)
    write("Zone.wav", cat(tone(0.06, hz("E5"), kind="tri", release=0.8), tone(0.12, hz("A5"), kind="tri", release=1.2)), 0.22)
    write("Click.wav", mix(tone(0.03, 1800, 900, "noise", attack=0.001, release=1.0), tone(0.025, 700, 500, "square", attack=0.001, release=1.0, gain=0.3)), 0.15)
    write("Refused.wav", tone(0.22, 110, kind="square", release=0.5, gain=0.5), 0.18)
    # Disasters: the siren, an explosion, the monster, the earthquake, the wind, the water.
    siren = []
    for _ in range(2):
        siren += tone(0.55, 620, 960, "sine", attack=0.02, release=0.0, gain=0.8)
        siren += tone(0.55, 960, 620, "sine", attack=0.001, release=0.0, gain=0.8)
    write("Siren.wav", [s * min(1, (len(siren) - i) / (RATE * 0.2)) for i, s in enumerate(siren)], 0.4)
    write("Explosion.wav", mix(tone(1.4, 1200, 120, "noise", attack=0.002, release=1.6), tone(1.2, 60, 30, "sine", attack=0.002, release=1.2, gain=0.9)), 0.6)
    write("Roar.wav", mix(swell(tone(1.6, 160, 70, "saw", attack=0.05, release=0.8, vibrato=0.08, vib_rate=11, gain=0.7), 0.15),
                          swell(tone(1.6, 500, 160, "noise", attack=0.05, release=0.9, gain=0.5), 0.2),
                          tone(1.6, 110, 55, "square", attack=0.05, release=1.0, vibrato=0.1, vib_rate=7, gain=0.3)), 0.55)
    write("Quake.wav", mix(tone(2.4, 70, 40, "noise", attack=0.1, release=0.8), tone(2.4, 42, 30, "square", attack=0.1, release=0.8, vibrato=0.3, vib_rate=9, gain=0.45)), 0.55)
    write("Wind.wav", swell(mix(tone(2.4, 300, 700, "noise", attack=0.3, release=0.7, gain=0.8), tone(2.4, 900, 400, "noise", attack=0.3, release=0.9, gain=0.3)), 0.35), 0.4)
    write("Splash.wav", cat(tone(0.5, 3000, 500, "noise", attack=0.005, release=1.2), *[tone(0.06, 500 - i * 40, 300, "sine", attack=0.005, release=1.0, gain=0.4) for i in range(5)]), 0.4)
    # The city: a car horn in a jam, a train's whistle, the budget's chime, the fanfare of a milestone.
    horn = mix(tone(0.32, 392, kind="square", attack=0.01, release=0.15, gain=0.4), tone(0.32, 494, kind="square", attack=0.01, release=0.15, gain=0.35))
    write("Horn.wav", cat(horn[: int(RATE * 0.18)], silence(0.07), horn), 0.3)
    whistle = mix(tone(0.9, 620, kind="sine", attack=0.03, release=0.3, vibrato=0.006, vib_rate=6, gain=0.5),
                  tone(0.9, 780, kind="sine", attack=0.03, release=0.3, vibrato=0.006, vib_rate=6, gain=0.4),
                  tone(0.9, 930, kind="sine", attack=0.03, release=0.3, gain=0.2))
    chuff = cat(*[tone(0.11, 900, 400, "noise", attack=0.003, release=1.5, gain=0.35) for _ in range(6)])
    write("Train.wav", mix(whistle, chuff), 0.3)
    write("Chime.wav", mix(bell(1.4, hz("C6")), cat(silence(0.18), bell(1.2, hz("E6"))), cat(silence(0.36), bell(1.1, hz("G6")))), 0.35)
    brass = lambda d, n: mix(tone(d, hz(n), kind="saw", attack=0.02, release=0.4, gain=0.45), tone(d, hz(n) * 1.004, kind="square", attack=0.02, release=0.4, gain=0.2))
    write("Fanfare.wav", cat(brass(0.14, "C5"), brass(0.14, "E5"), brass(0.14, "G5"), brass(0.6, "C6")), 0.4)
    # The title: the Minuet in G.
    lead, low = minuet()
    write("TitleTune.wav", cat(mix(play(112, lead), bass(112, low)), silence(1.5)), 0.32)


if __name__ == "__main__":
    main()
