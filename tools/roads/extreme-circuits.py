#!/usr/bin/env python3
"""
Writes the circuits the roads are pushed with into the circuit library.

    ./tools/roads/extreme-circuits.py            # into <KSA user dir>/KSACars/Circuits/
    ./tools/roads/extreme-circuits.py --list     # their names and what each is for

Each is laid out in metres east and north of its own place on Earth, 2.5 km from the next, on the flat
ground west of -24, -62. They are meant to be driven by the bridge's `lap`, not to be pleasant.
"""

import argparse
import json
import math
import pathlib
import subprocess

REPO = pathlib.Path(__file__).resolve().parents[2]
LAT0, LON0 = -24.0, -62.05
M_PER_DEG_LAT = 111_319.0
M_PER_DEG_LON = M_PER_DEG_LAT * math.cos(math.radians(LAT0))


class Course:
    def __init__(self, name, what, slot, width=10.0, closed=False):
        self.name, self.what, self.slot, self.width, self.closed = name, what, slot, width, closed
        self.nodes, self.roads = [], []

    def point(self, east, north, height=0.0, corner=1.0, join=True):
        self.nodes.append((east, north, height, corner))
        if join and len(self.nodes) > 1:
            self.roads.append((len(self.nodes) - 1, len(self.nodes)))
        return len(self.nodes)

    def road(self, a, b):
        self.roads.append((a, b))

    def to_json(self):
        east0 = -self.slot * 2500.0
        nodes = [{"id": i + 1, "lat_deg": LAT0 + n / M_PER_DEG_LAT, "lon_deg": LON0 + (east0 + e) / M_PER_DEG_LON,
                  "corner": c, "height_m": h} for i, (e, n, h, c) in enumerate(self.nodes)]
        roads = [{"from": a, "to": b} for a, b in self.roads]
        if self.closed:
            roads.append({"from": len(self.nodes), "to": 1})
        return {"version": 1, "name": self.name, "body": "Earth", "width_m": self.width, "nodes": nodes, "roads": roads}


def hairpins():
    c = Course("X Hairpins", "six 120 m legs joined by hairpins 24 m across", 0)
    for k in range(6):
        y = 24.0 * k
        xs = (0.0, 120.0) if k % 2 == 0 else (120.0, 0.0)
        c.point(xs[0], y)
        c.point(xs[1], y)
    return c


def spiral():
    c = Course("X Spiral", "a helix of 25 m radius climbing 7 m a turn for four turns, then 300 m straight back down", 1)
    turns, per_turn, radius = 4, 7.0, 25.0
    c.point(-80.0, -radius)
    for i in range(turns * 8 + 1):
        a = -math.pi / 2 + i * math.pi / 4
        c.point(radius * math.cos(a), radius * math.sin(a), per_turn * i / 8.0)
    top = turns * per_turn
    c.point(120.0, -radius, top)
    c.point(420.0, -radius, 0.0)
    c.point(520.0, -radius, 0.0)
    return c


def coaster():
    c = Course("X Coaster", "humps of 6 to 25 m on 60 to 100 m of road, peaking near 30%, with two bends", 2)
    for e, n, h in ((0, 0, 0), (80, 0, 0), (160, 0, 15), (240, 0, 0), (300, 10, 6), (360, 30, 0), (460, 40, 25),
                    (560, 30, 0), (620, 0, 10), (700, -20, 2), (780, -20, 12), (880, 0, 0), (980, 0, 0)):
        c.point(float(e), float(n), float(h))
    return c


def eight():
    c = Course("X Eight", "a figure of eight of 60 m loops, one road 6 m over the other where they cross", 3, closed=True)
    r = 60.0
    for i in range(8):                      # east loop, anticlockwise from the crossing
        a = math.pi + i * math.pi / 4
        c.point(r + r * math.cos(a), r * math.sin(a), 6.0 if i == 0 else max(0.0, 6.0 - 3.0 * min(i, 8 - i)))
    for i in range(1, 8):                   # west loop, clockwise, under the bridge at the crossing
        a = -i * math.pi / 4
        c.point(-r + r * math.cos(a), r * math.sin(a), 0.0)
    return c


def speedway():
    c = Course("X Speedway", "a 3.3 km oval, 1 km straights and 180 m bends, 14 m wide", 4, width=14.0, closed=True)
    r, half = 180.0, 500.0
    c.point(-half, -r)
    c.point(half, -r)
    for i in range(1, 8):
        a = -math.pi / 2 + i * math.pi / 8
        c.point(half + r * math.cos(a), r * math.sin(a))
    c.point(half, r)
    c.point(-half, r)
    for i in range(1, 8):
        a = math.pi / 2 + i * math.pi / 8
        c.point(-half + r * math.cos(a), r * math.sin(a))
    return c


def chicane():
    c = Course("X Chicane", "a 6 m road swinging 15 m either side every 40 m, ten times", 5, width=6.0)
    c.point(-60.0, 0.0)
    for k in range(11):
        c.point(40.0 * k, 15.0 * (1 if k % 2 else -1) if 0 < k < 10 else 0.0)
    c.point(460.0, 0.0)
    return c


def kinks():
    c = Course("X Kinks", "straight roads meeting at 30, 60, 90 and 120 degrees with no corner rounding", 6)
    x = y = heading = 0.0
    c.point(x, y, corner=0.0)
    for turn in (0, 30, -60, 90, -120, 60, 0):
        heading += math.radians(turn)
        x, y = x + 90.0 * math.cos(heading), y + 90.0 * math.sin(heading)
        c.point(x, y, corner=0.0)
    return c


def grid():
    c = Course("X Grid", "a 3 by 3 grid of crossroads 100 m apart, the middle one 5 m up", 7)
    ids = {}
    for j in range(3):
        for i in range(3):
            ids[i, j] = c.point(100.0 * i, 100.0 * j, 5.0 if (i, j) == (1, 1) else 0.0, join=False)
    for j in range(3):
        for i in range(3):
            if i < 2:
                c.road(ids[i, j], ids[i + 1, j])
            if j < 2:
                c.road(ids[i, j], ids[i, j + 1])
    return c


COURSES = [hairpins, spiral, coaster, eight, speedway, chicane, kinks, grid]


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--list", action="store_true", help="say what each circuit is for and write nothing")
    args = ap.parse_args()
    courses = [make() for make in COURSES]
    if args.list:
        for c in courses:
            print(f"{c.name:12} {len(c.nodes):3} points  {c.what}")
        return
    user = subprocess.run([str(REPO / "tools" / "ksa-user-dir.sh")], capture_output=True, text=True, check=True).stdout.strip()
    library = pathlib.Path(user) / "KSACars" / "Circuits"
    library.mkdir(parents=True, exist_ok=True)
    for c in courses:
        (library / f"{c.name}.json").write_text(json.dumps(c.to_json(), indent=1))
    print(f"wrote {len(courses)} circuits into {library}")


if __name__ == "__main__":
    main()
