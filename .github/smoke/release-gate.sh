#!/usr/bin/env bash
# Decides whether the Unity smoke verdicts allow a release to proceed.
#
#   usage: release-gate.sh <verdict-directory> <versions-json>
#   env:   OVERRIDE      reason given for releasing without a smoke result ("" = none)
#          GITHUB_ENV    optional; the accepted reason is written here for later steps
#
#   exit 0  release may proceed
#   exit 1  release is blocked
#
# The verdict directory holds one <verdict-directory>/unity-smoke-<version>/verdict.txt
# per version, containing pass, product or infra.
#
# The override exists because the smoke test depends on a licence server, a container
# registry and an editor that takes minutes to start, none of which say anything about
# the package. Without an override, an outage anywhere in that chain blocks releases
# indefinitely. The risk it introduces is the opposite one - that the override becomes
# how failures get waved through - so it is scoped to exactly what it is for:
#
#   product      the project compiled and the result was wrong. Never overridable.
#   infra        the project never compiled. Overridable with a stated reason.
#   no verdict   treated as infra: producing any verdict at all requires a compile that
#                got far enough to judge, so silence can only mean it never got there.
#
# The asymmetry is the point. An infrastructure failure can be waved through because it
# carries no information about the package; a product failure carries nothing but.
set -euo pipefail

verdict_dir=${1:?verdict directory}
versions_file=${2:?versions json}
override=${OVERRIDE:-}

# Read the list into a variable and check it, rather than iterating the command
# substitution directly: a `for x in $(...)` over a failed or empty command iterates zero
# times, and set -e does not fire there. The gate would then check nothing at all and
# allow the release - green, silent, and wrong.
#
# Extracted with grep rather than jq so that the one script standing between a failed
# package and a permanent tag needs nothing installed to run, here or on a laptop. The
# file is a flat array of version strings; anything else fails the emptiness check below.
versions=$(grep -oE '"[^"]+"' "$versions_file" | tr -d '"')
if [ -z "$versions" ]; then
  echo "::error::no Unity versions listed in $versions_file"
  exit 1
fi

refused=0
blocked=0
for version in $versions; do
  file="$verdict_dir/unity-smoke-$version/verdict.txt"
  verdict=$([ -f "$file" ] && cat "$file" || echo "no verdict")
  echo "$version: $verdict"
  case "$verdict" in
    pass) ;;
    product) refused=1 ;;
    *) blocked=1 ;;
  esac
done

if [ "$refused" -eq 1 ]; then
  echo "::error::The package failed the Unity smoke test. This is not overridable: the project compiled and the analyzers did not behave. Fix it and release again."
  exit 1
fi

if [ "$blocked" -eq 1 ]; then
  if [ -z "$override" ]; then
    echo "::error::The Unity smoke test could not run, so this package is untested against Unity. Re-run the workflow with a reason in 'smoke_override' to release anyway - it will be recorded on the release commit."
    exit 1
  fi
  echo "::warning::Releasing without a Unity smoke result. Reason: $override"
  if [ -n "${GITHUB_ENV:-}" ]; then
    echo "SMOKE_OVERRIDE_NOTE=$override" >> "$GITHUB_ENV"
  fi
  if [ -n "${GITHUB_STEP_SUMMARY:-}" ]; then
    {
      echo "### Unity smoke test overridden"
      echo ""
      echo "This release was not verified against Unity. Reason given: $override"
    } >> "$GITHUB_STEP_SUMMARY"
  fi
elif [ -n "$override" ]; then
  echo "::notice::smoke_override was set but not needed; the Unity smoke test passed."
fi

exit 0
