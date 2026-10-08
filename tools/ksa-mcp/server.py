#!/usr/bin/env python3
"""An MCP server that lets an agent drive a running KSA through the mod's bridge.

The mod reads commands from <KSA user dir>/Logs/bridge/KSACars/in and answers in .../out (Ksa/Bridge.cs):
files, so nothing needs a port and it works across the WSL boundary. This turns that folder into
tools -- load a save, park a car, drive it, step the world, photograph it -- and hands pictures back
inline, so a capture arrives in the conversation the way Blender's MCP returns a render.

Stdlib only: MCP is JSON-RPC over stdin and stdout, one message a line.

    python3 tools/ksa-mcp/server.py                          # serve MCP on stdio
    python3 tools/ksa-mcp/server.py cli status               # one tool from a shell
    python3 tools/ksa-mcp/server.py cli capture '{"label":"a","frames":4}'
"""

from __future__ import annotations

import base64
import json
import os
import shutil
import signal
import subprocess
import sys
import time
from pathlib import Path

REPO = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(REPO / "tools" / "vis"))

import vis  # noqa: E402

_user_dir: Path | None = None


def user_dir() -> Path:
    global _user_dir
    if _user_dir is None:
        out = subprocess.run([str(REPO / "tools" / "ksa-user-dir.sh")], capture_output=True, text=True)
        _user_dir = Path(out.stdout.strip())
    return _user_dir


def bridge() -> Path:
    # The mod's own subfolder, so another mod's bridge never answers a command meant for this one.
    return user_dir() / "Logs" / "bridge" / "KSACars"


def game_running() -> bool:
    try:
        out = subprocess.run(["tasklist.exe"], capture_output=True, text=True, timeout=15).stdout.lower()
    except (OSError, subprocess.TimeoutExpired):
        return False
    return "starmap.exe" in out or "kittenspaceagency" in out


class Refused(Exception):
    pass


def wsl(path: str) -> str:
    """A path the mod wrote, from Windows, as this side reads it."""
    if len(path) > 2 and path[1] == ":" and path[2] in "\\/":
        return f"/mnt/{path[0].lower()}/" + path[3:].replace("\\", "/")
    return path


def send(cmd: str, timeout: float = 30.0, **args) -> dict:
    """One command to the mod, and its reply. Raises Refused with the mod's own reason."""
    inbox, outbox = bridge() / "in", bridge() / "out"
    inbox.mkdir(parents=True, exist_ok=True)
    ident = f"{time.time_ns()}"
    body = {"id": ident, "cmd": cmd, **{k: v for k, v in args.items() if v is not None}}

    part = inbox / f"{ident}.json.part"
    part.write_text(json.dumps(body))
    part.rename(inbox / f"{ident}.json")

    reply_path = outbox / f"{ident}.json"
    deadline = time.time() + timeout
    while time.time() < deadline:
        if reply_path.exists():
            try:
                reply = json.loads(reply_path.read_text())
            except json.JSONDecodeError:
                time.sleep(0.05)
                continue
            reply_path.unlink(missing_ok=True)
            if not reply.get("ok"):
                raise Refused(reply.get("error") or "refused")
            return reply.get("data") or {}
        time.sleep(0.05)

    (inbox / f"{ident}.json").unlink(missing_ok=True)
    raise Refused(f"no reply to '{cmd}' in {timeout:.0f} s -- is the game running with this build?")


# ---- the game's lifetime -------------------------------------------------------------------

STATE = Path(__file__).resolve().parent / ".launched"


def launch(save: str | None = None) -> str:
    """Starts the game if none is running. Never a second one, and never over the player's."""
    if game_running():
        note = "a game is already running; using it"
    else:
        log = open(Path(__file__).resolve().parent / "launch.log", "w")
        proc = subprocess.Popen([str(REPO / "tools" / "run.sh")], cwd=REPO, stdout=log,
                                stderr=subprocess.STDOUT, start_new_session=True)
        STATE.write_text(str(proc.pid))
        note = "launched"

    deadline = time.time() + 300
    while True:
        try:
            send("status", timeout=5)
            break
        except Refused:
            if time.time() > deadline:
                raise Refused("the game never answered on the bridge")
            time.sleep(2)

    if save:
        data = send("load", timeout=90, save=save)
        note += f"; loaded '{save}', flying {data.get('craft')}"
    return note


def quit_game() -> str:
    """Closes a game this server launched, and refuses any other: that one is somebody's session."""
    if not STATE.exists():
        raise Refused("this server did not launch the running game; it is the player's to close")
    pid = int(STATE.read_text())
    try:
        os.killpg(pid, signal.SIGTERM)
    except ProcessLookupError:
        pass
    STATE.unlink(missing_ok=True)
    return "closed"


# ---- captures ------------------------------------------------------------------------------

def capture(label: str = "shot", frames: int = 1, every_s: float | None = None,
            every_frames: int | None = None, width: int = 960) -> list[dict]:
    """Pictures and what they show. A series comes back as a sheet, an animation and a temporal map."""
    content: list[dict] = []

    data = send("capture", timeout=60 + frames * 10, label=label, frames=frames,
                every_s=every_s, every_frames=every_frames)
    shots = data["frames"]
    imgs = [vis.load(wsl(s["file"])) for s in shots]

    if frames == 1:
        content.append(_text(f"{shots[0]['file']}\n{_brief(shots[0])}"))
        content.append(_image(imgs[0], width))
        return content

    folder = Path(wsl(data["folder"]))
    labels = [f"#{s.get('index', '?')}" for s in shots]
    sheet_path = folder / "sheet.jpg"
    vis.sheet(imgs, labels, cols=min(4, len(imgs))).save(sheet_path, quality=88)
    gif_path = vis.gif(imgs, folder / "series.gif")
    t, stats = vis.temporal(imgs)
    t.save(folder / "temporal.png")

    content.append(_text(f"{frames} frames in {folder}\nsheet {sheet_path}\nanimation {gif_path}\n"
                         f"temporal {stats} -- with the world paused, bright is renderer noise\n"
                         + "\n".join(_brief(s) for s in shots)))
    content.append(_image(vis.load(sheet_path), 1280))
    content.append(_image(t, width))
    return content


def _brief(shot: dict) -> str:
    keys = ("label", "craft", "paused", "speed", "fov_deg")
    return ", ".join(f"{k}={shot[k]}" for k in keys if k in shot)


def _text(text: str) -> dict:
    return {"type": "text", "text": text}


def _image(img, width: int) -> dict:
    return {"type": "image", "data": vis.jpeg_b64(img, width), "mimeType": "image/jpeg"}


def log_tail(pattern: str = "", lines: int = 40) -> str:
    text = (user_dir() / "Logs" / "KSACars.log").read_text(errors="replace").splitlines()
    if pattern:
        text = [t for t in text if pattern.lower() in t.lower()]
    return "\n".join(text[-lines:])


# ---- the tools -----------------------------------------------------------------------------

def _num(desc):
    return {"type": "number", "description": desc}


TOOLS = {
    "ksa_status": ("What the game is doing: scene, craft, pause, speed, and the craft near the one flown.", {}, [],
                   lambda a: [_text(json.dumps(send("status"), indent=1))]),
    "ksa_launch": ("Start KSA with this tree's deployed build if it is not running, and optionally load a "
                   "save. Uses a running game rather than starting a second.",
                   {"save": {"type": "string"}}, [], lambda a: [_text(launch(a.get("save")))]),
    "ksa_quit": ("Close the game, only if this server launched it.", {}, [], lambda a: [_text(quit_game())]),
    "ksa_load": ("Load a save by name.", {"save": {"type": "string"}}, ["save"],
                 lambda a: [_text(json.dumps(send("load", timeout=90, save=a["save"])))]),
    "ksa_pause": ("Pause the world.", {}, [], lambda a: [_text(json.dumps(send("pause")))]),
    "ksa_resume": ("Resume the world.", {}, [], lambda a: [_text(json.dumps(send("resume")))]),
    "ksa_speed": ("Set the simulation speed.", {"x": _num("speed multiple")}, ["x"],
                  lambda a: [_text(json.dumps(send("speed", x=a["x"])))]),
    "ksa_step": ("Run the world for so many simulated seconds, then pause.", {"seconds": _num("sim seconds")},
                 ["seconds"], lambda a: [_text(json.dumps(send("step", timeout=a["seconds"] * 20 + 40,
                                                               seconds=a["seconds"])))]),
    "ksa_site": ("Set a craft (the flown one unless craft names another) down at a latitude and longitude, on its "
                 "body or a named one. Over the sea it lands on the seabed. Returns the sun's "
                 "elevation there: negative is night.",
                 {"lat": _num("deg"), "lon": _num("deg"), "body": {"type": "string"}, "craft": {"type": "string"}},
                 ["lat", "lon"],
                 lambda a: [_text(json.dumps(send("site", timeout=60, **a)))]),
    "ksa_capture": ("Screenshot the game. frames>1 takes a series (every_s simulated seconds, or every_frames "
                    "rendered frames when paused) and returns a sheet, an animation path and a temporal-noise "
                    "map.",
                    {"label": {"type": "string"}, "frames": _num("count"), "every_s": _num("sim s"),
                     "every_frames": _num("frames"), "width": _num("px")}, [],
                    lambda a: capture(**a)),
    "ksa_spawn": ("Park a craft from a vehicle library (craft, e.g. Beach Buggy or Eldorado) on the ground at "
                  "lat/lon, named name.",
                  {"craft": {"type": "string"}, "name": {"type": "string"}, "lat": _num("deg"), "lon": _num("deg")},
                  ["craft", "lat", "lon"], lambda a: [_text(json.dumps(send("spawn", **a)))]),
    "ksa_ground": ("The ground's height against sea level at lat/lon (negative is seabed depth), or along a line "
                   "to to_lat/to_lon in steps. Reads the height field; places nothing.",
                   {"lat": _num("deg"), "lon": _num("deg"), "to_lat": _num("deg"), "to_lon": _num("deg"),
                    "steps": _num("count")}, ["lat", "lon"],
                   lambda a: [_text(json.dumps(send("ground", **a)))]),
    "ksa_road": ("Lay the roads of a circuit from the library (circuit), with lat/lon its first point put there and turned by heading, on whatever body the craft is on, and save_as keeping it so; or a test circuit with a junction from lat/lon "
                 "along heading (deg, 0 north), length and width in metres, kept in the library with save_as. lift is "
                 "above the ground, spacing between points, margin how far past the edge clutter is cleared "
                 "(clutter=false leaves it); clear=true takes the roads up. edit=true opens that circuit in the road "
                 "editor instead, with its free camera at view_yaw and view_pitch (deg) and view_distance (m). "
                 "probe_clutter=true lays nothing and measures the mod's clutter positions against the game's. "
                 "box_top puts one collider box (box_size square, box_thick deep) under the flown craft with its "
                 "top that far above the ground, which nothing draws; box_size=0 takes it away. mesh_test draws a "
                 "curved patch made at runtime that far above the ground under the flown craft (default 3), mesh_size "
                 "across (default 10); mesh_bend carries its far end that many metres aside and, unless mesh_cells says "
                 "otherwise, makes it of 12 cells a side instead of 32, written over the same room; mesh_size=0 stops "
                 "it being drawn. The reply has what was reserved, what KSA's mesh buffers have free and any error. "
                 "A laying's reply has the runs and meshes drawn and any left out, the vertices and indices "
                 "uploaded, the pool's places used, had and allowed, what KSA's buffers have free, the collider "
                 "meshes and their triangles, and a *_warning for whatever of that failed: a road that is not "
                 "drawn is still laid.",
                 {"circuit": {"type": "string"}, "save_as": {"type": "string"}, "lat": _num("deg"), "lon": _num("deg"),
                  "heading": _num("deg"), "length": _num("m"), "width": _num("m"), "lift": _num("m"), "spacing": _num("m"),
                  "margin": _num("m"), "clutter": {"type": "boolean"}, "clear": {"type": "boolean"},
                  "edit": {"type": "boolean"}, "probe_clutter": {"type": "boolean"}, "box_top": _num("m"),
                  "box_size": _num("m"), "box_thick": _num("m"), "mesh_test": _num("m"), "drag_node": _num("point id"), "steps": _num("layings"), "ground_cache": {"type": "boolean"},
                  "mesh_size": _num("m"), "mesh_bend": _num("m"), "mesh_cells": _num("a side"), "view_yaw": _num("deg"), "view_pitch": _num("deg"),
                  "view_distance": _num("m")}, [],
                 lambda a: [_text(json.dumps(send("road", **a)))]),
    "ksa_lap": ("A driver that follows a route round the laid circuit (lay one with ksa_road first), stepped inside the "
                "physics window; it answers at once and never waits for the lap. start=true sets it going on craft (the "
                "flown one unless named): route is the circuit's point ids to pass through in order, comma separated and "
                "ending on the first for laps ('1,2,3,5,1'), or without it the circuit's first road followed round; laps "
                "(default 1); speed is the cruise in m/s (default the car's top speed), held down for each bend; offset "
                "is metres left of the road's centre; timeout in simulated seconds (default 600); place=false leaves the "
                "car where it is instead of standing it on the road at the route's start, facing along it; rows=true "
                "keeps a line a step, with whether the hull was on anything; jump=true takes crests and dips at "
                "whatever the bends allow, where it otherwise slows to stay on its springs. status=true, or no flag, answers whether it is running, why it ended (Finished, "
                "OffRoad, Flipped, Stuck, TimedOut, Failed, Stopped, NotDriven, RoadsRelaid) and the summary so far: "
                "laps, time, cross-track error, time off the asphalt, in the air and with the hull down, hub heights. "
                "stop=true ends it. "
                "When it ends the summary is written to Logs/bridge/KSACars/laps/<utc>-<craft>.json, with the rows "
                "beside it as .csv, added to each second while it runs.",
                {"craft": {"type": "string"}, "start": {"type": "boolean"}, "status": {"type": "boolean"},
                 "stop": {"type": "boolean"}, "route": {"type": "string"}, "laps": _num("count"), "speed": _num("m/s"),
                 "offset": _num("m left"), "timeout": _num("sim seconds"), "place": {"type": "boolean"},
                 "rows": {"type": "boolean"}, "jump": {"type": "boolean"}, "racing": {"type": "boolean"}, "push": _num("0..1"), "jump_zones": {"type": "string"}, "tune": {"type": "string"}, "inside": _num("m")}, [],
                lambda a: [_text(json.dumps(send("lap", **a), indent=1))]),
    "ksa_save": ("Write the game to a save of this name, as KSA's save console command does.",
                 {"name": {"type": "string"}}, ["name"], lambda a: [_text(json.dumps(send("save", **a)))]),
    "ksa_drive": ("Drive a buggy: hold throttle (-1..1, negative brakes then reverses) and steer (-1 right..1 "
                  "left) for seconds of simulated time, and report its speed, RPM, gear, suspension travel and "
                  "hub heights. With no seconds it only reports, and the report is taken when the command arrives, "
                  "not when the hold ends. focus=true flies it and follows it first; crew=true fills its seats as "
                  "the launch window's Fill Seats does, seat_kittens names who sits where; eva=true lets the driver "
                  "(or kitten) out; flip=true tips it onto its roof and unflip=true sets it back on its wheels, as the "
                  "panel's button does; lights is off, low or high; scoop=true puts the scoop on (or 'Scoop XXL' for the larger one, false for none), and rock_weight (0.0001..1) is what a rock weighs under it as a share of KSA's; boost=true holds the tail rockets lit for seconds; downforce=true lights the rockets that press the car down; game_drag=true leaves KSA's own air drag on every car, to compare against (false, the default, keeps it off a car on its wheels); hull_margin=false leaves the physics engine's own speculative margin on a car on a road, to compare against (true, the default, holds it short); lift (0..1) lights the rockets at that throttle, 0.5 hovers, 0 cuts them; off the "
                  "ground throttle leans the nose down, steer banks left, and turn (-1..1) yaws left. cam_elevation_deg (above the car), cam_azimuth_deg and cam_distance (KSA's "
                  "orbit distance power, about 1-2 for a car) move the orbit camera on the flown craft.",
                  {"craft": {"type": "string"}, "throttle": _num("-1..1"), "steer": _num("-1..1"),
                   "seconds": _num("sim seconds"), "focus": {"type": "boolean"},
                   "crew": {"type": "boolean"}, "seat_kittens": {"type": "string"},
                   "eva": {"type": "boolean"}, "kitten": {"type": "string"},
                   "flip": {"type": "boolean"}, "unflip": {"type": "boolean"}, "lights": {"type": "string"}, "scoop": {"type": ["boolean", "string"]}, "rock_weight": _num("share"), "boost": {"type": "boolean"}, "downforce": {"type": "boolean"}, "game_drag": {"type": "boolean"}, "hull_margin": {"type": "boolean"}, "lift": _num("0..1"), "turn": _num("-1..1"),
                   "cam_elevation_deg": _num("deg"), "cam_azimuth_deg": _num("deg"), "cam_distance": _num("power"),
                   "log_every_s": _num("seconds between drive log lines")}, [],
                  lambda a: [_text(json.dumps(send("drive", **a), indent=1))]),
    "ksa_log": ("The mod's log, filtered.", {"pattern": {"type": "string"}, "lines": _num("count")}, [],
                lambda a: [_text(log_tail(a.get("pattern", ""), int(a.get("lines", 40))))]),
}


def call(name: str, args: dict) -> tuple[list[dict], bool]:
    if name not in TOOLS:
        return [_text(f"no tool {name}")], True
    try:
        return TOOLS[name][3](args or {}), False
    except Refused as e:
        return [_text(str(e))], True
    except Exception as e:  # noqa: BLE001 -- a tool that throws must still answer
        return [_text(f"{type(e).__name__}: {e}")], True


def serve() -> None:
    def reply(ident, result=None, error=None):
        msg = {"jsonrpc": "2.0", "id": ident}
        msg["result" if error is None else "error"] = result if error is None else error
        sys.stdout.write(json.dumps(msg) + "\n")
        sys.stdout.flush()

    for line in sys.stdin:
        line = line.strip()
        if not line:
            continue
        try:
            msg = json.loads(line)
        except json.JSONDecodeError:
            continue

        method, ident = msg.get("method"), msg.get("id")
        if ident is None:
            continue  # a notification

        if method == "initialize":
            version = (msg.get("params") or {}).get("protocolVersion", "2025-06-18")
            reply(ident, {"protocolVersion": version, "capabilities": {"tools": {"listChanged": False}},
                          "serverInfo": {"name": "ksa", "version": "0.1"}})
        elif method == "ping":
            reply(ident, {})
        elif method == "tools/list":
            reply(ident, {"tools": [{"name": n, "description": t[0],
                                     "inputSchema": {"type": "object", "properties": t[1], "required": t[2]}}
                                    for n, t in TOOLS.items()]})
        elif method == "tools/call":
            params = msg.get("params") or {}
            content, is_error = call(params.get("name", ""), params.get("arguments") or {})
            reply(ident, {"content": content, "isError": is_error})
        else:
            reply(ident, error={"code": -32601, "message": f"no method {method}"})


def cli(argv: list[str]) -> int:
    name = argv[0] if argv[0].startswith("ksa_") else f"ksa_{argv[0]}"
    args = json.loads(argv[1]) if len(argv) > 1 else {}
    content, is_error = call(name, args)

    # What an MCP client would be shown inline, written out so a shell can look at it too.
    shown = Path(__file__).resolve().parent / "last"
    shutil.rmtree(shown, ignore_errors=True)
    shown.mkdir()
    for k, c in enumerate(content):
        if c["type"] == "text":
            print(c["text"])
        else:
            path = shown / f"{k:02d}.jpg"
            path.write_bytes(base64.b64decode(c["data"]))
            print(f"[image] {path}")
    return 1 if is_error else 0


if __name__ == "__main__":
    if len(sys.argv) > 2 and sys.argv[1] == "cli":
        sys.exit(cli(sys.argv[2:]))
    serve()
