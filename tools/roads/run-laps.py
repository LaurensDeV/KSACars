#!/usr/bin/env python3
"""
Drives cars round circuits in a running game with the bridge's `lap`, and prints how each run ended.

    ./tools/roads/run-laps.py                                  # every X circuit, every car
    ./tools/roads/run-laps.py --circuit "X Spiral" --car Eldorado --speed 20 --offset 2

A run that does not end Finished, leaves the asphalt, flies, or leans is marked, and its summary is kept by
the game under Logs/bridge/KSACars/laps/. The game has to be running with a save loaded.
"""

import argparse
import json
import pathlib
import subprocess
import sys
import time

REPO = pathlib.Path(__file__).resolve().parents[2]
CARS = {"F2004": "Ferrari F2004", "Eldorado": "Eldorado", "Buggy": "Beach Buggy"}


def bridge(tool, timeout=120, **args):
    out = subprocess.run([sys.executable, str(REPO / "tools" / "ksa-mcp" / "server.py"), "cli", tool, json.dumps(args)],
                         capture_output=True, text=True, timeout=timeout).stdout
    start = out.find("{")
    try:
        return json.loads(out[start:]) if start >= 0 else {"error": out.strip()}
    except json.JSONDecodeError:
        return {"error": out.strip()[:200]}


def library():
    user = subprocess.run([str(REPO / "tools" / "ksa-user-dir.sh")], capture_output=True, text=True, check=True).stdout.strip()
    return pathlib.Path(user) / "KSACars" / "Circuits"


def lap(craft, wait_s, **args):
    started = bridge("lap", start=True, craft=craft, **args)
    if "running" not in started:
        return {"end": "Refused", "why": str(started)[:160]}
    deadline = time.time() + wait_s
    while time.time() < deadline:
        time.sleep(2.0)
        status = bridge("lap", craft=craft)
        if status.get("running") is False:
            return status
    bridge("lap", stop=True, craft=craft)
    return {**bridge("lap", craft=craft), "end": "RunnerGaveUp"}


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--circuit", action="append", help="a circuit's name; default every one starting with X")
    ap.add_argument("--car", action="append", choices=list(CARS), help="default all three")
    ap.add_argument("--speed", type=float, help="cruise in m/s; default each car's top speed")
    ap.add_argument("--offset", type=float, default=0.0, help="metres left of the centre line")
    ap.add_argument("--route", help="point ids to pass through, comma separated")
    ap.add_argument("--laps", type=int, default=1)
    ap.add_argument("--wait", type=float, default=300.0, help="wall seconds to give a run")
    args = ap.parse_args()

    circuits = args.circuit or sorted(p.stem for p in library().glob("X *.json"))
    cars = args.car or list(CARS)
    present = {o["name"].replace("(flown) ", "") for o in bridge("status").get("others", [])}

    for circuit in circuits:
        first = json.loads((library() / f"{circuit}.json").read_text())["nodes"][0]
        print(f"== {circuit}")
        for car in cars:
            craft = f"Lap {car}"
            if craft not in present:
                bridge("spawn", craft=CARS[car], name=craft, lat=first["lat_deg"] - 0.0003, lon=first["lon_deg"])
                present.add(craft)
                time.sleep(3.0)
            else:
                bridge("site", craft=craft, lat=first["lat_deg"] - 0.0003, lon=first["lon_deg"], timeout=90)
                time.sleep(2.0)
            bridge("drive", craft=craft, focus=True, cam_elevation_deg=30, cam_azimuth_deg=270, cam_distance=2.3)
            time.sleep(1.0)
            laid = bridge("road", circuit=circuit)
            if not laid.get("laid"):
                print(f"   {car:9} road not laid: {str(laid)[:120]}")
                continue
            time.sleep(1.5)
            extra = {k: v for k, v in (("speed", args.speed), ("route", args.route)) if v is not None}
            s = lap(craft, args.wait, laps=args.laps, offset=args.offset, timeout=240, **extra)
            # Out of the way of the next car: an open route ends in one place, and a car left standing
            # there is what the next one runs into.
            bridge("site", craft=craft, lat=first["lat_deg"] - 0.003 - 0.0004 * cars.index(car), lon=first["lon_deg"], timeout=90)
            bad = [w for w, hit in (
                (s.get("end", "?"), s.get("end") != "Finished"),
                (f"off asphalt {s.get('off_asphalt_s')} s", (s.get("off_asphalt_s") or 0) > 0.2),
                (f"flight {s.get('longest_flight_s')} s", (s.get("longest_flight_s") or 0) > 0.5),
                (f"roll {s.get('max_roll_deg')}", (s.get("max_roll_deg") or 0) > 20),
                (f"pitch {s.get('max_pitch_deg')}", (s.get("max_pitch_deg") or 0) > 20),
                (f"hull down {s.get('hull_down_s')} s", (s.get("hull_down_s") or 0) > 0),
                (f"cross {s.get('max_cross_m')} m", (s.get("max_cross_m") or 0) > 2.5)) if hit]
            print(f"   {car:9} {s.get('end','?'):10} {s.get('progress_m',0):7.0f}/{s.get('route_m',0):5.0f} m in {s.get('seconds',0):6.1f} s"
                  f"  vmax {s.get('max_speed_ms',0):5.1f}  cross {s.get('max_cross_m',0):5.2f}  off {s.get('off_asphalt_s',0):5.2f}"
                  f"  air {s.get('air_s',0):5.2f}/{s.get('longest_flight_s',0):4.2f}  hub {s.get('hub_low_m')}..{s.get('hub_high_m')}"
                  f"  roll {s.get('max_roll_deg',0):4.1f} pitch {s.get('max_pitch_deg',0):4.1f}"
                  f"  {'!! ' + '; '.join(bad) if bad else 'ok'}  {s.get('why','')}", flush=True)


if __name__ == "__main__":
    main()
