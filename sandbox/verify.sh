#!/usr/bin/env bash
# Compile the sandbox in a real editor and print what the analyzers reported.
#
#   usage: sandbox/verify.sh [<unity-version> ...]
#
# With no arguments it runs every version in .github/smoke/unity-versions.json, which is
# the one place the supported editors are listed.
#
# Why the project is assembled rather than committed: a Unity project carries the editor
# that owns it, and opening it in a newer one is an upgrade. The upgrade rewrites
# Packages/manifest.json with that editor's own module set, and the older editor then
# cannot resolve those packages at all -- it exits before compiling anything, with a log
# that says nothing about analyzers. Verifying both supported editors is the entire point
# of this project, so the manifest and the version stamp are written per run instead.
set -uo pipefail

here=$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)
root=$(cd "$here/.." && pwd)
project=$here/UnityProject

versions=("$@")
if [ ${#versions[@]} -eq 0 ]; then
  # grep rather than jq: this is the last check before believing a refactor changed
  # nothing, and it should not need anything installed.
  mapfile -t versions < <(grep -oE '[0-9]+\.[0-9]+\.[0-9]+[a-z][0-9]+' "$root/.github/smoke/unity-versions.json")
fi
if [ ${#versions[@]} -eq 0 ]; then
  echo "no Unity versions to run" >&2
  exit 2
fi

for version in "${versions[@]}"; do
  editor="C:/Program Files/Unity/Hub/Editor/$version/Editor/Unity.exe"
  if [ ! -f "$editor" ]; then
    echo "$version: editor not installed at $editor" >&2
    exit 2
  fi
done

# Build and install the analyzers the run is supposed to be about. Without this the script
# verifies whichever DLL happens to be sitting in package/Analyzers -- and it did: a newly
# added rule reported nothing across both editors, and the DLL Unity loaded predated it.
# A verification that does not control its own input reports on something else entirely.
echo "== building the analyzers"
dotnet build "$root/src/UnityPerformanceAnalyzers" -c Release >/dev/null || exit 2
dotnet build "$root/src/UnityPerformanceAnalyzers.CodeFixes" -c Release >/dev/null || exit 2
cp "$root/src/UnityPerformanceAnalyzers/bin/Release/netstandard2.0/UnityPerformanceAnalyzers.dll" \
   "$root/package/Analyzers/"
cp "$root/src/UnityPerformanceAnalyzers.CodeFixes/bin/Release/netstandard2.0/UnityPerformanceAnalyzers.CodeFixes.dll" \
   "$root/package/Analyzers/"
# The Traditional Chinese satellite. It has to sit beside the analyzer for the resource
# loader to find it, which is the whole reason it is installed rather than merely built.
mkdir -p "$root/package/Analyzers/zh-Hant"
cp "$root/src/UnityPerformanceAnalyzers/bin/Release/netstandard2.0/zh-Hant/UnityPerformanceAnalyzers.resources.dll" \
   "$root/package/Analyzers/zh-Hant/"

status=0
for version in "${versions[@]}"; do
  echo
  echo "== $version"
  bash "$here/pin-sandbox.sh" "$project" "$version"
  rm -rf "$project/Library/Bee"

  log=$root/sandbox/verify-$version.log
  "C:/Program Files/Unity/Hub/Editor/$version/Editor/Unity.exe" \
    -batchmode -quit -nographics -projectPath "$project" -logFile "$log"

  reported=$(grep -cE '\.cs\([0-9]+,[0-9]+\): (warning|error) UPA[0-9]{4}:' "$log")
  silent=$(grep -cE 'CS8032|CS8033|AD0001' "$log")
  compiled=0
  [ -f "$project/Library/ScriptAssemblies/Assembly-CSharp.dll" ] && compiled=1

  echo "   diagnostics: $reported"
  echo "   CS8032/CS8033/AD0001: $silent"
  echo "   Assembly-CSharp built: $compiled"

  # Three outcomes, not two. An analyzer built against a newer Roslyn than its host does
  # not fail the build: the host warns once and compiles with no analyzers at all. So
  # "reported nothing" and "never ran" produce the same green build, and only the count
  # together with the absence of CS8032 tells them apart.
  if [ "$compiled" -eq 0 ]; then
    echo "   VERDICT: infrastructure - nothing compiled, so nothing was shown either way"
    status=2
  elif [ "$silent" -ne 0 ]; then
    echo "   VERDICT: product - the analyzers did not load"
    status=1
  elif [ "$reported" -eq 0 ]; then
    echo "   VERDICT: product - it compiled and reported nothing"
    status=1
  else
    echo "   VERDICT: pass"
    # File, position, severity and id -- not the message. Unity 6 appends the help link to
    # the message and 2022.3 does not, so including it makes two runs of the same code
    # differ on every line and the dumps useless for the one thing they are for: comparing
    # editors, or comparing before and after a refactor.
    grep -oE '[^\\/]+\.cs\([0-9]+,[0-9]+\): (warning|error) UPA[0-9]{4}' "$log" \
      | sort -u > "$root/sandbox/verify-$version.txt"
    echo "   diagnostics written to sandbox/verify-$version.txt"
  fi
done

# Unity fills in stub .meta files on open. Leaving them changed makes every run look like an
# edit; the committed versions are what Unity generates anyway.
#
# Only the .meta files. Restoring the whole Assets directory also reverted Default.ruleset,
# which is generated from PresetTable and is exactly what a run of this script is meant to be
# exercising -- so a newly added rule was absent from the ruleset by the time anyone looked,
# and the run that was supposed to prove it fires reported that it did not.
git -C "$root" checkout -- 'sandbox/UnityProject/Assets/**/*.meta' 2>/dev/null || true
exit $status
