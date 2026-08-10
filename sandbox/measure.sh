#!/usr/bin/env bash
# Run the allocation measurements in a real editor, on every supported version, and keep
# the reports.
#
#   usage: sandbox/measure.sh [<unity-version> ...]
#
# With no arguments it runs every version in .github/smoke/unity-versions.json, which is
# the one place the supported editors are listed.
#
# This exists because the measurements are the one thing here that has to be run against
# more than one editor to mean anything, and running two editors against one project
# upgrades it out from under the older one. verify.sh has always pinned the project per
# run; the measurement path did not, so invoking the editor directly with -executeMethod
# left the project resolvable by exactly one version - and the next run reported that the
# editor was broken.
#
# IL2CPP is not covered here: it needs a player build, which is minutes rather than
# seconds and needs the build module installed. Use AllocationPlayerBuild.BuildIl2Cpp for
# that, after this script has pinned the project to the version you want it built with.
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

  bash "$here/pin-sandbox.sh" "$project" "$version"
  rm -rf "$project/Library/Bee"

  log=$root/sandbox/measure-$version.log
  editor="C:/Program Files/Unity/Hub/Editor/$version/Editor/Unity.exe"
  [ -x "$editor" ] || [ -f "$editor" ] || { echo "   editor not installed: $editor"; status=1; continue; }

  "$editor" -batchmode -quit -nographics -projectPath "$project" \
    -executeMethod AllocationProbe.Run -logFile "$log"
  editor_status=$?

  # The report is the product, not the exit code: the editor exits non-zero for reasons
  # that have nothing to do with the measurement, and exits zero on some that do.
  report=$(ls -t "$project/Measurements"/allocation-"$version"-*.txt 2>/dev/null | head -1)
  if [ -z "$report" ]; then
    echo "   no report written (editor exit $editor_status); see $log"
    status=1
    continue
  fi

  echo "   report: ${report#"$root/"}"
  grep -cE '^\[MEASURE\] ' "$report" | xargs -I{} echo "   {} measured lines"
done

# Left pinned to whichever version ran last, which is a state no reader should have to
# guess at.
echo
echo "project is pinned to $(sed -n 's/^m_EditorVersion: //p' "$project/ProjectSettings/ProjectVersion.txt")"
exit $status
