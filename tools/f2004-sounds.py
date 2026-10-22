#!/usr/bin/env python3
"""
Makes the F2004's engine out of one recording: a 2005 Red Bull-Cosworth RB1, a 3.0 litre V10 of the
F2004's own formula, pulling away from the start at Goodwood.

    ./tools/f2004-sounds.py             # tools/audio/f2004/ -> src/KSACars/Sounds/
    ./tools/f2004-sounds.py --report    # print what would be written, write nothing

Four files, mono because the engine spatialises the source. The recording has one thing the car needs,
three seconds of the engine under load, and none of the rest: it starts with the car already running
among others and ends with it gone up the hill. So the loop under load is the recording's, and the
idle, the start and the key-off are that same note played back slower, as the engine would sound
turning slower: its own timbre, at revs it was never recorded at.

The note falls from 688 to 606 Hz through the take as the car bogs off the line, and a loop has to hold
one pitch, so the take is first replayed at a rate that follows the note and brings it to 641 Hz
throughout. That line is one bank's firing, two and a half to a turn, so the engine is at 15,380 rpm,
which the profile records as LoadRecordedRpm: read as all ten cylinders' the car idles like one racing
and screams an octave high at speed. The take is then cut down to the engine's own lines, every half order of
it, because what lies between them is the tyres and the crowd, and a squeal that comes round every
loop is heard as one. A steady hiss goes back under it in their place. The seam of each loop is a
quarter-second crossfade at equal power: a note this high has no place where its tail repeats its
head, which is what tools/buggy-sounds.py looks for.
"""

import argparse
import importlib.util
import pathlib

import numpy as np
from scipy.signal import butter, medfilt, sosfilt

REPO = pathlib.Path(__file__).resolve().parents[1]
SRC = REPO / "tools" / "audio" / "f2004"
OUT = REPO / "src" / "KSACars" / "Sounds"

_spec = importlib.util.spec_from_file_location("buggy_sounds", REPO / "tools" / "buggy-sounds.py")
cutter = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(cutter)

TAKE = "Red_Bull-Cosworth_RB1_(2005)_017s-022s.wav"

NOTE_HZ = 641.0
NOTE_PER_TURN = 2.5
LOAD_RPM = NOTE_HZ * 60.0 / NOTE_PER_TURN
IDLE_RPM = 4000.0
SCRAMBLE_ABOVE_HZ = 1800.0


def note_track(x, rate):
    """The engine's strongest line through the take, in Hz, one value a sample."""
    n, hop = int(0.08 * rate), int(0.02 * rate)
    times, hz = [], []
    for i in range(0, len(x) - n, hop):
        spectrum = np.abs(np.fft.rfft(x[i:i + n] * np.hanning(n), 8 * n))
        freqs = np.fft.rfftfreq(8 * n, 1.0 / rate)
        band = (freqs > 520.0) & (freqs < 760.0)
        times.append((i + n / 2) / rate)
        hz.append(freqs[band][np.argmax(spectrum[band])])
    smooth = np.polyval(np.polyfit(times, medfilt(hz, 9), 3), np.arange(len(x)) / rate)
    return np.clip(smooth, 560.0, 720.0)


def play(source, rates):
    """The looped source read at a rate that changes sample by sample: 1 is as recorded."""
    phase = np.cumsum(rates) % len(source)
    lower = phase.astype(np.int64)
    part = phase - lower
    return source[lower] * (1.0 - part) + source[(lower + 1) % len(source)] * part


def loop_of(x, rate, start, seconds, seam=0.25):
    """A loop whose end runs into its beginning: what followed the cut is faded out over the head."""
    a, n, f = int(start * rate), int(seconds * rate), int(seam * rate)
    loop = x[a:a + n].copy()
    up = np.sqrt(np.linspace(0.0, 1.0, f))
    loop[:f] = loop[:f] * up + x[a + n:a + n + f] * up[::-1]
    return loop


def level(x, rate, seconds=0.15):
    """The take at one loudness throughout: the car is driving away, and a loop that swells throbs."""
    n = int(seconds * rate)
    power = np.convolve(x * x, np.ones(n) / n, mode="same")
    return x / np.sqrt(np.maximum(power, power.max() * 1e-3))


def engine_only(x, rate):
    """
    The engine's half orders and nothing between them, below where they run together; above it, the
    take's own spectrum with its phases scrambled, which keeps the colour of the scream and none of
    what happened when. A steady hiss goes under both.
    """
    spectrum = np.fft.rfft(x)
    freqs = np.fft.rfftfreq(len(x), 1.0 / rate)
    half_order = NOTE_HZ / (2.0 * NOTE_PER_TURN)
    off = np.abs(freqs - (np.round(freqs / half_order) * half_order))
    keep = np.clip(1.5 - (off / ((0.015 * freqs) + 4.0)), 0.0, 1.0)
    keep[freqs < 0.5 * half_order] = 0.0
    rng = np.random.default_rng(2004)
    high = freqs > SCRAMBLE_ABOVE_HZ
    spectrum = np.where(high, np.abs(spectrum) * np.exp(2j * np.pi * rng.random(len(freqs))), spectrum * keep)
    engine = np.fft.irfft(spectrum, len(x))
    hiss = sosfilt(butter(2, [400.0, 5000.0], btype="bandpass", fs=rate, output="sos"), rng.standard_normal(len(x)))
    return engine + (hiss * (0.06 * engine.std() / hiss.std()))


def ramp(points, rate):
    """A rate or a level against time, straight between (seconds, value) points."""
    t = np.arange(int(points[-1][0] * rate)) / rate
    return np.interp(t, [p[0] for p in points], [p[1] for p in points])


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--report", action="store_true", help="print what would be written and write nothing")
    args = ap.parse_args()

    take, rate = cutter.read_mono(SRC / TAKE)
    # the launch is 0.9 s into the excerpt, and the car is leaving by 4.3
    note = cutter.cut(take, rate, 0.9, 4.3)
    first = note_track(note, rate)
    held = play(note, NOTE_HZ / first)[:len(note) - rate // 10]
    # once more over what the first pass left, which is the wobble as the tyres bite
    track = note_track(held, rate)
    held = play(held, NOTE_HZ / track)[:len(held) - rate // 10]
    load = loop_of(level(engine_only(held, rate), rate), rate, 0.5, 2.4)

    slow = IDLE_RPM / LOAD_RPM
    # a turning-over engine has none of the scream, which is all above this once the note is slowed
    soft = butter(2, 2000.0, fs=rate, output="sos")
    idle = loop_of(sosfilt(soft, play(load, np.full(int(4.0 * rate), slow))), rate, 0.5, 2.6)

    # cranked from nothing, a flare as it catches, and down onto the idle
    start = sosfilt(soft, play(load, ramp([(0.0, 0.03), (0.45, 0.09), (0.60, 0.50), (0.85, 0.42), (1.6, slow), (2.8, slow)], rate)))
    start *= ramp([(0.0, 0.0), (0.05, 0.25), (0.45, 0.35), (0.60, 1.0), (1.6, 0.8), (2.8, 0.8)], rate)

    stop = sosfilt(soft, play(load, ramp([(0.0, slow), (0.25, slow), (1.1, 0.03)], rate)))
    stop *= ramp([(0.0, 1.0), (0.25, 1.0), (1.1, 0.0)], rate) ** 1.5

    plan = {
        "KSACars_F1_Start.wav": cutter.fade(cutter.normalise(start), rate, 0.0, 0.5),
        "KSACars_F1_Idle.wav": cutter.normalise(idle) * 0.8,
        "KSACars_F1_Load.wav": cutter.normalise(load),
        "KSACars_F1_Stop.wav": cutter.normalise(stop) * 0.8,
    }
    print(f"  the note runs {first.max():.0f} to {first.min():.0f} Hz and is held at {NOTE_HZ:.0f}: {LOAD_RPM:.0f} rpm")
    for out, samples in plan.items():
        print(f"  {out:24} {len(samples) / rate:5.2f} s at {rate} Hz")
        if not args.report:
            cutter.write_mono(OUT / out, samples, rate)
    if not args.report:
        print(f"wrote {len(plan)} files into {OUT}")


if __name__ == "__main__":
    main()
