#!/usr/bin/env bash
# Every car round every circuit file, in the headless rig: the laps tools/roads/run-laps.py takes
# minutes over in the game, in seconds, on flat ground and without the game's physics engine.
#
#   ./tools/roads/rig-laps.sh             every circuit in the library
#   ./tools/roads/rig-laps.sh "Z "        the ones whose name starts so
#   ./tools/roads/rig-laps.sh "Z Sky" F2004   and every step of that car's lap, as a CSV beside the table
#
# KSACARS_CIRCUITS names another folder of circuits; the table and the CSVs go to KSACARS_LAPS_DIR, or a
# temporary folder that is printed.
set -euo pipefail
cd "$(dirname "$0")/../.."

circuits="${KSACARS_CIRCUITS:-$(./tools/ksa-user-dir.sh)/KSACars/Circuits}"
out="${KSACARS_LAPS_DIR:-$(mktemp -d)}"
mkdir -p "$out"

KSACARS_CIRCUITS="$circuits" KSACARS_ONLY="${1:-}" KSACARS_TRACE="${2:-}" KSACARS_LAPS_OUT="$out/laps.txt" \
    ./tools/test.sh --filter CircuitFileLapTests > "$out/test.log" 2>&1 || { tail -30 "$out/test.log"; exit 1; }
cat "$out/laps.txt"
[ -z "${2:-}" ] || echo "steps: $out"
