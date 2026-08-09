#!/usr/bin/env bash
# Loads the built analyzers into the exact C# compilers the supported Unity versions
# ship, compiles a probe, and asserts on what comes back. Seconds, no Unity install.
#
#   usage: analyzer-load.sh
#   env:   UPA_ANALYZER_DIR   directory holding the two analyzer DLLs
#                             (default: the Release output under src/)
#
# Why a pinned compiler rather than the SDK's: an analyzer built against a newer Roslyn
# than the host cannot be created, and the host's response is to warn once and carry on
# compiling with no analyzers at all. The build stays green, the package ships, and the
# rules silently never run. The SDK compiler is newer than both Unity compilers, so a
# regression of that kind is invisible to every other check in this repository.
set -euo pipefail

here=$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)
root=$(cd "$here/../.." && pwd)
work="$root/artifacts/smoke"
packages="$work/packages"

analyzer_dir=${UPA_ANALYZER_DIR:-}
analyzer="${analyzer_dir:-$root/src/UnityPerformanceAnalyzers/bin/Release/netstandard2.0}/UnityPerformanceAnalyzers.dll"
codefixes="${analyzer_dir:-$root/src/UnityPerformanceAnalyzers.CodeFixes/bin/Release/netstandard2.0}/UnityPerformanceAnalyzers.CodeFixes.dll"
stubs="$root/src/UnityStubs/bin/Release/netstandard2.0/UnityStubs.dll"
ruleset="$root/package/Samples~/Ruleset Presets/recommended.ruleset"

# Both DLLs, not just the analyzer one: the package labels both for the compiler, so
# both are loaded on every compile in every consuming project, and a code fix assembly
# that fails to load is reported exactly like an analyzer that fails to load.
for f in "$analyzer" "$codefixes" "$stubs" "$ruleset"; do
  if [ ! -f "$f" ]; then
    echo "missing: $f" >&2
    echo "build the solution in Release first (dotnet build -c Release)." >&2
    exit 1
  fi
done

rm -rf "$work"
mkdir -p "$work"

# ---------------------------------------------------------------------------
# The import settings Unity reads.
#
# Unity hands a DLL to the compiler only when its .meta carries the RoslynAnalyzer
# label. Nothing else - not the folder, not the file name - has any effect. If Unity
# ever regenerates one of these .meta files the label is gone, the analyzer stops
# reaching the compiler, and there is no error anywhere: the rules just stop.
# ---------------------------------------------------------------------------
echo "== import settings"
meta_failed=0
shopt -s nullglob
metas=("$root/package/Analyzers"/*.dll.meta)
if [ ${#metas[@]} -eq 0 ]; then
  echo "FAIL no .dll.meta files under package/Analyzers"
  meta_failed=1
fi
for meta in "${metas[@]}"; do
  if ! grep -qE '^labels:' "$meta" || ! grep -qE '^- RoslynAnalyzer$' "$meta"; then
    echo "FAIL $(basename "$meta") has no RoslynAnalyzer label; Unity would not pass the DLL to the compiler."
    meta_failed=1
  else
    echo "ok   $(basename "$meta") carries the RoslynAnalyzer label"
  fi
done
# And the other direction: a DLL added to the folder without a .meta beside it is
# invisible to Unity in the same silent way.
for dll in "$root/package/Analyzers"/*.dll; do
  if [ ! -f "$dll.meta" ]; then
    echo "FAIL $(basename "$dll") has no .meta beside it."
    meta_failed=1
  fi
done
shopt -u nullglob
[ "$meta_failed" -eq 0 ] || exit 1

# ---------------------------------------------------------------------------
# The compilers.
# ---------------------------------------------------------------------------
echo
echo "== fetching the pinned compilers"
dotnet restore "$here/toolsets.proj" --packages "$packages" --verbosity quiet

refs=()
for f in "$packages"/netstandard.library.ref/*/ref/netstandard2.1/*.dll; do
  refs+=("-r:$f")
done
if [ ${#refs[@]} -eq 0 ]; then
  echo "no reference assemblies were restored" >&2
  exit 1
fi

# The package layout moved between the two versions (tasks/net6.0 vs tasks/netcore),
# so find the compiler rather than assuming where it sits.
compilers=$(find "$packages/microsoft.net.compilers.toolset" -path '*/bincore/csc.dll' | sort)
if [ -z "$compilers" ]; then
  echo "no csc.dll was restored under $packages" >&2
  exit 1
fi

compile() {   # $1 = csc.dll, $2 = log, remaining = extra csc arguments
  local csc=$1 log=$2
  shift 2
  # Roll forward: the 2022.3 compiler targets .NET 6, which CI does not install.
  DOTNET_ROLL_FORWARD=LatestMajor dotnet exec "$csc" \
    -nologo -nostdlib+ -preferreduilang:en-US \
    -nowarn:1701 \
    -target:library -out:"$work/probe.dll" \
    "${refs[@]}" -r:"$stubs" \
    -ruleset:"$ruleset" \
    "$@" \
    "$here/Probe.cs" "$here/NoTrigger.cs" > "$log" 2>&1 || true
}

status=0
for csc in $compilers; do
  version=$(echo "$csc" | sed -E 's|.*/microsoft.net.compilers.toolset/([^/]+)/.*|\1|')
  echo
  echo "== Roslyn $version"
  log="$work/roslyn-$version.log"
  compile "$csc" "$log" -analyzer:"$analyzer" -analyzer:"$codefixes"
  cat "$log"
  bash "$here/assert-diagnostics.sh" "$log" "Roslyn $version" "$here/Probe.cs" "$here/NoTrigger.cs" || status=1

  # Control run. The assertions above pass only when the analyzer is loaded, and this
  # is what proves it: the same probe, the same ruleset, no -analyzer arguments, and
  # the rule IDs must disappear. Without this, an assertion that matched on something
  # other than a live diagnostic would pass forever and mean nothing.
  control="$work/roslyn-$version-control.log"
  compile "$csc" "$control"
  if grep -qE '\): (warning|error|info) UPA[0-9]{4}:' "$control"; then
    echo "FAIL [Roslyn $version] rule IDs were reported with no analyzer loaded; the assertions are not measuring the analyzer."
    status=1
  else
    echo "PASS [Roslyn $version] control run with no analyzer reports no rules"
  fi
done

exit $status
