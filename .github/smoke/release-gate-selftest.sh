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

# A note reaches later steps only in GITHUB_ENV's delimiter form, because these values can
# span lines. Asserting on that form rather than on NAME=value is what keeps the assertion
# tied to something the workflow can actually read back.
recorded() {   # $1 = env file, $2 = name, $3 = value
  local delimiter
  delimiter=$(sed -n "s/^$2<<//p" "$1" 2>/dev/null | head -1)
  [ -n "$delimiter" ] || return 1
  # Compare what lies between the delimiters against the value, rather than checking that
  # each of its lines appears somewhere. An opener with no closer would leave Actions
  # reading the rest of the file as part of the value, and lines scattered across the file
  # in any order would satisfy a contents-only check identically.
  local block
  block=$(awk -v name="$2" -v delim="$delimiter" '
    $0 == name "<<" delim { inside = 1; next }
    inside && $0 == delim { exit }
    inside { print }
  ' "$1")
  [ "$block" = "$3" ]
}

check() {   # $1 = expected (allow|block), $2 = override, $3 = name, rest = version:verdict
  check_full "$1" "$2" "" "$3" "${@:4}"
}

check_local() {   # $1 = expected, $2 = local verdict, $3 = name, rest = version:verdict
  check_full "$1" "" "$2" "$3" "${@:4}"
}

check_full() {   # $1 = expected, $2 = override, $3 = local verdict, $4 = name, rest = pairs
  local want=$1 override=$2 local_verdict=$3 name=$4
  shift 4
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

  # The gate's two answers are exit 0 and exit 1. Anything else means it did not answer -
  # 126 for a script without the executable bit, 127 for one that is not there - and mapping
  # every non-zero status to "block" reads those as a refusal. Every case expecting a block
  # then passes while nothing runs at all, which is what happened on Linux: 29 of these
  # reported ok against a script that could not start.
  #
  # Run through `bash` for the same reason. The scripts are committed without the executable
  # bit, which Windows cannot represent and Linux enforces, so executing one directly works
  # everywhere it is developed and nowhere it is verified.
  local status=0
  OVERRIDE="$override" LOCAL_VERDICT="$local_verdict" \
    GITHUB_ENV="$dir/env" GITHUB_STEP_SUMMARY="$dir/summary" \
    bash "$here/release-gate.sh" "$dir" "$tmp/versions.json" > "$dir/out.txt" 2>&1 || status=$?

  local got
  case "$status" in
    0) got=allow ;;
    1) got=block ;;
    *)
      echo "SELFTEST FAIL: '$name' - the gate exited $status, which is neither allow nor block"
      sed 's/^/    /' "$dir/out.txt"
      failures=1
      return
      ;;
  esac

  if [ "$got" != "$want" ]; then
    echo "SELFTEST FAIL: '$name' should $want, got $got"
    sed 's/^/    /' "$dir/out.txt"
    failures=1
    return
  fi

  # Whichever way a release got past a missing Unity result has to leave a trace, or the
  # history cannot tell a verified release from an unverified one - and the two traces
  # must stay distinct, because they are different claims.
  # No `[ -s "$dir/env" ]` guard on these: the trace file being absent is precisely the
  # regression they exist to catch, and a guard on it would let that regression satisfy
  # them by having nothing to look at.
  #
  # Only when the override is what got the release through: a local result outranks it, and
  # demanding the override's trace there would insist the history record a claim nobody made.
  if [ "$want" = allow ] && [ -n "$override" ] && [ -z "$local_verdict" ]; then
    if ! recorded "$dir/env" SMOKE_OVERRIDE_NOTE "$override"; then
      echo "SELFTEST FAIL: '$name' accepted the override without recording it"
      failures=1
      return
    fi
  fi
  if [ "$want" = allow ] && [ -n "$local_verdict" ]; then
    if ! recorded "$dir/env" LOCAL_SMOKE_NOTE "$local_verdict"; then
      echo "SELFTEST FAIL: '$name' accepted the local result without recording it"
      failures=1
      return
    fi
    if grep -q "^SMOKE_OVERRIDE_NOTE<<" "$dir/env" 2>/dev/null; then
      echo "SELFTEST FAIL: '$name' recorded a local result as an override; the history would call a verified release untested"
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

# The licence is deliberately not on CI, so "no verdict anywhere" is the ordinary case and
# a stated local result is the ordinary way past it. What must not be ordinary is a claim
# vague enough to be true of a run that never happened - or specific, and a report of
# failure that nobody read.
check_local allow "2022.3=pass 6000.1=pass"  "a local result passing every version"
check_local allow "2022.3=PASS, 6000.1 = pass" "spacing, case and commas do not change the answer"
check_local allow "2022.3=pass;6000.1=pass"  "semicolons separate as documented"
check_local block "2022.3=pass"              "a local result missing a version"
check_local block "ran it, all good"         "a local result naming no version"
check_local block ""                         "an empty statement is not a statement"

# Free text can always be arranged to hold a pass inside a report of failure. Every one of
# these was accepted by some earlier version of this check; the grammar is closed now, so
# they fail for one reason rather than five - none of them is a list of <version>=pass.
check_local block "2022.3 failed, 6000.1 failed"   "naming every version as failed"
check_local block "2022.3=fail 6000.1=fail"        "stating failure in the short form"
check_local block "2022.3=pass 6000.1=fail"        "one version passing does not carry the other"
check_local block "2022.3=pass=false 6000.1=pass=false" "a token that says pass and then unsays it"
check_local block "not 2022.3=pass, not 6000.1=pass"   "a negated short form"
check_local block "2022.3=pass 6000.1=pass but the editor crashed"                                                    "prose alongside a complete claim"
check_local block "2022.3=pass 6000.1=pass extra=pass" "a version that is not supported"
check_local block "* *"                            "a glob where the tokens should be"

# The grammar's edges rather than a sample of attacks. Separators may repeat and a version
# may be stated twice - both are noise, and noise that cannot say anything is not a hole.
# What must not vary is which tokens are a token at all.
check_local allow "  2022.3=pass   6000.1=pass  "     "leading, trailing and repeated spaces"
check_local allow ",,2022.3=pass;;,6000.1=pass,,"     "repeated and mixed separators"
check_local allow "2022.3=pass 2022.3=pass 6000.1=pass" "a version stated twice adds nothing"
check_local allow "$(printf '2022.3=pass	6000.1=pass')" "a tab separates like a space"
check_local block "$(printf '2022.3=passÂ  6000.1=pass')" "a non-breaking space is not a separator"
check_local block "2022.3=pass 6000.1=passed"         "a token that merely starts with pass"
check_local block "2022.3=pass 6000.1=Pass!"          "punctuation welded to the token"
check_local block "2022.3=pass 6000.1:pass"           "the removed second spelling stays removed"
check_local block "2022.3=pass *"                     "a bare glob beside a valid token"

# The script's own output is no longer a second spelling: every version of that check left
# a field open, and an open field is somewhere to write the failure.
check_local block "PASS [Unity 2022.3] UPA0001 UPA0002 UPA0006 reported, NoTrigger.cs silent, no loader failures
PASS [Unity 6000.1] UPA0001 UPA0002 UPA0006 reported, NoTrigger.cs silent, no loader failures"                                                    "pasted pass output is not a spelling"
check_local block "PASS [Unity 2022.3] UPA9999 reported, PACKAGE_FAILED silent, no loader failures
PASS [Unity 6000.1] UPA9999 reported, PACKAGE_FAILED silent, no loader failures"                                                    "failure written into the fields a pass line leaves open"

check_local block "2022.3=pass 6000.1=pass" "a package failure outranks a local result" 2022.3:pass 6000.1:product
check_local allow "2022.3=pass 6000.1=pass" "a local result covers a partial CI run"    2022.3:pass 6000.1:infra
check_full  allow "outage" "2022.3=pass 6000.1=pass" "a local result wins over an override given at the same time"

# A version list the gate cannot read must block rather than pass over an empty loop.
printf '[]\n' > "$tmp/empty.json"
empty_status=0
OVERRIDE="" bash "$here/release-gate.sh" "$tmp" "$tmp/empty.json" > /dev/null 2>&1 || empty_status=$?
case "$empty_status" in
  1) echo "ok  an empty version list -> block" ;;
  0)
    echo "SELFTEST FAIL: an empty version list was allowed through"
    failures=1
    ;;
  *)
    echo "SELFTEST FAIL: the gate exited $empty_status on an empty version list, which is neither allow nor block"
    failures=1
    ;;
esac

exit $failures
