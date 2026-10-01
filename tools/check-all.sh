#!/usr/bin/env bash
#
# Runs every check CI runs, in the same order, so a push is not the first time you find out.
#
#     ./tools/check-all.sh              # everything
#     ./tools/check-all.sh --list       # name the checks and exit
#
# This is the script `.githooks/pre-push` runs and the one `ci.yml` calls, so the two cannot
# drift. That is the same one-script-two-callers discipline check-commit-msg.sh uses: one list,
# so "before you open a PR" and "what CI runs" cannot mean different things.
#
# Checks needing the game assemblies are skipped with a notice rather than failing: a contributor
# without KSA can still run most of this, and saying which ones were skipped is the difference
# between "it passed" and "it passed what it could".
set -uo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$REPO_ROOT" || exit 1

LIST=0
for arg in "$@"; do
    case "$arg" in
        --list)               LIST=1 ;;
        -h|--help)            sed -n '2,8p' "${BASH_SOURCE[0]}" | sed 's/^# \?//'; exit 0 ;;
        *) echo "unknown option: $arg" >&2; exit 2 ;;
    esac
done

# Every atlas is authored, and authored art is the only art nobody can rebuild from a clean
# checkout -- so this is its one gate. Globbed rather than named: a hand-listed atlas is how the
# next car ships unchecked while this script still exits 0.
AUTHORED_ATLASES=()
for glb in src/KSACars/Meshes/*.glb; do
    [[ -f "$glb" ]] && AUTHORED_ATLASES+=("$glb")
done

FAILED=()
SKIPPED=()
PASSED=0

have_assemblies() {
    [[ -n "${KSA_DLL_DIR:-}" && -f "${KSA_DLL_DIR:-}/KSA.dll" ]] && return 0
    [[ -f "$REPO_ROOT/Import/KSA.dll" ]] && return 0
    [[ -f "$REPO_ROOT/../ksa-game-assemblies/current/dll/KSA.dll" ]] && return 0
    return 1
}

run() {
    local name="$1"; shift
    if (( LIST )); then printf '  %s\n' "$name"; return 0; fi

    printf '\n\033[1m== %s\033[0m\n' "$name"
    if "$@"; then
        PASSED=$((PASSED + 1))
    else
        FAILED+=("$name")
    fi
}

skip() {
    if (( LIST )); then printf '  %s (conditional)\n' "$1"; return 0; fi
    printf '\n\033[1m== %s\033[0m\n' "$1"
    echo "  skipped: $2"
    SKIPPED+=("$1")
}

(( LIST )) && echo "checks, in order:"

run "Python tooling compiles"   python3 -m compileall -q tools

if (( LIST )) || command -v shellcheck >/dev/null 2>&1; then
    # -S warning on purpose: at info level shellcheck flags every `source tools/env.sh` as
    # unfollowable, which it is, and that would fail on nothing.
    run "Shell tooling is sane" bash -c 'shellcheck -S warning tools/*.sh tools/model/*.sh'
else
    skip "Shell tooling is sane" "shellcheck not installed"
fi

run "Sim/ is free of KSA types"     ./tools/check-boundary.sh
run "Part XML is well formed"       ./tools/check-xml.sh
run "Asset paths resolve"           ./tools/validate-parts.py --offline
run "Comment rules"                 ./tools/check-comments.sh
run "Documented facts"              ./tools/check-docs.sh
run "Changelogs fit SpaceDock"      ./tools/spacedock-changelog.py --check
run "No artefacts tracked"          ./tools/check-tracked.sh

# The proximity advisory off and the two hard checks on: a mesh authored in Blender has deliberate
# panel steps the advisory would report, while zero-UV-area triangles and exact coplanar overlaps are
# never deliberate.
if (( LIST )) || (( ${#AUTHORED_ATLASES[@]} )); then
    run "Authored meshes have no z-fighting or degenerate UVs" \
        ./tools/model/checkmesh.py "${AUTHORED_ATLASES[@]}" --near-max 0
else
    skip "Authored meshes have no z-fighting or degenerate UVs" \
         "no authored atlas in src/KSACars/Meshes/"
fi

if (( LIST )) || have_assemblies; then
    run "Build"                 ./tools/build.sh
    run "Test"                  ./tools/test.sh
    run "KSA API surface"       ./tools/api-surface.sh --check
    run "Assemblies match lock" ./tools/check-assemblies.sh
else
    skip "Build, test and API surface" "KSA assemblies not found - see tools/sync-import.sh"
fi

(( LIST )) && exit 0

echo
if (( ${#SKIPPED[@]} )); then
    echo "skipped ${#SKIPPED[@]}: ${SKIPPED[*]}"
fi

if (( ${#FAILED[@]} )); then
    printf '\033[31mFAILED %d of %d:\033[0m %s\n' \
        "${#FAILED[@]}" "$((PASSED + ${#FAILED[@]}))" "${FAILED[*]}" >&2
    exit 1
fi

printf '\033[32mall %d checks passed\033[0m\n' "$PASSED"
