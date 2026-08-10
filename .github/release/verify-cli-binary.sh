#!/usr/bin/env bash
# Decides whether a published upa-cli binary actually works.
#
#   usage: verify-cli-binary.sh <executable> <expected-version>
#
# Starting is not the same as working, and the difference is the whole reason this exists.
# The binary is published self-contained and not as a single file, because the runner
# resolves the reference assemblies it compiles against through Assembly.Location -- which a
# single-file bundle defines as the empty string. A build that gets that wrong still prints
# its version number happily and then reports nothing on every file it is given. Only
# analyzing real source tells the two apart.
#
# It lives in a script rather than inline in the workflow for a reason that cost a release.
# GitHub starts a `run:` step as `bash -e`, and `set -uo pipefail` written inside the step
# does not clear that. upa-cli exits 1 when it finds violations, which is the passing outcome
# here -- so the assignment that captured its output aborted the step instead, on all three
# platforms at once, during 0.8.1. A script is a child bash with its own options, and it can
# be run here before a release depends on it.
set -uo pipefail

exe=${1:?path to the published upa-cli}
expected=${2:?expected version}

fail() { echo "::error::$*"; exit 1; }

[ -x "$exe" ] || [ -f "$exe" ] || fail "no executable at $exe"

reported=$("$exe" --version) || fail "$exe could not print its version"
if [ "$reported" != "$expected" ]; then
  fail "published binary reports '$reported', expected '$expected'"
fi

work=$(mktemp -d)
trap 'rm -rf "$work"' EXIT

# UPA0005 is off by default, hence --all-warn below: a probe that cannot fail proves nothing.
printf '%s\n' \
  'using UnityEngine;' \
  '' \
  'public sealed class Probe : MonoBehaviour' \
  '{' \
  '    private void Update()' \
  '    {' \
  '        Debug.Log("hello");' \
  '    }' \
  '}' > "$work/Probe.cs"

if output=$("$exe" "$work/Probe.cs" --all-warn); then
  status=0
else
  status=$?
fi
printf '%s\n' "$output"

# Exit 1 is the success case: it means violations were found.
if [ "$status" -ne 1 ]; then
  fail "expected exit 1 from a file that violates UPA0005, got $status. The binary starts, so this is the reference assemblies failing to resolve rather than a broken command line."
fi

printf '%s' "$output" | grep -q 'warning UPA0005' ||
  fail "exit 1 but no UPA0005 in the output; something else failed the run"

echo "ok: $exe reports $reported and analyzes a file"
