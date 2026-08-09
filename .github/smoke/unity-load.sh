#!/usr/bin/env bash
# Installs the assembled package into a throwaway Unity project, compiles it in batch
# mode, and asserts on the compiler diagnostics Unity produced.
#
#   usage: unity-load.sh <unity-executable> <label>
#
# Exit codes - the caller depends on the difference:
#   0  pass
#   1  product failure: the project compiled, and what came out was wrong
#   2  infrastructure failure: the project never compiled, so the product was not tested
#
# That split is the whole reason this script decides rather than the workflow. A release
# must never be waved through because a rule stopped firing, and must not be held hostage
# because a licence server was down. The two are told apart by evidence, not by judgement:
# reaching a verdict of "product failure" requires diagnostics from a real compile, so an
# infrastructure failure can never be reported as a passing product, and a product failure
# can never be relabelled as infrastructure.
#
# The pinned-compiler smoke test next door covers the same load failures in seconds. This
# one exists because it is the only check that puts the shipped package through Unity's own
# import pipeline: the .meta labels, the package layout, the ruleset lookup, and the
# compiler arguments Unity assembles are all Unity's, and none of them are exercised by
# invoking a compiler directly.
set -uo pipefail

unity=${1:?path to the Unity executable}
label=${2:?label}

here=$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)
root=$(cd "$here/../.." && pwd)
project="$here/UnityProbe"
log="$root/artifacts/smoke/unity-$label.log"

infra() { echo "INFRA [$label] $1"; exit 2; }
product() { echo "FAIL [$label] $1"; exit 1; }

# The DLLs are what a release publishes and what Unity loads; without them the project
# would compile cleanly and report nothing, which is indistinguishable from a pass.
# Not an infrastructure failure: nothing external is involved, the repository is simply
# not in a releasable state.
for dll in UnityPerformanceAnalyzers.dll UnityPerformanceAnalyzers.CodeFixes.dll; do
  if [ ! -f "$root/package/Analyzers/$dll" ]; then
    product "package/Analyzers/$dll is missing; build in Release and copy the DLLs into the package first."
  fi
done

if [ ! -x "$unity" ] && [ ! -f "$unity" ]; then
  infra "Unity executable not found at $unity"
fi

mkdir -p "$(dirname "$log")" "$project/Assets"

# Generated state from an earlier run would let a cached compilation stand in for this
# one - and a cached compilation emits no diagnostics, so every positive assertion would
# fail for a reason that has nothing to do with the analyzer.
rm -rf "$project/Library" "$project/Temp" "$project/Logs" "$project/obj" \
       "$project/UserSettings" "$project/ProjectSettings" \
       "$project/Packages/manifest.json" "$project/Packages/packages-lock.json"
rm -f "$project/Assets"/*.cs "$project/Assets"/*.cs.meta "$project/Assets"/*.ruleset \
      "$project"/*.csproj "$project"/*.sln

# The manifest is written from a template rather than committed, because Unity rewrites
# it: a project with no settings is a new project, and Unity fills a new project's
# manifest with its default package set. Those defaults are downloads this test does not
# need and does not want to depend on. Declaring the editor version is what stops that -
# the project is then an existing one, and Unity leaves the manifest alone.
cp "$project/Packages/manifest.template.json" "$project/Packages/manifest.json"
mkdir -p "$project/ProjectSettings"
printf 'm_EditorVersion: %s\n' "$label" > "$project/ProjectSettings/ProjectVersion.txt"

# One copy of the probe, shared with the pinned-compiler smoke test. Two copies would
# drift, and the drift would show up as one layer quietly testing less than the other.
cp "$here/Probe.cs" "$here/NoTrigger.cs" "$project/Assets/"
cp "$root/package/Samples~/Ruleset Presets/recommended.ruleset" "$project/Assets/Default.ruleset"

echo "== Unity $label"
echo "   editor:  $unity"
echo "   project: $project"

"$unity" -batchmode -quit -nographics -disable-assembly-updater \
  -projectPath "$project" -logFile "$log"
unity_status=$?
echo "   editor exit status: $unity_status"

if [ ! -s "$log" ]; then
  infra "Unity produced no log; it did not get far enough to compile anything."
fi

# "Did the compiler run?" is answered by evidence in the log or on disk, never by the
# editor's exit status: Unity exits non-zero for reasons that have nothing to do with the
# compile, and exits zero on some that do.
compiled=0
[ -f "$project/Library/ScriptAssemblies/Assembly-CSharp.dll" ] && compiled=1
if grep -qE "(CS8032|CS8033|AD0001|\): error CS[0-9]+:)" "$log"; then
  # A compile that failed, or an analyzer that failed to load or threw, is a product
  # verdict even though nothing was produced - it is exactly what this test looks for.
  compiled=1
fi

if [ "$compiled" -ne 1 ]; then
  echo "--- last 40 lines of $log"
  tail -40 "$log" | sed 's/^/    /'
  infra "the project never compiled (licence, image, or editor start-up), so the package was not tested."
fi

if ! "$here/assert-diagnostics.sh" "$log" "Unity $label" "$here/Probe.cs" "$here/NoTrigger.cs"; then
  echo "--- UPA diagnostics in $log"
  grep -E "\): (warning|error|info) (UPA[0-9]{4}|CS8032|CS8033|AD0001):" "$log" | sed 's/^/    /' || true
  exit 1
fi

exit 0
