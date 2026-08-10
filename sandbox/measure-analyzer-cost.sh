#!/usr/bin/env bash
# What the analyzers cost the compile they run in.
#
#   usage: sandbox/measure-analyzer-cost.sh [<unity-version> ...]
#
# 46 rules run on every Unity compile and on every keystroke in an IDE, and Unity
# developers are acutely sensitive to compile time. This produces the number, from the
# compiler that actually runs them.
#
# The instrument is csc's own -reportanalyzer, added to the sandbox's csc.rsp for the
# duration of the run. Not a stopwatch around anything of ours: the question is what the
# compiler attributes to each analyzer, and only the compiler can answer that.
#
# Two limits of this instrument, both read out of its output rather than assumed:
#
#   * Unity compiles assemblies in parallel and they interleave in one log, so a table
#     cannot be attributed to the assembly that produced it. Only totals are reported.
#   * Per-analyzer times sum to more than the reported total, because analyzers run
#     concurrently: the per-analyzer figure is CPU time and the total is closer to wall
#     clock. So the comparison worth making is between analyzers in the same compile,
#     not against a stopwatch.
set -uo pipefail

here=$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)
root=$(cd "$here/.." && pwd)
project=$here/UnityProject
rsp=$project/Assets/csc.rsp

versions=("$@")
if [ ${#versions[@]} -eq 0 ]; then
  mapfile -t versions < <(grep -oE '[0-9]+\.[0-9]+\.[0-9]+[a-z][0-9]+' "$root/.github/smoke/unity-versions.json")
fi

# The rsp is a tracked file carrying the project's scripting defines. It is appended to and
# put back, never rewritten: overwriting it once already cost a run, because the run then
# measured a project with no UPA_TARGET_WEBGL and five rules quietly stopped reporting.
backup=$(mktemp)
cp "$rsp" "$backup"
restore() { cp "$backup" "$rsp"; rm -f "$backup"; }
trap restore EXIT

printf -- "-reportanalyzer\n" >> "$rsp"

mkdir -p "$project/Measurements"

for version in "${versions[@]}"; do
  echo
  echo "== $version"
  bash "$here/verify.sh" "$version" >/dev/null 2>&1
  log=$root/sandbox/verify-$version.log
  [ -f "$log" ] || { echo "   no log at $log" >&2; exit 2; }

  out=$project/Measurements/analyzer-cost-$version.txt
  python - "$log" "$version" <<'PY' | tee "$out"
import collections, io, re, sys

log = io.open(sys.argv[1], encoding="utf-8", errors="replace").read()
totals = [float(x) for x in re.findall(r"Total analyzer execution time: ([\d.]+) seconds", log)]

per = collections.defaultdict(float)
vendor = collections.defaultdict(float)
for line in log.splitlines():
    match = re.match(r"\s*(<?[\d.]+)\s+(<?\d+)\s+([A-Za-z0-9_.]+)\s*(\(|$)", line)
    if not match:
        continue
    name = match.group(3)
    if "." not in name:
        continue
    # "<0.001" is the compiler saying "below the resolution I print", not zero.
    value = 0.0005 if match.group(1).startswith("<") else float(match.group(1))
    vendor[name.split(".")[0]] += value
    if name.startswith("UnityPerformanceAnalyzers."):
        per[name.split(".")[-1]] += value

ours = sum(per.values())
everything = sum(vendor.values())
share = (100 * ours / everything) if everything else 0

print("Unity %s, sandbox project, %d assembly compiles" % (sys.argv[2], len(totals)))
print("  compiler-reported analyzer time, summed: %.3f s" % sum(totals))
print("  per-analyzer CPU time, all vendors:      %.3f s" % everything)
print("  of which ours (%d analyzers):            %.3f s  (%.0f%%)" % (len(per), ours, share))
print()
print("  by vendor:")
for name, value in sorted(vendor.items(), key=lambda kv: -kv[1]):
    print("    %7.3f s  %s" % (value, name))
print()
print("  our most expensive rules:")
for name, value in sorted(per.items(), key=lambda kv: -kv[1])[:8]:
    print("    %7.3f s  %s" % (value, name))
ordered = sorted(per.values())
if ordered:
    print("    median rule: %.4f s" % ordered[len(ordered) // 2])
PY
done
