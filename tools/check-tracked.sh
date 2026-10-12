#!/usr/bin/env bash
#
# Refuses to let build artefacts or the game's assemblies live in the repository.
#
#     ./tools/check-tracked.sh
#
# Two separate hazards. A committed .dll or .pdb would be republishing RocketWerkz's copyrighted
# files, which is the one mistake here with consequences outside this project. And .gitignore does
# not untrack what is already tracked, so a file committed ahead of its ignore rule stays in the
# repository indefinitely and silently.
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$REPO_ROOT" || exit 1

fail=0

# The one binary here that is not KSA's and is meant to ship: SDL's own Windows build, under the zlib
# licence beside it. It is let through by its digest, so nothing else can ride in under its name.
SDL=src/KSACars/Native/SDL3.dll
SDL_SHA256=2b4b128d8d510cc2d442693cf848c050d700d43dc667afe9e5fd82b0570d667e
if [[ -f "$SDL" ]] && [[ "$(sha256sum "$SDL" | cut -d' ' -f1)" != "$SDL_SHA256" ]]; then
    echo "error: $SDL is not the SDL build this repository ships (release 3.4.18, win32-x64)" >&2
    fail=1
fi

if hits="$(git ls-files | grep -E '\.(dll|pdb)$' | grep -vxF "$SDL")"; then
    echo "$hits" | sed 's/^/  /'
    echo "error: game assemblies must not be committed - see .gitignore" >&2
    fail=1
fi

if hits="$(git ls-files | grep -E '(^|/)__pycache__/|\.pyc$')"; then
    echo "$hits" | sed 's/^/  /'
    echo "error: build artefacts are tracked; git rm --cached them" >&2
    fail=1
fi

if (( fail )); then
    exit 1
fi

echo "tracked files ok: no assemblies or build artefacts"
