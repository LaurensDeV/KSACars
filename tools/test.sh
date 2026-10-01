#!/usr/bin/env bash
#
# Runs the headless car tests. No game required.
#
#     ./tools/test.sh                     # the whole suite
#     ./tools/test.sh Debug               # with the optimiser off, to step through one
#     ./tools/test.sh --filter BuggyDrive # anything else goes to `dotnet test`
#
# Release because it is the configuration the shipped mod compiles Sim/ in. Nothing under Sim/ is
# conditioned on DEBUG, so the two differ in speed and floating-point contraction and nothing else.
#
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
# shellcheck source=env.sh
source "$REPO_ROOT/tools/env.sh"

# Only a leading Debug/Release is a configuration; everything else is `dotnet test`'s.
CONFIG=Release
case "${1:-}" in
    Debug|Release) CONFIG="$1"; shift ;;
esac

dotnet test "$REPO_ROOT/tests/KSACars.Tests/KSACars.Tests.csproj" -c "$CONFIG" --nologo "$@"
