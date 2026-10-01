#!/usr/bin/env python3
"""
Cuts the beach buggy's engine out of two recordings of air-cooled VW Beetles.

    ./tools/buggy-sounds.py             # tools/audio/buggy/ -> src/KSACars/Sounds/
    ./tools/buggy-sounds.py --report    # print what would be written, write nothing

Four files: the starter catching, an idle loop, a loop under load, and the shut-off. Mono, because the
engine spatialises the source. Each is normalised to the same peak, and the mod mixes them.

The idle and the shut-off are one recording of a '74 Bug; the load loop is the steadiest 1.7 s of a
contact mic on another '74 Bug's hood while it drives, found by tracking its strongest engine line and
taking the window where it moves least. Both loops are cut where the recording comes back round to how
it starts, and the seam crossfaded.
"""

import argparse
import pathlib
import sys
import wave

import numpy as np
from scipy.signal import resample_poly

REPO = pathlib.Path(__file__).resolve().parents[1]
SRC = REPO / "tools" / "audio" / "buggy"
OUT = REPO / "src" / "KSACars" / "Sounds"

START = "734252__yfjesse__car-engine-start.wav"
# 148-152 s of the three-minute original; the rest of it is road noise and gear changes.
LOAD = "331707__be_a_hero_not_a_patriot__auto-74-vw-bug-ext-contact-mic-on-hood_148s-152s.wav"

HEAD_SECONDS = 0.25
SEARCH_SECONDS = 0.7
CROSSFADE_SECONDS = 0.02
PEAK = 0.7 * 32767


def read_mono(path):
    with wave.open(str(path)) as w:
        channels, width, rate, frames = w.getnchannels(), w.getsampwidth(), w.getframerate(), w.getnframes()
        raw = w.readframes(frames)
    if width == 2:
        x = np.frombuffer(raw, dtype="<i2").astype(np.float64)
    elif width == 3:
        b = np.frombuffer(raw, dtype=np.uint8).reshape(-1, 3).astype(np.int32)
        x = b[:, 0] | (b[:, 1] << 8) | (b[:, 2] << 16)
        x = np.where(x >= 1 << 23, x - (1 << 24), x).astype(np.float64) / 256.0
    else:
        sys.exit(f"{path.name}: {width * 8}-bit is not handled")
    x = x.reshape(-1, channels).mean(axis=1)
    if rate == 96000:
        x, rate = resample_poly(x, 1, 2), 48000
    return x, rate


def write_mono(path, samples, rate):
    with wave.open(str(path), "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(rate)
        w.writeframes(np.clip(np.round(samples), -32768, 32767).astype("<i2").tobytes())


def cut(x, rate, start, end):
    return x[int(start * rate):int(end * rate)].copy()


def fade(x, rate, fade_in=0.0, fade_out=0.0):
    n_in, n_out = int(fade_in * rate), int(fade_out * rate)
    if n_in:
        x[:n_in] *= np.linspace(0.0, 1.0, n_in)
    if n_out:
        x[-n_out:] *= np.linspace(1.0, 0.0, n_out)
    return x


def normalise(x):
    x = x - x.mean()
    return x * (PEAK / (np.abs(x).max() + 1e-9))


def make_loop(x, rate):
    """Cut where the tail best repeats the head, and blend what would have followed onto the head."""
    head = x[:int(HEAD_SECONDS * rate)]
    head = (head - head.mean()) / (head.std() + 1e-9)
    n = len(head)
    best, best_score = len(x) - n, -2.0
    for end in range(len(x) - int(SEARCH_SECONDS * rate), len(x) - n):
        seg = x[end:end + n]
        score = float(np.dot(head, (seg - seg.mean()) / (seg.std() + 1e-9))) / n
        if score > best_score:
            best, best_score = end, score
    f = int(CROSSFADE_SECONDS * rate)
    loop = x[:best].copy()
    ramp = np.linspace(0.0, 1.0, f)
    loop[:f] = x[:f] * ramp + x[best:best + f] * (1.0 - ramp)
    return loop, best_score


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--report", action="store_true", help="print what would be written and write nothing")
    args = ap.parse_args()

    start, rate_s = read_mono(SRC / START)
    load, rate_l = read_mono(SRC / LOAD)

    idle, idle_score = make_loop(cut(start, rate_s, 5.0, 9.0), rate_s)
    loaded, load_score = make_loop(cut(load, rate_l, 1.3, 3.0), rate_l)

    plan = {
        "KSACars_Buggy_Start.wav": (fade(normalise(cut(start, rate_s, 0.0, 2.6)), rate_s, 0.0, 0.6), rate_s),
        "KSACars_Buggy_Idle.wav": (normalise(idle), rate_s),
        "KSACars_Buggy_Load.wav": (normalise(loaded), rate_l),
        "KSACars_Buggy_Stop.wav": (fade(normalise(cut(start, rate_s, 16.9, 19.5)), rate_s, 0.1, 0.3), rate_s),
    }
    print(f"  idle loop seam matches its head at r = {idle_score:.3f}")
    print(f"  load loop seam matches its head at r = {load_score:.3f}")
    for out, (samples, rate) in plan.items():
        print(f"  {out:28} {len(samples) / rate:5.2f} s at {rate} Hz")
        if not args.report:
            write_mono(OUT / out, samples, rate)
    if not args.report:
        print(f"wrote {len(plan)} files into {OUT}")


if __name__ == "__main__":
    main()
