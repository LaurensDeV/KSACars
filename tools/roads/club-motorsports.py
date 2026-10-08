#!/usr/bin/env python3
"""Writes Club Motorsports, a real 3.6 km road course in Tamworth, New Hampshire, into the circuit library.

    ./tools/roads/club-motorsports.py            # 'Club Motorsports' and 'Club Motorsports Flat'
    ./tools/roads/club-motorsports.py --print    # the turns and the heights, and nothing written

The line and its heights are club-motorsports.csv, which says where each is from. A circuit's points are
taken from it closer together where it bends, and each is as high above the ground as the real track is
above its lowest place, since KSA's ground cannot be shaped: on level ground the lap climbs and falls as
the real one does, on fill and on decks. The flat one is the same line on the ground.
"""
import argparse
import csv
import json
import math
import subprocess
from pathlib import Path

HERE = Path(__file__).resolve().parent
REPO = HERE.parent.parent

# Level ground south of the short course: 1.2 m of fall across the whole lap.
AT = {"lat_deg": -24.0445, "lon_deg": -62.4905}
WIDTH_M = 12.2          # 40 ft
STEP_M = 10.0           # the csv's spacing
LEAST_M, MOST_M = 20.0, 60.0


def line():
    rows = [r for r in csv.reader(l for l in (HERE / "club-motorsports.csv").read_text().splitlines() if not l.startswith("#"))][1:]
    return [(float(e), float(n), float(z)) for e, n, z in rows]


def radius(pts, i):
    """How tightly the line turns at a sample, m, from its heading 30 m either side."""
    n = len(pts)

    def heading(k):
        (ax, ay, _), (bx, by, _) = pts[(k - 2) % n], pts[(k + 2) % n]
        return math.atan2(by - ay, bx - ax)

    turn = (heading(i + 3) - heading(i - 3) + math.pi) % (2.0 * math.pi) - math.pi
    return 60.0 / abs(turn) if abs(turn) > 1e-6 else math.inf, turn


def points(pts):
    """The samples a circuit's points are put at: a third of the bend's radius apart, 20 m to 60 m."""
    n, i, picked = len(pts), 0, []
    while i < n:
        picked.append(i)
        gap = min(max(radius(pts, i)[0] / 3.0, LEAST_M), MOST_M)
        i += max(int(round(gap / STEP_M)), 1)
    # The last gap closes the lap: not a sliver.
    if (n - picked[-1]) * STEP_M < LEAST_M:
        picked.pop()
    return picked


def heights(pts):
    """Each sample's height over the lowest, smoothed over 30 m: a lidar's metre of ground is rougher than asphalt."""
    n = len(pts)
    smooth = [sum(pts[(i + k) % n][2] for k in (-1, 0, 1)) / 3.0 for i in range(n)]
    low = min(smooth)
    return [z - low for z in smooth]


def circuit(name, raised):
    pts = line()
    high = heights(pts)
    picked = points(pts)
    nodes = []
    for k, i in enumerate(picked):
        node = {"id": k + 1, "east_m": round(pts[i][0], 3), "north_m": round(pts[i][1], 3)}
        if raised and high[i] >= 0.001:
            node["height_m"] = round(high[i], 3)
        nodes.append(node)
    count = len(nodes)
    roads = [{"from": k + 1, "to": (k + 1) % count + 1} for k in range(count)]
    return {"version": 3, "name": name, "body": "Earth", "radius_m": 6371000, "at": AT, "width_m": WIDTH_M,
            "route": [k + 1 for k in range(count)] + [1], "nodes": nodes, "roads": roads}


def write(saved, folder):
    """A point a line, as the game writes it."""
    head = {k: v for k, v in saved.items() if k not in ("nodes", "roads")}
    text = json.dumps(head, indent=2)[:-2]
    for key in ("nodes", "roads"):
        text += ',\n  "%s": [\n    ' % key + ",\n    ".join(json.dumps(item, separators=(",", ":")) for item in saved[key]) + "\n  ]"
    (folder / f"{saved['name']}.json").write_text(text + "\n}\n")


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--print", action="store_true", help="say what the lap is and write nothing")
    args = ap.parse_args()

    pts, high = line(), heights(line())
    picked = points(pts)
    climb = sum(max(high[(i + 1) % len(high)] - high[i], 0.0) for i in range(len(high)))
    steep = max((high[(i + 3) % len(high)] - high[i]) / (3.0 * STEP_M) for i in range(len(high)))
    print(f"{len(pts) * STEP_M:.0f} m, {len(picked)} points, {max(high):.1f} m from lowest to highest, "
          f"{climb:.0f} m climbed a lap, {100.0 * steep:.1f}% at the steepest")
    if args.print:
        for i in range(0, len(pts), 5):
            r, turn = radius(pts, i)
            print(f"  {i * STEP_M:5.0f} m  {high[i]:5.1f} m up  " + ("straight" if r > 600.0 else f"{'left' if turn > 0 else 'right'} {r:.0f} m"))
        return

    user = subprocess.run([str(REPO / "tools" / "ksa-user-dir.sh")], capture_output=True, text=True, check=True).stdout.strip()
    folder = Path(user) / "KSACars" / "Circuits"
    folder.mkdir(parents=True, exist_ok=True)
    write(circuit("Club Motorsports", raised=True), folder)
    write(circuit("Club Motorsports Flat", raised=False), folder)
    print(f"wrote Club Motorsports.json and Club Motorsports Flat.json to {folder}")


if __name__ == "__main__":
    main()
