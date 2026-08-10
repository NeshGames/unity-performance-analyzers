#!/usr/bin/env bash
# Run the rules over real open-source Unity code and snapshot what they report.
#
#   usage: sandbox/corpus.sh [--unity-dll-dir <dir>] [--check] [<repo key> ...]
#
# --check writes nothing and exits 1 if what the rules report today differs from the
# committed snapshots. That is the regression test: a change that adds four hundred
# findings to real code shows up as a diff here and nowhere else in this repository.
#
# Unit-test fixtures answer "does the rule fire where I wrote it to". They cannot answer
# "how much does this package say on code nobody wrote for it", and that second question is
# the one that decides whether anyone leaves the rules switched on.
#
# What this produces is a snapshot per project - per-rule counts, plus the numbers that say
# how much to trust them - committed so a diff shows what a change did to real code. A
# change that adds four hundred findings across the corpus is visible here and nowhere else.
#
# Sources are cloned at analysis time and pinned to a commit. Nothing from them is committed
# to this repository: the snapshots hold counts, not code.
set -uo pipefail

here=$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)
root=$(cd "$here/.." && pwd)
corpus=$root/sandbox/corpus
snapshots=$root/sandbox/corpus-snapshots
unity_dll_dir=""
check=no

while [ $# -gt 0 ]; do
  case "$1" in
    --unity-dll-dir) unity_dll_dir=$2; shift 2 ;;
    --check) check=yes; shift ;;
    *) break ;;
  esac
done

# key|url|commit|licence|kind|source globs
#
# Pinned to a commit because an unpinned corpus makes every snapshot diff ambiguous: a
# change in the counts could be ours or theirs. Licence is recorded per entry because this
# reads other people's code, and permissive is the only kind here.
entries=(
  "kino-bloom|https://github.com/keijiro/KinoBloom.git|effects|Assets"
  "skinner|https://github.com/keijiro/Skinner.git|effects|Assets/Skinner"
  "klak|https://github.com/keijiro/Klak.git|library|Assets/Klak"
  "pcx|https://github.com/keijiro/Pcx.git|library|Packages/jp.keijiro.pcx"
  "uieffect|https://github.com/mob-sakai/UIEffect.git|library|Packages/src"
  "zstring|https://github.com/Cysharp/ZString.git|library|src/ZString.Unity/Assets/Scripts/ZString"
  "unitask|https://github.com/Cysharp/UniTask.git|library|src/UniTask/Assets/Plugins/UniTask/Runtime"
)

mkdir -p "$corpus" "$snapshots"
status=0

echo "== building upa-cli"
dotnet build "$root/src/UnityPerformanceAnalyzers.Cli" -c Release >/dev/null || exit 2
cli=$root/src/UnityPerformanceAnalyzers.Cli/bin/Release/net8.0/upa-cli.dll

wanted=("$@")
for entry in "${entries[@]}"; do
  IFS='|' read -r key url kind globs <<< "$entry"

  if [ ${#wanted[@]} -gt 0 ] && ! printf '%s\n' "${wanted[@]}" | grep -qx "$key"; then
    continue
  fi

  echo
  echo "== $key ($kind)"
  dir=$corpus/$key
  if [ ! -d "$dir/.git" ]; then
    git clone --depth 1 "$url" "$dir" >/dev/null 2>&1 || { echo "   clone failed" >&2; continue; }
  fi
  commit=$(git -C "$dir" rev-parse HEAD)

  # Read from the project rather than asserted here. A licence I typed from memory is a
  # claim about someone else's terms with nothing behind it; the file is the terms.
  licence=$(cat "$dir"/LICENSE* "$dir"/COPYING* 2>/dev/null \
    | grep -i -oE "MIT License|Permission is hereby granted|Unlicense|Apache License|BSD|GNU GENERAL PUBLIC|Mozilla Public" \
    | head -1)
  case "$licence" in "Permission is hereby granted"*|"permission is hereby granted"*) licence="MIT License" ;; esac
  licence=${licence:-UNKNOWN}
  case "$licence" in
    *GNU*|*Mozilla*) licence="COPYLEFT: $licence" ;;
  esac
  if [ "$licence" = UNKNOWN ] || [[ "$licence" == COPYLEFT* ]]; then
    echo "   licence is $licence - skipping. This reads other people's code, and only" >&2
    echo "   permissive terms that were actually read count as checked." >&2
    continue
  fi
  echo "   licence: $licence  commit: ${commit:0:9}"

  files=()
  for glob in $globs; do
    while IFS= read -r file; do files+=("$file"); done < <(find "$dir/$glob" -name '*.cs' 2>/dev/null)
  done
  if [ ${#files[@]} -eq 0 ]; then
    echo "   no sources under: $globs" >&2
    continue
  fi

  args=("${files[@]}" --all-warn --fail-on none --format json)
  [ -n "$unity_dll_dir" ] && args+=(--unity-dll-dir "$unity_dll_dir")

  # No --whole-assembly: these are not complete compilations here - package references are
  # missing - and claiming otherwise would turn every unresolved type into a fatal error
  # rather than into the count below, which is the thing worth knowing.
  out=$snapshots/$key.json
  [ "$check" = yes ] && out=$(mktemp)
  dotnet "$cli" "${args[@]}" > "$out.raw" 2>/dev/null

  python - "$out.raw" "$out" "$key" "$licence" "$kind" "$commit" "${#files[@]}" <<'PY'
import collections, io, json, sys

raw, target, key, licence, kind, commit, file_count = sys.argv[1:8]
data = json.load(io.open(raw, encoding="utf-8"))

counts = collections.Counter(d["id"] for d in data["diagnostics"])
summary = data["summary"]

snapshot = {
    "project": key,
    "licence": licence,
    "kind": kind,
    "commit": commit,
    "files": int(file_count),
    # Both of these decide how much the counts are worth. Unresolved types keep rules from
    # firing at all, so a snapshot with compile errors under-reports - and a snapshot that
    # does not say so reads exactly like a clean one.
    "compileErrors": summary["compileErrorCount"],
    "analyzerFailures": summary["analyzerFailureCount"],
    "total": len(data["diagnostics"]),
    "byRule": dict(sorted(counts.items())),
}
io.open(target, "w", encoding="utf-8", newline="\n").write(
    json.dumps(snapshot, indent=2, ensure_ascii=False) + "\n")

print("   files: %s   findings: %d   compile errors: %d   analyzer failures: %d"
      % (file_count, snapshot["total"], snapshot["compileErrors"], snapshot["analyzerFailures"]))
top = counts.most_common(5)
if top:
    print("   most reported: " + ", ".join("%s x%d" % (r, n) for r, n in top))
PY
  rm -f "$out.raw"

  if [ "$check" = yes ]; then
    committed=$snapshots/$key.json
    if [ ! -f "$committed" ]; then
      echo "   NO SNAPSHOT COMMITTED for $key" >&2
      status=1
    elif ! diff -u "$committed" "$out" > "$out.diff"; then
      echo "   CHANGED against the committed snapshot:" >&2
      sed -n '1,40p' "$out.diff" | sed 's/^/     /' >&2
      status=1
    else
      echo "   unchanged"
    fi
    rm -f "$out" "$out.diff"
  fi
done

echo
if [ "$check" = yes ]; then
  [ "$status" -eq 0 ] && echo "corpus unchanged" || echo "corpus CHANGED - review the diff above"
  exit "$status"
fi
echo "snapshots in $snapshots"
