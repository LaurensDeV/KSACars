#!/usr/bin/env python3
"""
Writes the circuits the roads are pushed with into the circuit library.

    ./tools/roads/extreme-circuits.py            # into <KSA user dir>/KSACars/Circuits/
    ./tools/roads/extreme-circuits.py --list     # their names and what each is for

Each is laid out in metres east and north of its own place on Earth, 2.5 km from the next, on the flat
ground west of -24, -62. They are meant to be driven by the bridge's `lap`, not to be pleasant.

tests/KSACars.Tests/ExtremeCircuits.cs lays the first eight for the headless laps, point for point: a
change to one of those here has to be made there too. tools/roads/rig-laps.sh drives the files themselves.
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
    def __init__(self, name, what, slot, width=10.0, closed=False, north=0.0, bank=None):
        self.name, self.what, self.slot, self.width, self.closed = name, what, slot, width, closed
        self.north, self.bank = north, bank
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
        nodes = [{"id": i + 1, "lat_deg": LAT0 + (self.north + n) / M_PER_DEG_LAT,
                  "lon_deg": LON0 + (east0 + e) / M_PER_DEG_LON,
                  "corner": c, "height_m": h} for i, (e, n, h, c) in enumerate(self.nodes)]
        roads = [{"from": a, "to": b} for a, b in self.roads]
        if self.closed:
            roads.append({"from": len(self.nodes), "to": 1})
        if self.bank is not None:
            for road in roads:
                road["from_bank_deg"] = road["to_bank_deg"] = self.bank
        return {"version": 2 if self.bank is not None else 1, "name": self.name, "body": "Earth", "width_m": self.width,
                "nodes": nodes, "roads": roads}


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


# The harder set. tests/KSACars.Tests/ExtremeCircuits.cs mirrors only the eight above.

def stelvio():
    c = Course("Z Stelvio", "twelve 90 m legs climbing 10% between hairpins 18 m across, then 108 m back down in 600 m", 8, width=8.0)
    legs, rise = 12, 9.0
    for k in range(legs):
        y = 18.0 * k
        xs = (0.0, 90.0) if k % 2 == 0 else (90.0, 0.0)
        c.point(xs[0], y, rise * k)
        c.point(xs[1], y, rise * (k + 1))
    top_x = 0.0 if legs % 2 == 0 else 90.0
    c.point(top_x - 60.0, 18.0 * legs, rise * legs)
    c.point(top_x - 120.0, 18.0 * legs + 60.0, rise * legs)
    c.point(top_x - 120.0, 18.0 * legs + 660.0, 0.0)
    c.point(top_x - 120.0, 18.0 * legs + 760.0, 0.0)
    return c


def tower():
    c = Course("Z Tower", "a helix of 14 m radius climbing 9 m a turn for twelve turns, 14% on its inside, then 108 m down in 700 m", 9, width=8.0)
    turns, per_turn, radius = 12, 9.0, 14.0
    c.point(-70.0, -radius)
    for i in range(turns * 8 + 1):
        a = -math.pi / 2 + i * math.pi / 4
        c.point(radius * math.cos(a), radius * math.sin(a), per_turn * i / 8.0)
    top = turns * per_turn
    c.point(90.0, -radius, top)
    c.point(790.0, -radius, 0.0)
    c.point(900.0, -radius, 0.0)
    return c


def corkscrew():
    c = Course("Z Corkscrew", "a blind crest, 20 m down in 80 m through a left and a right, a dip, and 15 m back up in 60 m", 10)
    for e, n, h in ((0, 0, 20), (220, 0, 20), (260, 10, 14), (300, 30, 0), (340, 10, 0), (380, -10, 0),
                    (440, -10, 15), (500, 0, 15), (650, 0, 15), (760, 0, 0), (860, 0, 0)):
        c.point(float(e), float(n), float(h))
    c.nodes[0] = (0.0, 0.0, 0.0, 1.0)
    c.nodes.insert(1, (100.0, 0.0, 20.0, 1.0))
    c.roads = [(i, i + 1) for i in range(1, len(c.nodes))]
    return c


def ring(name, what, slot, radius, bank, width=12.0, points=16):
    c = Course(name, what, slot, width=width, closed=True, bank=bank)
    for i in range(points):
        a = 2.0 * math.pi * i / points
        c.point(radius * math.cos(a), radius * math.sin(a))
    return c


def bowl():
    # Anticlockwise, so the outside is on the right and a negative bank lifts it.
    return ring("Z Bowl", "a 50 m ring banked 30 degrees into the turn", 11, 50.0, -30.0)


def offcamber():
    return ring("Z Offcamber", "an 80 m ring banked 20 degrees the wrong way", 12, 80.0, 20.0)


def sky():
    c = Course("Z Sky", "up 300 m in 2.5 km, 1.5 km of 5 m road at that height with two 60 m bends, then 300 m down in 1.5 km to a hairpin", 13, width=5.0)
    for e, n, h in ((0, 0, 0), (150, 0, 0), (2650, 0, 300), (3100, 0, 300), (3220, 120, 300), (3220, 600, 300),
                    (3100, 720, 300), (2600, 720, 300), (1100, 720, 0), (1000, 720, 0), (970, 750, 0), (1000, 780, 0), (1200, 780, 0)):
        c.point(float(e), float(n), float(h))
    return c


def alley():
    c = Course("Z Alley", "a 4.5 m street round nine blocks, every corner a right angle on 25 m legs", 14, width=4.5, closed=True)
    for e, n in ((0, 0), (75, 0), (75, 25), (150, 25), (150, 100), (100, 100), (100, 150), (25, 150), (25, 75), (-25, 75), (-25, 25), (0, 25)):
        c.point(float(e), float(n))
    return c


def knot():
    c = Course("Z Knot", "a trefoil: one road crossing itself three times, alternately over and under", 15, closed=True)
    for i in range(36):
        t = 2.0 * math.pi * i / 36
        c.point(55.0 * (math.sin(t) + 2.0 * math.sin(2.0 * t)), 55.0 * (math.cos(t) - 2.0 * math.cos(2.0 * t)),
                7.0 * (1.0 - math.sin(3.0 * t)))
    return c


def marathon():
    c = Course("Z Marathon", "a 20 km lap: 6 km and 3 km straights with a weave down one side, 250 m corners", 0, width=12.0, closed=True, north=9000.0)
    r, lx, ly = 250.0, 6000.0, 3000.0
    corners = ((lx, 0.0, -90), (lx, ly, 0), (0.0, ly, 90), (0.0, 0.0, 180))
    c.point(r, -r)
    c.point(lx - r, -r)
    for cx, cy, start in corners:
        if (cx, cy) == (0.0, ly):                      # the weave, along the top, east to west
            for k in range(1, 12):
                c.point(lx - 500.0 * k, ly + (120.0 if k % 2 else -120.0) * (0 if k in (1, 11) else 1))
        ox = cx - r if cx > 0 else cx + r
        oy = cy - r if cy > 0 else cy + r
        for i in (1, 2, 3):
            a = math.radians(start + 22.5 * i)
            c.point(ox + r * math.cos(a), oy + r * math.sin(a))
    return c


def ramp(run, grade, ease=30.0, every=40.0):
    """Stations along a ramp and the height gained at each: the grade held for all but an ease at each end.

    Heights between points are a monotone cubic, which between two level points alone peaks at half as
    steep again as the ramp is on average; points along the ramp hold it to the grade asked for.
    """
    rise = grade * (run - ease)
    held = run - 2.0 * ease
    inner = [ease + held * k / max(1, round(held / every)) for k in range(max(1, round(held / every)) + 1)]
    return [(0.0, 0.0)] + [(d, grade * (d - 0.5 * ease)) for d in inner] + [(run, rise)]


def vertical():
    c = Course("Z Vertical", "four walls in a row, each steeper: 30%, 45%, 60% and 80% up, and 45%, 60%, 80% and 100% back down, a short flat on each", 16)
    east = 0.0
    c.point(east, 0.0, 0.0)
    east += 150.0
    for up, down in ((0.30, 0.45), (0.45, 0.60), (0.60, 0.80), (0.80, 1.00)):
        top = up * (130.0 - 30.0)
        for d, h in ramp(130.0, up):
            c.point(east + d, 0.0, h)
        east += 130.0 + 60.0
        back = 30.0 + top / down
        for d, h in ramp(back, down):
            c.point(east + d, 0.0, top - h)
        east += back + 80.0
    c.point(east + 70.0, 0.0, 0.0)
    return c


def cliffs():
    c = Course("Z Cliffs", "a lap that is never level for long: six climbs and six drops round a 300 m ring, 45% to 75%", 17, closed=True)
    r, grades = 300.0, (0.45, 0.55, 0.65, 0.75, 0.55, 0.45)
    cycle = 2.0 * math.pi * r / len(grades)

    def at(arc, height):
        a = arc / r
        c.point(r * math.cos(a), r * math.sin(a), height)

    for k, grade in enumerate(grades):
        start, run = k * cycle, 0.5 * (cycle - 90.0)
        top = grade * (run - 30.0)
        for d, h in ramp(run, grade):
            at(start + d, h)
        for d, h in ramp(run, grade):
            at(start + run + 45.0 + d, top - h)
    return c


class Lap:
    """A course drawn by driving it: straights and arcs from where the last one ended, each point with
    its own height, lean and width. A point put where one already is joins the road there, which is a
    junction, and `route` is every point in the order driven."""

    def __init__(self, name, what, slot, width=12.0):
        self.name, self.what, self.slot, self.width = name, what, slot, width
        self.x = self.y = 0.0
        self.heading = 0.0            # radians anticlockwise from east
        self.h, self.bank, self.w = 0.0, 0.0, width
        self.nodes, self.roads, self.route = [], [], []
        self.along, self.marks, self.last = 0.0, [], None

    def _point(self):
        if self.last is not None:
            self.along += math.dist(self.last, (self.x, self.y, self.h))
        self.last = (self.x, self.y, self.h)
        for i, n in enumerate(self.nodes):
            if math.hypot(n[0] - self.x, n[1] - self.y) < 0.5 and abs(n[2] - self.h) < 0.5:
                break
        else:
            self.nodes.append((self.x, self.y, self.h, self.bank, self.w))
            i = len(self.nodes) - 1
        if self.route:
            self.roads.append((self.route[-1], i + 1))
        self.route.append(i + 1)

    def start(self):
        self._point()
        return self

    def mark(self):
        """Opens a stretch where the driver is not to slow for a crest, or closes the one that is open."""
        self.marks.append(self.along)
        return self

    def straight(self, length, rise=0.0, pieces=1, bank=None, width=None):
        b0, w0 = self.bank, self.w
        for k in range(1, pieces + 1):
            self.x += math.cos(self.heading) * length / pieces
            self.y += math.sin(self.heading) * length / pieces
            self.h += rise / pieces
            self.bank = b0 + ((bank if bank is not None else b0) - b0) * k / pieces
            self.w = w0 + ((width if width is not None else w0) - w0) * k / pieces
            self._point()
        return self

    def climb(self, run, grade, ease=30.0):
        """Up or down at a held grade, eased at each end: see ramp()."""
        last = 0.0, 0.0
        for d, h in ramp(run, abs(grade), ease)[1:]:
            self.x += math.cos(self.heading) * (d - last[0])
            self.y += math.sin(self.heading) * (d - last[0])
            self.h += math.copysign(h - last[1], grade)
            last = d, h
            self._point()
        return self

    def arc(self, radius, degrees, rise=0.0, lean=0.0, every=30.0, width=None):
        """Left for a positive angle. `lean` is into the bend, whichever way it goes, and eases in and out over its first and last points."""
        steps = max(2, round(abs(degrees) / every))
        turn = math.radians(degrees) / steps
        side = 1.0 if degrees > 0 else -1.0
        w0 = self.w
        for k in range(1, steps + 1):
            cx = self.x - math.sin(self.heading) * radius * side
            cy = self.y + math.cos(self.heading) * radius * side
            self.heading += turn
            self.x = cx + math.sin(self.heading) * radius * side
            self.y = cy - math.cos(self.heading) * radius * side
            self.h += rise / steps
            self.bank = 0.0 if k == steps else -side * lean
            self.w = w0 + ((width if width is not None else w0) - w0) * k / steps
            self._point()
        return self

    def close(self):
        if self.route[-1] != self.route[0]:            # unless the last point drawn was the first one itself
            self.roads.append((self.route[-1], self.route[0]))
            self.route.append(self.route[0])
        return self

    def to_json(self):
        east0 = -self.slot * 2500.0
        nodes = [{"id": i + 1, "lat_deg": LAT0 + n / M_PER_DEG_LAT, "lon_deg": LON0 + (east0 + e) / M_PER_DEG_LON,
                  "corner": 1.0, "height_m": round(h, 3)} for i, (e, n, h, _, _) in enumerate(self.nodes)]
        roads = []
        for a, b in self.roads:
            (_, _, _, ba, wa), (_, _, _, bb, wb) = self.nodes[a - 1], self.nodes[b - 1]
            road = {"from": a, "to": b}
            if ba or bb:
                road["from_bank_deg"], road["to_bank_deg"] = round(ba, 2), round(bb, 2)
            if wa != self.width or wb != self.width:
                road["from_width_m"], road["to_width_m"] = round(wa, 2), round(wb, 2)
            roads.append(road)
        return {"version": 2, "name": self.name, "body": "Earth", "width_m": self.width, "nodes": nodes, "roads": roads,
                "route": self.route, "jumps": [[round(a - 60.0), round(b + 60.0)] for a, b in zip(self.marks[::2], self.marks[1::2])]}


def insane():
    c = Lap("Insane", "everything at once, at a racing car's size: 900 m flat out through a crossroads, a bowl banked 30 degrees, a 50% wall "
            "160 m up, a weave along the sky, a corkscrew down, a dive, a jump, the crossroads again and a flick", 18, width=14.0)
    c.x, c.y = 10.0, 0.0
    c.start()
    c.straight(750.0, pieces=5, width=16.0)            # the crossroads is the last point of this, at 760 east
    c.straight(150.0)
    c.arc(180.0, 180.0, lean=30.0, width=14.0)         # the bowl; its width sets how long the straight into the crossroads is later
    c.straight(100.0)
    c.climb(480.0, 0.50, ease=160.0)                   # the wall, to 160 m, its foot long enough to be met at speed
    c.straight(60.0, width=9.0)
    c.arc(180.0, -40.0, lean=20.0, every=20.0)         # the weave in the sky, 9 m wide
    c.arc(180.0, 80.0, lean=20.0, every=20.0)
    c.arc(180.0, -40.0, lean=20.0, every=20.0)
    c.straight(60.0, width=12.0)
    c.arc(80.0, 540.0, rise=-130.0, lean=25.0, every=30.0)   # the corkscrew, a turn and a half down 130 m
    c.straight(40.0)
    c.climb(130.0, -30.0 / 90.0, ease=40.0)            # the dive
    c.h = 0.0
    c.straight(150.0, pieces=2, width=14.0)
    c.mark()
    # The jump: a ramp held at 12% to its very lip, a table 3.4 m under the lip that the car clears, and a landing
    # as steep as it comes down. A ramp that eases off at its top is a hump the car follows and does not leave.
    for run, rise in ((40.0, 2.4), (50.0, 6.0), (47.0, 5.64), (3.0, 0.36)):
        c.straight(run, rise=rise)
    c.straight(6.0, rise=-3.4)
    c.straight(54.0)
    c.climb(140.0, -c.h / 100.0, ease=40.0)
    c.h = 0.0
    c.mark()
    c.straight(640.0 - c.x, pieces=2)                  # room to brake in after landing
    c.arc(120.0, -90.0, lean=20.0)
    c.x = 760.0
    c.straight(c.y)                                    # 80 m straight onto the crossroads, southbound: a junction wants room for its mouth
    c.straight(200.0, pieces=2)
    c.arc(120.0, -90.0, lean=20.0)
    c.straight(400.0, pieces=3)
    c.arc(60.0, -30.0, lean=10.0, every=15.0)          # the flick
    c.arc(60.0, 60.0, lean=10.0, every=15.0)
    c.arc(60.0, -30.0, lean=10.0, every=15.0)
    c.straight(c.x - 20.0)
    c.arc(120.0, -90.0, lean=20.0)
    c.straight(-110.0 - c.y)
    c.arc(110.0, -90.0, lean=20.0, every=22.5)
    c.close()
    return c


def sprint():
    c = Lap("Sprint", "short and hard: 1.3 km of a heavy stop, a climbing left over a crest, a downhill run to esses, "
            "a banked hairpin, a chicane and a tightening last corner, on a road 10 m wide", 19, width=10.0)
    for x, y, h, bank in ((0, 0, 0, 0), (140, 0, 0, 0), (200, -10, 0, 0), (225, -45, 1, 0), (215, -85, 3, 0), (245, -120, 6, 0),
                          (300, -130, 9, 0), (345, -105, 8, 0), (360, -60, 5, 0), (340, -10, 2, 0), (360, 35, 0, 0), (335, 75, 0, -8),
                          (290, 90, 0, -12), (250, 70, 0, -8), (200, 45, 0, 0), (150, 70, 2, 0), (100, 50, 3, 0), (50, 75, 2, 0),
                          (-10, 60, 0, 0), (-45, 25, 0, 0)):
        c.x, c.y, c.h, c.bank = float(x), float(y), float(h), float(bank)
        c._point()
    c.close()
    return c


BRUTAL = [stelvio, tower, corkscrew, bowl, offcamber, sky, alley, knot, marathon, vertical, cliffs]

COURSES = [hairpins, spiral, coaster, eight, speedway, chicane, kinks, grid] + BRUTAL


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--list", action="store_true", help="say what each circuit is for and write nothing")
    args = ap.parse_args()
    courses = [make() for make in COURSES] + [insane(), sprint()]
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
