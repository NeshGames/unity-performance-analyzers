#!/usr/bin/env bash
# Run the rules over real open-source Unity code and snapshot what they report.
#
#   usage: sandbox/corpus.sh [--unity-dll-dir <dir>] [--all-warn] [--check] [<repo key> ...]
#
# --check writes nothing and exits 1 if what the rules report today differs from the
# committed snapshots. That is the regression test: a change that adds four hundred
# findings to real code shows up as a diff here and nowhere else in this repository.
#
# Runs through the `recommended` preset, because the question this corpus exists to answer is
# what a user sees - and a user has a preset. Forcing every rule to warning measured something
# nobody is configured for: it turned on the rules that ship disabled. --all-warn restores that
# for surveying what the disabled rules would say; the snapshot records which mode produced it,
# because two configurations writing to one file is how a snapshot diff becomes unreadable.
#
# The preset is copied to the corpus root before the run rather than passed where it lives.
# .editorconfig sections are matched relative to the directory holding the file, so a config
# outside the tree being analysed matches nothing at all - and matches it silently, which is
# the failure that looks exactly like success. Measured: with the config left in package/,
# every section was inert and the whole drop in findings came from dropping --all-warn.
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
all_warn=no
preset="$root/package/Samples~/Ruleset Presets/recommended.editorconfig"

while [ $# -gt 0 ]; do
  case "$1" in
    --unity-dll-dir) unity_dll_dir=$2; shift 2 ;;
    --check) check=yes; shift ;;
    --all-warn) all_warn=yes; shift ;;
    *) break ;;
  esac
done

mode=preset-recommended
[ "$all_warn" = yes ] && mode=all-warn
active_config=$corpus/.editorconfig
if [ "$all_warn" = no ]; then
  [ -f "$preset" ] || { echo "preset not found: $preset" >&2; exit 2; }
fi

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

  # Games, added because the seven above could not answer the question this corpus exists
  # for. Six of them are libraries or effect packages, and a false-positive rate on library
  # code says nothing about gameplay code -- which is where the noise that makes a team
  # switch the rules off actually comes from. Licences were read from each repository before
  # anything was cloned: MIT, MIT, and an unmodified Apache 2.0.
  "san-andreas|https://github.com/in0finite/SanAndreasUnity.git|game|Assets/Scripts"
  "diablerie|https://github.com/mofr/Diablerie.git|game|Assets/Scripts/Diablerie"
  "chop-chop|https://github.com/UnityTechnologies/open-project-1.git|game|UOP1_Project/Assets/Scripts"
)

mkdir -p "$corpus" "$snapshots"
status=0

rm -f "$active_config"
[ "$all_warn" = no ] && cp "$preset" "$active_config"
trap 'rm -f "$active_config"' EXIT

echo "== building upa-cli"
dotnet build "$root/src/UnityPerformanceAnalyzers.Cli" -c Release >/dev/null || exit 2
cli=$root/src/UnityPerformanceAnalyzers.Cli/bin/Release/net8.0/upa-cli.dll

wanted=("$@")

# Option parsing above stops at the first positional argument, so anything option-shaped
# after a repo key was silently taken as a repo key. `--check chop-chop --unity-dll-dir <dir>`
# therefore ran against the bundled stubs instead of a real Unity: 3968 compile errors rather
# than 516, different counts, and a regression gate reporting a change that was never made.
for w in "${wanted[@]}"; do
  case "$w" in
    -*) echo "'$w' came after a repo key, so it was never parsed as an option." >&2
        echo "Options go first: corpus.sh [--unity-dll-dir <dir>] [--check] [<key> ...]" >&2
        exit 2 ;;
  esac
  printf '%s\n' "${entries[@]}" | cut -d'|' -f1 | grep -qx "$w" || {
    echo "no corpus entry named '$w'. Known: $(printf '%s\n' "${entries[@]}" | cut -d'|' -f1 | tr '\n' ' ')" >&2
    exit 2
  }
done
for entry in "${entries[@]}"; do
  IFS='|' read -r key url kind globs <<< "$entry"

  if [ ${#wanted[@]} -gt 0 ] && ! printf '%s\n' "${wanted[@]}" | grep -qx "$key"; then
    continue
  fi

  echo
  echo "== $key ($kind)"
  dir=$corpus/$key
  if [ ! -d "$dir/.git" ]; then
    # Partial and sparse, not just shallow. One of these projects is a 4.7 GB repository
    # whose C# is a few megabytes of it -- the rest is art nobody here will read. Blobs are
    # fetched on demand and only the analysed directories are checked out. Cone mode keeps
    # the root files, which is where the licence is read from a few lines below, so this
    # cannot quietly turn a checked licence into UNKNOWN.
    git clone --depth 1 --filter=blob:none --sparse "$url" "$dir" >/dev/null 2>&1 \
      || { echo "   clone failed" >&2; continue; }
    git -C "$dir" sparse-checkout set $globs >/dev/null 2>&1 \
      || { echo "   sparse-checkout failed for: $globs" >&2; continue; }
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

  # Through a response file, not the command line. Windows caps a command line at 32767
  # characters, and a project of three hundred files with absolute paths goes past it -- at
  # which point the runner is never invoked, nothing is written, and the only symptom was a
  # JSON parse error further down. Two of the first three game projects added here failed
  # exactly that way while the script still exited 0.
  # Paths inside a response file are read literally. On the command line Git Bash rewrites
  # /d/... into D:\... on the way to a Windows program; in a file nothing does, so the
  # runner is handed paths Windows cannot resolve and produces nothing. cygpath is used
  # where it exists and skipped where it does not, which is every other platform.
  rsp=$corpus/$key.rsp
  if command -v cygpath >/dev/null 2>&1; then
    printf '%s\n' "${files[@]}" | cygpath -w -f - > "$rsp"
    rsp_arg=$(cygpath -w "$rsp")
  else
    printf '%s\n' "${files[@]}" > "$rsp"
    rsp_arg=$rsp
  fi
  printf '%s\n' --fail-on none --format json >> "$rsp"
  if [ "$all_warn" = yes ]; then
    printf '%s\n' --all-warn >> "$rsp"
  else
    # Paths inside a response file are read literally, same as the source list above.
    if command -v cygpath >/dev/null 2>&1; then
      printf '%s\n' --editorconfig "$(cygpath -w "$active_config")" >> "$rsp"
    else
      printf '%s\n' --editorconfig "$active_config" >> "$rsp"
    fi
  fi
  [ -n "$unity_dll_dir" ] && printf '%s\n' --unity-dll-dir "$unity_dll_dir" >> "$rsp"

  # No --whole-assembly: these are not complete compilations here - package references are
  # missing - and claiming otherwise would turn every unresolved type into a fatal error
  # rather than into the count below, which is the thing worth knowing.
  out=$snapshots/$key.json
  [ "$check" = yes ] && out=$(mktemp)
  dotnet "$cli" "@$rsp_arg" > "$out.raw" 2>/dev/null
  rm -f "$rsp"

  # A run that produced nothing must not read as a project with nothing to report. Without
  # this the traceback below scrolled past and the script exited 0 with two projects missing.
  if [ ! -s "$out.raw" ]; then
    echo "   the runner produced no output - snapshot not written" >&2
    status=1
    continue
  fi

  python - "$out.raw" "$out" "$key" "$licence" "$kind" "$commit" "${#files[@]}" "$mode" <<'PY'
import collections, io, json, sys

raw, target, key, licence, kind, commit, file_count, mode = sys.argv[1:9]
data = json.load(io.open(raw, encoding="utf-8"))

counts = collections.Counter(d["id"] for d in data["diagnostics"])
severities = collections.Counter(d["severity"] for d in data["diagnostics"])
summary = data["summary"]

snapshot = {
    "project": key,
    "licence": licence,
    "kind": kind,
    "commit": commit,
    # Which configuration produced these counts. Without it two modes write to one file and
    # the diff reads as a regression when it is only a different question being asked.
    "mode": mode,
    "files": int(file_count),
    # Both of these decide how much the counts are worth. Unresolved types keep rules from
    # firing at all, so a snapshot with compile errors under-reports - and a snapshot that
    # does not say so reads exactly like a clean one.
    "compileErrors": summary["compileErrorCount"],
    "analyzerFailures": summary["analyzerFailureCount"],
    "total": len(data["diagnostics"]),
    "bySeverity": dict(sorted(severities.items())),
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
    elif ! grep -q "\"mode\": \"$mode\"" "$committed"; then
      # A mode mismatch is a configuration difference, not a regression. Left to the diff it
      # would show every count changing and read as though the rules had gone wrong.
      echo "   snapshot was taken in a different mode - rerun without --all-warn, or with it" >&2
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
