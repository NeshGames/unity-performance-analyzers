#!/usr/bin/env bash
# Applies the smoke assertions to one compiler log.
#
#   usage: assert-diagnostics.sh <log-file> <label> <probe-file> <silent-file>
#
# The four assertions, in the order they are checked:
#
#   (a) nothing failed to load and nothing crashed - no CS8032, CS8033 or AD0001
#   (b) every rule the probe is marked to trigger was reported
#   (c) the file marked to trigger nothing reported nothing
#   (d) the probe compiled - no compiler errors
#
# (a) is the one that justifies the whole exercise. An analyzer built against a newer
# Roslyn than the host loads nothing, reports nothing, and the build stays green: the
# only trace is a CS8032 warning that scrolls past. (b) is what catches the same failure
# when the warning is suppressed, and (c) keeps (b) honest - "everything fires" would
# satisfy (b) on its own.
#
# The expected rule IDs are read out of the probe file's own markers, so the probe and
# the assertions cannot drift apart.
set -euo pipefail

log=${1:?log file}
label=${2:?label}
probe=${3:?probe file}
silent=${4:?file that must stay silent}

probe_name=$(basename "$probe")
silent_name=$(basename "$silent")
failed=0

fail() {
  echo "FAIL [$label] $1"
  failed=1
}

if [ ! -s "$log" ]; then
  echo "FAIL [$label] compiler log $log is missing or empty"
  exit 1
fi

# A diagnostic line looks the same from csc and from Unity:
#   <path>(<line>,<col>): warning UPA0001: <message>
diagnostics_for() {   # $1 = file name, $2 = optional id
  local id_pattern="${2:-[A-Z]+[0-9]+}"
  grep -E "${1//./\\.}\([0-9]+,[0-9]+\): (warning|error|info) ${id_pattern}:" "$log" || true
}

# (a) load and execution failures.
#   CS8032 - the analyzer could not be created (this is the silent-death signature)
#   CS8033 - the assembly was passed to the compiler but carries no analyzers
#   AD0001 - an analyzer threw
loader=$(grep -E "(CS8032|CS8033|AD0001)" "$log" || true)
if [ -n "$loader" ]; then
  fail "the compiler reported an analyzer load or execution failure:"
  echo "$loader" | sed 's/^/         /'
fi

# (d) checked early because a compile error explains away every other failure below.
errors=$(grep -E "\): error CS[0-9]+:" "$log" || true)
if [ -n "$errors" ]; then
  fail "the probe did not compile:"
  echo "$errors" | sed 's/^/         /'
fi

# (b) every marked rule fired.
expected=$(grep -oE '// expect (UPA[0-9]{4})' "$probe" | awk '{print $3}' | sort -u)
forbidden=$(grep -oE '// expect-none (UPA[0-9]{4})' "$probe" | awk '{print $3}' | sort -u)

expected_count=$(printf '%s' "$expected" | grep -c . || true)
if [ "$expected_count" -lt 3 ]; then
  fail "$probe_name carries only $expected_count 'expect' markers; the probe has lost its violations."
fi

for id in $expected; do
  if [ -z "$(diagnostics_for "$probe_name" "$id")" ]; then
    fail "$id was expected on $probe_name and was not reported."
  fi
done

for id in $forbidden; do
  if [ -n "$(diagnostics_for "$probe_name" "$id")" ]; then
    fail "$id was reported on $probe_name; it is configured off, so the ruleset did not take effect."
  fi
done

# (c) the negative file stayed silent.
noise=$(diagnostics_for "$silent_name")
if [ -n "$noise" ]; then
  fail "$silent_name must report nothing, and reported:"
  echo "$noise" | sed 's/^/         /'
fi

if [ "$failed" -ne 0 ]; then
  exit 1
fi

echo "PASS [$label] $(printf '%s' "$expected" | tr '\n' ' ') reported, $silent_name silent, no loader failures"
