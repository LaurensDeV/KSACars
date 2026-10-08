#!/usr/bin/env python3
"""
Searches for a faster lap of one circuit by one car, in the headless rig.

    ./tools/roads/personal-best.py Sprint                  # carry on from the best so far
    ./tools/roads/personal-best.py Sprint --rounds 30
    ./tools/roads/personal-best.py Sprint --show           # the best so far, as the game's lap wants it

A way of driving is the driver's settings (Autopilot.Tuning) and how far inside the road's edge its
racing line keeps. Each round tries a couple of dozen near the best one, all at once, and a way only
counts if its flying lap is clean (finished, no wheel off the asphalt, hull never down) at every kind
of step the rig has, and with 6% less grip than the driver takes there to be; its time is the slowest
of those with all the grip. The best and every round's are kept in
tools/roads/pb/<circuit>.json, so a search can be stopped and carried on.
"""

import argparse
import json
import math
import os
import pathlib
import random
import subprocess
import tempfile

REPO = pathlib.Path(__file__).resolve().parents[2]

# name: (start, least, most)
SETTINGS = {
    "grip_share": (0.85, 0.5, 2.0), "brake_share": (0.7, 0.4, 1.5), "tip_share": (0.6, 0.4, 1.2),
    "crest_share": (0.6, 0.3, 1.5), "dip_share": (0.35, 0.15, 1.5), "look_ahead_seconds": (0.4, 0.12, 1.0),
    "least_look_ahead_m": (4.0, 1.5, 12.0), "brake_on_ms": (0.3, 0.05, 2.5), "brake_on_share": (0.04, 0.005, 0.12),
    "brake_off_ms": (0.1, 0.0, 1.5), "brake_lead_seconds": (0.1, 0.0, 0.5), "full_throttle_under_ms": (2.0, 0.2, 5.0),
    "dip_brake_share": (0.3, 0.1, 1.5), "brake_band_ms": (0.0, 0.0, 6.0), "least_brake": (0.1, 0.0, 0.6), "inside_m": (1.635, 0.85, 3.0),
}


PACES = 24
for _k in range(PACES):
    SETTINGS[f"pace_{_k:02}"] = (1.0, 0.75, 1.6)


NUDGES = 24
for _k in range(NUDGES):
    SETTINGS[f"nudge_{_k:02}"] = (0.0, -2.5, 2.5)


def nudged(way):
    return ";".join(f"{way[f'nudge_{k:02}']:.3f}" for k in range(NUDGES))


def tuned(way):
    """A way as the driver's settings are written: its paces together, in order."""
    plain = ",".join(f"{k}={v:.5g}" for k, v in way.items() if k != "inside_m" and not k.startswith(("pace_", "nudge_")))
    return plain + ",pace=" + ";".join(f"{way[f'pace_{k:02}']:.4g}" for k in range(PACES))


def library():
    user = subprocess.run([str(REPO / "tools" / "ksa-user-dir.sh")], capture_output=True, text=True, check=True).stdout.strip()
    return pathlib.Path(user) / "KSACars" / "Circuits"


def line(name, way):
    return f"{name}|{tuned(way)}|{way['inside_m']:.4f}|0|{nudged(way)}"


def timed(circuit, car, ways):
    """The slowest clean flying lap of each way, or None with why it is not one."""
    with tempfile.TemporaryDirectory() as tmp:
        file = pathlib.Path(tmp) / "candidates.txt"
        file.write_text("\n".join(line(str(i), w) for i, w in enumerate(ways)) + "\n")
        env = dict(os.environ, KSACARS_CANDIDATES=str(file), KSACARS_CIRCUIT=str(library() / f"{circuit}.json"), KSACARS_CAR=car)
        run = subprocess.run([str(REPO / "tools" / "test.sh"), "--filter", "LapSearchTests"], capture_output=True, text=True, env=env)
        out = pathlib.Path(str(file) + ".out")
        if not out.exists():
            raise SystemExit(run.stdout[-2000:])
        results = [None] * len(ways)
        for row in out.read_text().splitlines():
            i, a, b = row.split("|", 2)
            results[int(i)] = (None, b) if a == "fault" else (float(a), float(b))
        return results


def near(best, rng, spread):
    way = dict(best)
    for k in rng.sample(list(SETTINGS), rng.choice((1, 1, 2, 3, 5, len(SETTINGS)))):
        _, low, high = SETTINGS[k]
        scale = (high - low) * spread
        way[k] = min(high, max(low, way[k] + rng.gauss(0.0, scale)))
    return way


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("circuit")
    ap.add_argument("--car", default="F2004")
    ap.add_argument("--rounds", type=int, default=12)
    ap.add_argument("--each", type=int, default=28, help="ways tried a round")
    ap.add_argument("--heat", type=float, default=1.0, help="how far from the best a round looks, as a multiple: a search that has settled looks close")
    ap.add_argument("--show", action="store_true")
    ap.add_argument("--recheck", action="store_true", help="time the best so far again first, after a change to what counts as clean")
    ap.add_argument("--seed", type=int)
    args = ap.parse_args()

    kept = REPO / "tools" / "roads" / "pb" / f"{args.circuit}-{args.car}.json"
    kept.parent.mkdir(exist_ok=True)
    state = json.loads(kept.read_text()) if kept.exists() else {"best": None, "time": None, "rounds": []}
    if args.show:
        if not state["best"]:
            raise SystemExit("no lap yet")
        way = state["best"]
        print(f"{state['time']:.3f} s, {len(state['rounds'])} rounds\ntune: {tuned(way)}\ninside_m: {way['inside_m']:.3f}\nline: {nudged(way)}")
        return

    rng = random.Random(args.seed)
    for k, v in SETTINGS.items():                      # a setting the driver has gained since the best was found
        if state["best"] is not None:
            state["best"].setdefault(k, v[0])
    if args.recheck and state["best"]:
        # The rules a lap is clean by have changed: the best is timed again, with less taken of the grip until it is clean.
        way = dict(state["best"])
        for _ in range(40):
            (time, why), = timed(args.circuit, args.car, [way])
            if time is not None:
                break
            way["grip_share"] *= 0.98
        else:
            raise SystemExit(f"the best so far is not clean however little grip it takes: {why}")
        print(f"the best so far, as a lap now has to be: {time:.3f} s (it was {state['time']:.3f})", flush=True)
        state.update(best=way, time=time)
        kept.write_text(json.dumps(state, indent=1))
    if not state["best"]:
        start = {k: v[0] for k, v in SETTINGS.items()}
        (time, _), = timed(args.circuit, args.car, [start])
        if time is None:
            raise SystemExit("the driver's own settings do not lap this cleanly")
        state.update(best=start, time=time, first=time)
        print(f"as the driver is: {time:.3f} s", flush=True)

    for _ in range(args.rounds):
        n = len(state["rounds"])
        spread = args.heat * 0.12 * (0.5 ** (min(n, 40) / 14.0)) * rng.choice((0.4, 1.0, 1.0, 2.0))
        ways = [near(state["best"], rng, spread) for _ in range(args.each)]
        results = timed(args.circuit, args.car, ways)
        clean = [(r[0], w) for r, w in zip(results, ways) if r and r[0] is not None]
        faults = len(ways) - len(clean)
        won = min(clean, key=lambda x: x[0]) if clean else None
        better = won is not None and won[0] < state["time"] - 1e-4
        if better:
            state["time"], state["best"] = won
        state["rounds"].append({"round": n + 1, "best_of_round": won[0] if won else None, "faults": faults, "time": state["time"], "spread": spread})
        kept.write_text(json.dumps(state, indent=1))
        mark = "  <-- new best" if better else ""
        print(f"round {n + 1:3}: best of round {won[0] if won else float('nan'):7.3f} s, {faults:2} of {len(ways)} not clean; personal best {state['time']:.3f} s{mark}", flush=True)


if __name__ == "__main__":
    main()
