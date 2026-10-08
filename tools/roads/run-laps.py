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
    ap.add_argument("--circuit", action="append", help="a circuit's name; default every one of the set")
    ap.add_argument("--set", default="X", help="the first letter of the circuits to run: X the first eight, Z the harder ones")
    ap.add_argument("--jump", action="store_true", help="do not slow for crests and dips")
    ap.add_argument("--timeout", type=float, default=240.0, help="simulated seconds to give a lap")
    ap.add_argument("--warp", type=float, default=1.0, help="the game's speed while the laps run; 2 doubles the physics step")
    ap.add_argument("--gap", type=float, default=60.0, help="metres a car is given before the next sets off")
    ap.add_argument("--car", action="append", choices=list(CARS), help="default all three")
    ap.add_argument("--speed", type=float, help="cruise in m/s; default each car's top speed")
    ap.add_argument("--offset", type=float, default=0.0, help="metres left of the centre line")
    ap.add_argument("--route", help="point ids to pass through, comma separated")
    ap.add_argument("--laps", type=int, default=1)
    ap.add_argument("--best", action="store_true", help="drive as personal-best.py's best so far for this circuit and car does")
    ap.add_argument("--crew", action="store_true", help="fill each car's seats first, so it has a driver to film")
    ap.add_argument("--racing", action="store_true", help="drive the racing line and not the middle of the road")
    ap.add_argument("--push", type=float, default=0.0, help="how hard the lap is driven, 0 to 1")
    ap.add_argument("--rows", action="store_true", help="file every step of each lap as a CSV beside its summary")
    ap.add_argument("--wait", type=float, default=300.0, help="wall seconds to give a run")
    args = ap.parse_args()

    circuits = args.circuit or sorted(p.stem for p in library().glob(f"{args.set} *.json"))
    cars = args.car or list(CARS)
    present = {o["name"].replace("(flown) ", "") for o in bridge("status").get("others", [])}

    if args.warp != 1.0:
        bridge("speed", x=args.warp)

    for circuit in circuits:
        saved = json.loads((library() / f"{circuit}.json").read_text())
        # A file since version 3 is one place and metres from it, and its first point is that place.
        first = saved["at"] if "at" in saved else saved["nodes"][0]
        print(f"== {circuit}", flush=True)
        for i, car in enumerate(cars):
            craft = f"Lap {car}"
            lat, lon = first["lat_deg"] - 0.0006 - 0.0003 * list(CARS).index(car), first["lon_deg"]
            if craft not in present:
                bridge("spawn", craft=CARS[car], name=craft, lat=lat, lon=lon)
                present.add(craft)
                time.sleep(3.0)
            else:
                bridge("site", craft=craft, lat=lat, lon=lon, timeout=90)
                time.sleep(1.0)
        bridge("drive", craft=f"Lap {cars[0]}", focus=True, cam_elevation_deg=30, cam_azimuth_deg=270, cam_distance=2.6)
        if args.crew:
            for car in cars:
                if not bridge("drive", craft=f"Lap {car}").get("crew"):
                    bridge("drive", craft=f"Lap {car}", crew=True)
        laid = bridge("road", circuit=circuit)
        if not laid.get("laid"):
            print(f"   road not laid: {str(laid)[:160]}", flush=True)
            continue
        for key in ("draw_warning", "mesh_warning", "collider_warning", "hook_warning"):
            if laid.get(key):
                print(f"   {key}: {str(laid[key])[:160]}", flush=True)
        time.sleep(1.5)

        extra = {k: v for k, v in (("speed", args.speed), ("route", args.route)) if v is not None}
        best = REPO / "tools" / "roads" / "pb" / f"{circuit}-{cars[0]}.json"
        if args.best and best.exists():
            way = json.loads(best.read_text())["best"]
            paces = sorted(k for k in way if k.startswith("pace_"))
            tune = ",".join(f"{k}={v:.5g}" for k, v in way.items() if k != "inside_m" and not k.startswith(("pace_", "nudge_")))
            nudges = sorted(k for k in way if k.startswith("nudge_"))
            if nudges:
                extra["line"] = ";".join(f"{way[k]:.3f}" for k in nudges)
            if paces:
                tune += ",pace=" + ";".join(f"{way[k]:.4g}" for k in paces)
            extra.update(racing=True, inside=way["inside_m"], tune=tune)
        if args.racing:
            extra["racing"] = True
        if args.push:
            extra["push"] = args.push
        zones = json.loads((library() / f"{circuit}.json").read_text()).get("jumps") or []
        if zones:
            extra["jump_zones"] = ",".join(f"{a}-{b}" for a, b in zones)
        if args.jump:
            extra["jump"] = True

        # Every car at once, each set off when the one before is clear of the start. A car that has
        # ended is parked off the road straight away, so none finds another standing where it finishes.
        running, ended = [], {}
        deadline = time.time() + max(args.wait, args.timeout * 1.5) / args.warp + 60.0
        waiting = list(cars)
        while (waiting or running) and time.time() < deadline:
            clear = all((bridge("lap", craft=f"Lap {c}").get("progress_m") or 0) > args.gap for c in running[-1:])
            if waiting and clear:
                car = waiting.pop(0)
                started = bridge("lap", start=True, craft=f"Lap {car}", laps=args.laps, offset=args.offset, timeout=args.timeout, rows=args.rows, **extra)
                if "running" in started:
                    running.append(car)
                else:
                    ended[car] = {"end": "Refused", "why": str(started)[:160]}
            time.sleep(1.5)
            for car in list(running):
                status = bridge("lap", craft=f"Lap {car}")
                if status.get("running") is False:
                    ended[car] = status
                    running.remove(car)
                    k = list(CARS).index(car)
                    bridge("site", craft=f"Lap {car}", lat=first["lat_deg"] - 0.003 - 0.0004 * k, lon=first["lon_deg"], timeout=90)
        for car in running:
            bridge("lap", stop=True, craft=f"Lap {car}")
            ended[car] = {**bridge("lap", craft=f"Lap {car}"), "end": "RunnerGaveUp"}

        for car in cars:
            s = ended.get(car, {"end": "NotRun"})
            bad = [w for w, hit in (
                (s.get("end", "?"), s.get("end") != "Finished"),
                (f"off asphalt {s.get('off_asphalt_s')} s", (s.get("off_asphalt_s") or 0) > 0.2),
                (f"flight {s.get('longest_flight_s')} s", (s.get("longest_flight_s") or 0) > 0.5),
                (f"roll {s.get('max_roll_deg')}", (s.get("max_roll_deg") or 0) > 20),
                (f"pitch {s.get('max_pitch_deg')}", (s.get("max_pitch_deg") or 0) > 20),
                (f"cross {s.get('max_cross_m')} m", (s.get("max_cross_m") or 0) > 2.5)) if hit]
            print(f"   {car:9} {s.get('end','?'):10} {s.get('progress_m',0):7.0f}/{s.get('route_m',0):5.0f} m in {s.get('seconds',0):6.1f} s"
                  f"  last lap {s.get('last_lap_s',0):7.3f} s"
                  f"  vmax {s.get('max_speed_ms',0):5.1f}  cross {s.get('max_cross_m',0):5.2f}  off {s.get('off_asphalt_s',0):5.2f}"
                  f"  air {s.get('air_s',0):5.2f}/{s.get('longest_flight_s',0):4.2f}  hub {s.get('hub_low_m')}..{s.get('hub_high_m')}"
                  f"  roll {s.get('max_roll_deg',0):4.1f} pitch {s.get('max_pitch_deg',0):4.1f}  hull {s.get('hull_down_s',0)}"
                  f"  step {s.get('longest_step_ms',0)}  {'!! ' + '; '.join(bad) if bad else 'ok'}  {s.get('why','')}", flush=True)

    if args.warp != 1.0:
        bridge("speed", x=1.0)


if __name__ == "__main__":
    main()
