#!/usr/bin/env bash
#
# Two copies of the game on this machine, for racing one against the other online.
#
#     ./tools/two-games.sh            # deploy, make or refresh both profiles, launch both
#     ./tools/two-games.sh --race     # ...then load, park an F2004 in each, lay a course, host in A and join from B
#     KSACARS_FPS_B=20 ./tools/two-games.sh --race   # ...with B held to 20 frames a second, as a slow PC is
#
# StarMap gives KSA a profile of its own with -InstancePath, as a Borea instance does, so each copy
# has its own log, saves and bridge. Then drive either through the bridge:
#
#     KSA_USER_DIR="$(./tools/two-games.sh --dir A)" python3 tools/ksa-mcp/server.py cli online '{}'
#
# Both copies share one graphics card, and at the player's settings two of them fill its memory and
# one draws a frame every few seconds; the profiles are made with the graphics turned well down, and
# vsync off: limited by the monitor, the copy in front takes the whole card and the one behind runs in
# slow motion. KSA 2026.10.19.5638 saves an fps limit and no longer reads it.
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
USER_DIR="$("$REPO_ROOT/tools/ksa-user-dir.sh")"
# Beside the player's own Windows home, which is where a path for -InstancePath has to be.
TEST_ROOT="${KSACARS_TWO_GAMES:-${USER_DIR%%/Documents/*}/KSACarsTest}"
SAVE="${KSACARS_TWO_GAMES_SAVE:-GOLF}"

win() { wslpath -w "$1"; }

if [[ "${1:-}" == "--dir" ]]; then
    echo "$TEST_ROOT/${2:?A or B}"
    exit 0
fi

STARMAP_EXE="$(find "${USER_DIR%%/Documents/*}" -maxdepth 2 -iname 'StarMap.exe' 2>/dev/null | head -1 || true)"
[[ -n "${STARMAP_DIR:-}" ]] && STARMAP_EXE="$STARMAP_DIR/StarMap.exe"
if [[ ! -f "$STARMAP_EXE" ]]; then
    echo "error: StarMap.exe not found; set STARMAP_DIR" >&2
    exit 1
fi

if tasklist.exe 2>/dev/null | grep -qi starmap; then
    echo "error: a game is running; close it first (taskkill.exe /F /IM StarMap.exe)" >&2
    exit 1
fi

"$REPO_ROOT/tools/deploy.sh" >/dev/null
echo "deployed"

for name in A B; do
    dir="$TEST_ROOT/$name"
    mkdir -p "$dir/mods" "$dir/saves" "$dir/vehicles" "$dir/Logs"
    rm -rf "$dir/mods/FastAndPurrious"
    cp -r "$USER_DIR/mods/FastAndPurrious" "$dir/mods/"
    [[ -d "$USER_DIR/saves/$SAVE" ]] && cp -r "$USER_DIR/saves/$SAVE" "$dir/saves/"
    for car in "Beach Buggy" "Eldorado" "Ferrari F2004"; do
        [[ -d "$USER_DIR/vehicles/$car" ]] && cp -r "$USER_DIR/vehicles/$car" "$dir/vehicles/"
    done
    printf '[[mods]]\nid = "Core"\nenabled = true\n\n[[mods]]\nid = "FastAndPurrious"\nenabled = true\n' > "$dir/manifest.toml"
    python3 - "$USER_DIR/settings.toml" "$dir/settings.toml" "$name" <<'EOF'
import re, sys
source, target, name = sys.argv[1:]
s = open(source).read()
light = {
    "width": "800", "height": "500", "posX": "0" if name == "A" else "820", "posY": "40", "vsync": "false", "fpslimit": "40",
    "antialiasing": '"None"', "terrainShadowMapSize": "1024", "cascadedShadowMapSize": "1024",
    "LightAtlasShadowMapSize": "1024", "cascadedShadowMapFilter": '"Pcf"', "lightSystemShadowSlots": '"Low"',
    "cloudShadows": '"Off"', "clouds": "false", "cloudUpscale": '"Low"', "oceanSimQuality": '"Low"', "ocean": "false",
    "ambientOcclusion": "false", "terrainGodrays": "false", "planetaryRings": "false", "planetaryRingMeshes": "false",
    "planetaryRingsVolumetricDust": "false", "groundClutterShadowCasting": "false", "textureStreamingBudgetMb": "1536",
    "celestialTextureCap": "2048", "vesselTextureCap": "1024", "kittenFurQuality": '"Low"', "explosionQuality": '"Low"',
    "exhaustUpscale": '"Low"', "reflections": "false", "terrainTessellation": "false", "stars": "false",
    "lensFlare": "false", "globalBloom": "false", "thresholdBloom": "false", "plumeTrails": "false",
    "plumeTrailShadows": "false", "particleCountQuality": '"Low"', "vesselDeformation": "false",
    "terrainShadows": "false", "skyboxShadows": "false", "milkyWayQuality": '"Low"', "vesselCascadeShadows": "false",
    "thinFilmInterference": "false", "specularAntiAliasing": "false", "showDistantGlints": "false",
    "crewPortraitCameras": "false", "masterVolume": "0",
}
for key, value in light.items():
    s = re.sub(rf"(?m)^{key} = .*$", f"{key} = {value}", s, count=1)
open(target, "w").write(s)
EOF
done
echo "profiles in $TEST_ROOT"

cd "$(dirname "$STARMAP_EXE")"
for name in A B; do
    ./StarMap.exe -InstancePath "$(win "$TEST_ROOT/$name")" >/dev/null 2>&1 &
    disown
    echo "launched $name"
    # One at a time: two loading at once fight over the disk and the card.
    sleep 25
done

# Windows gives the window in front the processor and throttles one behind it, and both copies race.
# shellcheck disable=SC2016  # the dollars are PowerShell's
powershell.exe -NoProfile -Command '
Add-Type -TypeDefinition @"
using System; using System.Runtime.InteropServices;
public static class Throttle {
  [StructLayout(LayoutKind.Sequential)] public struct State { public uint Version; public uint ControlMask; public uint StateMask; }
  [DllImport("kernel32.dll")] public static extern bool SetProcessInformation(IntPtr h, int kind, ref State s, int size);
  public static bool Off(IntPtr h) { var s = new State { Version = 1, ControlMask = 1, StateMask = 0 }; return SetProcessInformation(h, 4, ref s, Marshal.SizeOf(s)); }
}
"@
Get-Process StarMap | ForEach-Object { $_.PriorityClass = "High"; [void][Throttle]::Off($_.Handle) }' >/dev/null 2>&1 || true
echo "both at high priority, unthrottled"

[[ "${1:-}" == "--race" ]] || exit 0

call() {
    local name=$1
    shift
    KSA_USER_DIR="$TEST_ROOT/$name" timeout 150 python3 "$REPO_ROOT/tools/ksa-mcp/server.py" cli "$@"
}

for name in A B; do
    for _ in $(seq 1 60); do
        call "$name" status '{}' 2>/dev/null | grep -q '"build"' && break
        sleep 5
    done
    call "$name" load "{\"save\":\"$SAVE\"}"
    call "$name" spawn "{\"craft\":\"Ferrari F2004\",\"name\":\"Car$name\",\"lat\":-24.0,\"lon\":-62.0}"
    sleep 2
    call "$name" drive "{\"craft\":\"Car$name\",\"focus\":true,\"crew\":true,\"cam_elevation_deg\":20,\"cam_azimuth_deg\":160,\"cam_distance\":1.6}" >/dev/null
done
call A road '{"course":"Club Motorsports Flat"}'
call A online '{"host":true,"name":"Alpha"}' >/dev/null
sleep 4
call B online '{"join":"127.0.0.1:50000","name":"Bravo"}' >/dev/null
# KSA's own fps limit is saved and no longer read, so a slow PC is made by the bridge sleeping in B's frames.
[[ -n "${KSACARS_FPS_B:-}" ]] && call B online "{\"fps\":$KSACARS_FPS_B}" >/dev/null
sleep 15
call B online '{}'
