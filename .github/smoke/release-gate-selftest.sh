#!/usr/bin/env bash
# Pins the release gate's decisions. The gate is the only thing standing between a
# package that failed in Unity and a permanent tag, and its two failure directions are
# both bad in ways that are easy to introduce and hard to notice: too strict and an
# outage blocks releases forever, too lax and an override waves a broken package through.
#
# Each case below states a set of verdicts, whether an override was given, and what the
# gate must decide. Runs in milliseconds; no Unity, no network.
set -euo pipefail

here=$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)
tmp=$(mktemp -d)
trap 'rm -rf "$tmp"' EXIT

printf '["2022.3", "6000.1"]\n' > "$tmp/versions.json"
failures=0
case_number=0

check() {   # $1 = expected (allow|block), $2 = override, $3 = name, rest = version:verdict
  local want=$1 override=$2 name=$3
  shift 3
  case_number=$((case_number + 1))
  local dir="$tmp/case$case_number"
  local pair version verdict
  for pair in "$@"; do
    version=${pair%%:*}
    verdict=${pair#*:}
    mkdir -p "$dir/unity-smoke-$version"
    printf '%s\n' "$verdict" > "$dir/unity-smoke-$version/verdict.txt"
  done
  mkdir -p "$dir"

  local got=allow
  OVERRIDE="$override" GITHUB_ENV="$dir/env" GITHUB_STEP_SUMMARY="$dir/summary" \
    "$here/release-gate.sh" "$dir" "$tmp/versions.json" > "$dir/out.txt" 2>&1 || got=block

  if [ "$got" != "$want" ]; then
    echo "SELFTEST FAIL: '$name' should $want, got $got"
    sed 's/^/    /' "$dir/out.txt"
    failures=1
    return
  fi

  # An accepted override has to leave a trace, or the release history cannot tell a
  # verified release from an unverified one.
  if [ "$want" = allow ] && [ -n "$override" ] && [ -s "$dir/env" ]; then
    if ! grep -q "SMOKE_OVERRIDE_NOTE=$override" "$dir/env"; then
      echo "SELFTEST FAIL: '$name' accepted the override without recording it"
      failures=1
      return
    fi
  fi
  echo "ok  $name -> $got"
}

check allow ""                "every version passed"                    2022.3:pass 6000.1:pass
check block ""                "one version failed on the package"       2022.3:pass 6000.1:product
check block "the runner died" "a package failure cannot be overridden"  2022.3:pass 6000.1:product
check block ""                "the smoke test could not run"            2022.3:pass 6000.1:infra
check allow "registry outage" "an infrastructure failure with a reason" 2022.3:pass 6000.1:infra
check block ""                "a version never reported"                2022.3:pass
check allow "the job timed out" "a missing verdict counts as infrastructure" 2022.3:pass
check block "outage"          "a package failure outranks an infrastructure one" 2022.3:infra 6000.1:product
check block ""                "no verdicts at all"

# A version list the gate cannot read must block rather than pass over an empty loop.
printf '[]\n' > "$tmp/empty.json"
if OVERRIDE="" "$here/release-gate.sh" "$tmp" "$tmp/empty.json" > /dev/null 2>&1; then
  echo "SELFTEST FAIL: an empty version list was allowed through"
  failures=1
else
  echo "ok  an empty version list -> block"
fi

exit $failures
