#!/usr/bin/env python3
"""
Cuts the Eldorado's engine out of two recordings: a 1966 Cadillac Coupe de Ville starting, and a 1980
Chevrolet Impala's V8.

    ./tools/eldorado-sounds.py             # tools/audio/eldorado/ -> src/KSACars/Sounds/
    ./tools/eldorado-sounds.py --report    # print what would be written, write nothing

Four files, mono because the engine spatialises the source. The start and the idle are the Cadillac's,
whose 429 is the Eldorado's own engine family. The loop at a held light-throttle RPM and the key-off
are the Impala's, because the Cadillac was recorded pulling away into the distance and arriving with
its door slammed over the shutdown: neither leaves a clean held note or a clean key-off.

Both are freesound's HQ previews, 128 kbit MP3s decoded to WAV; only the stretches used are kept. The held-RPM stretch is the flattest in the take, a steady line near 125 Hz: about 1,900 rpm
on a V8, which fires four times a turn. Loops are cut as tools/buggy-sounds.py cuts them.
"""

import argparse
import importlib.util
import pathlib

REPO = pathlib.Path(__file__).resolve().parents[1]
SRC = REPO / "tools" / "audio" / "eldorado"
OUT = REPO / "src" / "KSACars" / "Sounds"

_spec = importlib.util.spec_from_file_location("buggy_sounds", REPO / "tools" / "buggy-sounds.py")
cutter = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(cutter)

CADDY = "73729__galarne__cadillac-start_000s-006s.wav"
HELD = "779211__yannsauvin__chevrolet-impala-1980_044s-052s.wav"
TAIL = "779211__yannsauvin__chevrolet-impala-1980_146s-155s.wav"


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--report", action="store_true", help="print what would be written and write nothing")
    args = ap.parse_args()

    caddy, rate = cutter.read_mono(SRC / CADDY)
    # the Impala is 48 kHz and the Cadillac 44.1, so each file keeps its own rate
    held, impala = cutter.read_mono(SRC / HELD)
    tail, _ = cutter.read_mono(SRC / TAIL)

    # the crank catches by 2.5 s and the car pulls away at 5.8
    idle, idle_score = cutter.make_loop(cutter.cut(caddy, rate, 2.6, 5.7), rate)
    load, load_score = cutter.make_loop(cutter.cut(held, impala, 2.0, 6.5), impala)

    plan = {
        "KSACars_Eldo_Start.wav": (cutter.fade(cutter.normalise(cutter.cut(caddy, rate, 0.0, 3.2)), rate, 0.0, 0.6), rate),
        "KSACars_Eldo_Idle.wav": (cutter.normalise(idle), rate),
        "KSACars_Eldo_Load.wav": (cutter.normalise(load), impala),
        # the key-off lands 5.1 s into the tail excerpt, at 151.1 s in the take
        "KSACars_Eldo_Stop.wav": (cutter.fade(cutter.normalise(cutter.cut(tail, impala, 3.8, 6.9)), impala, 0.05, 0.4), impala),
    }
    print(f"  idle loop seam matches its head at r = {idle_score:.3f}")
    print(f"  load loop seam matches its head at r = {load_score:.3f}")
    for out, (samples, hz) in plan.items():
        print(f"  {out:26} {len(samples) / hz:5.2f} s at {hz} Hz")
        if not args.report:
            cutter.write_mono(OUT / out, samples, hz)
    if not args.report:
        print(f"wrote {len(plan)} files into {OUT}")


if __name__ == "__main__":
    main()
