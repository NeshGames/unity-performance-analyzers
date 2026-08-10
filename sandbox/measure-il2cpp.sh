#!/usr/bin/env bash
# Build and run the IL2CPP measurement player, and file the report.
#
#   usage: sandbox/measure-il2cpp.sh [<unity-version> ...]
#
# With no arguments it runs every version in .github/smoke/unity-versions.json.
#
# measure.sh stops at the editor half deliberately: an IL2CPP run is a player build, minutes
# rather than seconds, and needs the build module installed. This is that half, and 2.2b makes
# it the half that decides - IL2CPP is the backend a shipped game runs.
set -uo pipefail

here=$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)
root=$(cd "$here/.." && pwd)
project=$here/UnityProject

if [ $# -gt 0 ]; then
  versions=("$@")
else
  mapfile -t versions < <(grep -oE '[0-9]+\.[0-9]+\.[0-9]+[a-z][0-9]+' "$root/.github/smoke/unity-versions.json")
fi
[ ${#versions[@]} -gt 0 ] || { echo "no editor versions to run" >&2; exit 2; }

status=0
for version in "${versions[@]}"; do
  echo
  echo "== $version"

  editor="C:/Program Files/Unity/Hub/Editor/$version/Editor/Unity.exe"
  [ -f "$editor" ] || { echo "   editor not installed: $editor"; status=2; continue; }

  bash "$here/pin-sandbox.sh" "$project" "$version"
  rm -rf "$project/Library/Bee"

  # The whole build directory, not just the report. A failed build leaves the previous
  # player sitting there, and running it produces a stale report with a fresh timestamp -
  # which is how this script once reported measurements for code that had never compiled.
  rm -rf "$project/Builds/il2cpp"

  build_log=$root/sandbox/il2cpp-build-$version.log
  echo "   building the player"
  "$editor" -batchmode -quit -nographics -projectPath "$project" \
    -executeMethod AllocationPlayerBuild.BuildIl2Cpp -logFile "$build_log"
  editor_status=$?

  player=$project/Builds/il2cpp/MeasurementPlayer.exe
  if [ ! -f "$player" ]; then
    echo "   BUILD FAILED (editor exit $editor_status); first errors:"
    grep -m 5 -E "error CS[0-9]+|BuildFailedException|build result \| Failed" "$build_log" | sed 's/^/     /'
    echo "   full log: ${build_log#"$root/"}"
    status=1
    continue
  fi

  run_log=$root/sandbox/il2cpp-run-$version.log
  echo "   running the player"
  "$player" -batchmode -nographics -logFile "$run_log"

  report=$project/Builds/il2cpp/allocation-player-report.txt
  if [ ! -s "$report" ]; then
    echo "   the player wrote no report; see ${run_log#"$root/"}"
    status=1
    continue
  fi

  # An exception inside the harness leaves a report that stops at the section before it, and
  # the sections are the contract with whoever reads the file.
  if grep -q "Exception" "$run_log"; then
    echo "   NOTE: the player log contains exceptions - the report may stop early:"
    grep -m 3 -E "Exception" "$run_log" | sed 's/^/     /'
    status=1
  fi

  dest=$project/Measurements/allocation-$version-IL2CPP-NET_Standard_2_0.txt
  cp "$report" "$dest"
  echo "   filed ${dest#"$root/"} ($(grep -c '^\[MEASURE\] ' "$dest") measured lines, \
$(grep -c '^=== ' "$dest") sections)"
done

echo
echo "project is pinned to $(sed -n 's/^m_EditorVersion: //p' "$project/ProjectSettings/ProjectVersion.txt")"
exit $status
