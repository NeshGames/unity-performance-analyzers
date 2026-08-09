#!/usr/bin/env bash
# Decides whether the Unity smoke verdicts allow a release to proceed.
#
#   usage: release-gate.sh <verdict-directory> <versions-json>
#   env:   LOCAL_VERDICT  versions verified by running unity-load.sh outside CI ("" = none)
#          OVERRIDE       reason for releasing with no Unity result at all ("" = none)
#          GITHUB_ENV     optional; whichever was accepted is written here for later steps
#
#   exit 0  release may proceed
#   exit 1  release is blocked
#
# The verdict directory holds one <verdict-directory>/unity-smoke-<version>/verdict.txt
# per version, containing pass, product or infra.
#
#   product      the project compiled and the result was wrong. Never waved through.
#   infra        the project never compiled. See below.
#   no verdict   treated as infra: producing any verdict at all requires a compile that
#                got far enough to judge, so silence can only mean it never got there.
#
# The asymmetry is the point. An infrastructure failure carries no information about the
# package; a product failure carries nothing but.
#
# There are two ways past an infrastructure result, and they are not the same claim:
#
#   LOCAL_VERDICT  the Unity check was run, just not here. Every required version must be
#                  named and reported as passing, so the claim is specific enough to be
#                  wrong and a pasted failure is not mistaken for one. This is the ordinary
#                  path when the editor licence is deliberately kept off CI.
#   OVERRIDE       the Unity check was not run at all, and here is why. This is the
#                  outage path, and it is the one that says the package is untested.
#
# Neither is verifiable from here - both are a human's word, recorded on the release
# commit. Keeping them apart is what stops the second from quietly becoming routine,
# which is what happens to a single escape hatch that every release has to use.
set -euo pipefail

verdict_dir=${1:?verdict directory}
versions_file=${2:?versions json}
override=${OVERRIDE:-}
local_verdict=${LOCAL_VERDICT:-}

# Read the list into a variable and check it, rather than iterating the command
# substitution directly: a `for x in $(...)` over a failed or empty command iterates zero
# times, and set -e does not fire there. The gate would then check nothing at all and
# allow the release - green, silent, and wrong.
#
# Extracted with grep rather than jq so that the one script standing between a failed
# package and a permanent tag needs nothing installed to run, here or on a laptop. This
# lifts every quoted string rather than parsing JSON, which is sound only because the file
# is in this repository and reviewed with it - it is not input, and it is not validated as
# though it were. What the check below catches is the file being absent, empty or
# unreadable, not a malformed one that still happens to contain quoted strings.
versions=$(grep -oE '"[^"]+"' "$versions_file" | tr -d '"')
if [ -z "$versions" ]; then
  echo "::error::no Unity versions listed in $versions_file"
  exit 1
fi

# Both notes travel to later steps through GITHUB_ENV, and both can be multi-line:
# unity-load.sh's own output is one line per version, and pasting it is the point. The
# NAME=value spelling cannot carry that - everything after the first newline is read as
# further entries - so the delimiter form is the only correct one here. A value containing
# the delimiter would reopen exactly that hole, so it is refused rather than escaped.
record() {   # $1 = name, $2 = value
  [ -n "${GITHUB_ENV:-}" ] || return 0
  case "$2" in
    *UPA_NOTE_EOF*)
      echo "::error::the stated result may not contain the text UPA_NOTE_EOF"
      exit 1
      ;;
  esac
  {
    printf '%s<<UPA_NOTE_EOF\n' "$1"
    printf '%s\n' "$2"
    printf 'UPA_NOTE_EOF\n'
  } >> "$GITHUB_ENV"
}

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
  if [ -n "$local_verdict" ]; then
    # Every version must be named AND said to have passed, in one exact spelling:
    #
    #     2022.3.62f2=pass 6000.5.3f1=pass
    #
    # The value is parsed against that, not searched for a pass inside it. Searching was
    # wrong four times running - "2022.3.62f2 failed" holds the version, "NOT PASS [Unity
    # 2022.3.62f2]" holds the pass line, "2022.3.62f2=pass=false" holds the token and then
    # takes it back, "PASS [Unity 2022.3.62f2] PACKAGE FAILED, no loader failures" holds
    # both ends - because free text can always be arranged to carry a pass inside a report
    # of failure.
    #
    # Pasting unity-load.sh's own output used to be accepted as a second spelling. It is
    # not any more. Every version of that check kept one field open - the rule IDs, the
    # silent file's name - and an open field is somewhere to write the failure. Matching it
    # exactly would mean deriving the emitted line from Probe.cs here, which makes this
    # script an inch from a second implementation of the assertion it is quoting. The short
    # form has no open field at all: a token either equals a known version followed by
    # "=pass" or it is refused. Repeated separators and a version stated twice are accepted
    # and mean nothing extra - the set of accepted strings is not finite, but the set of
    # things any of them can say is, and "it failed" is not among them.
    # What it costs is typing two words per version.
    #
    # A human can still write "=pass" about a run that failed. Nothing checkable stops that;
    # what this stops is a failure travelling through without anyone having said otherwise.
    covered=""
    form=short

    # Commas and semicolons read as separators, and spaces around the "=" are closed up, so
    # "a = pass, b = pass" is two tokens rather than seven. No prose survives this: "not
    # a=pass" still leaves a "not" that nothing accepts.
    candidate=$(printf '%s' "$local_verdict" | tr ',;' '  ' |
                sed 's/[[:space:]]*=[[:space:]]*/=/g')
    if [ -z "$(printf '%s' "$candidate" | tr -d '[:space:]')" ]; then
      form=invalid
    else
      # Word-splitting is wanted here; pathname expansion is not. Unset rather than trusted
      # to be harmless: a token is compared against a fixed list, so a glob that happened to
      # match files would be answering with the working directory's contents.
      set -f
      for token in $candidate; do
        lowered=$(printf '%s' "$token" | tr '[:upper:]' '[:lower:]')
        name=""
        case "$lowered" in
          *=pass) name=${lowered%=pass} ;;
        esac
        if [ -z "$name" ]; then
          form=invalid
          break
        fi
        known=""
        for version in $versions; do
          if [ "$name" = "$(printf '%s' "$version" | tr '[:upper:]' '[:lower:]')" ]; then
            known=$version
          fi
        done
        if [ -z "$known" ]; then
          form=invalid
          break
        fi
        covered="$covered $known "
      done
      set +f
    fi

    if [ "$form" = invalid ]; then
      echo "::error::The stated result must be '<version>=pass' for every supported version and nothing else, e.g. '2022.3.62f2=pass 6000.5.3f1=pass'. Pasted script output is not accepted: every field it leaves open is somewhere a failure can be written. If a version did not pass, fix it - this is not the input for that. If the check was not run at all, use 'smoke_override'."
      exit 1
    fi

    missing=""
    for version in $versions; do
      case "$covered" in
        *" $version "*) ;;
        *) missing="$missing $version" ;;
      esac
    done
    if [ -n "$missing" ]; then
      echo "::error::The local Unity result does not report a pass for:$missing. Run 'bash .github/smoke/unity-load.sh <editor> <version>' for every supported version and state each as '<version>=pass', or paste the script's own PASS lines. If a version did not pass, fix it - this is not the input for that. If the check was not run at all, use 'smoke_override'."
      exit 1
    fi

    echo "::notice::Released on a Unity result produced outside CI: $local_verdict"
    record LOCAL_SMOKE_NOTE "$local_verdict"
    if [ -n "${GITHUB_STEP_SUMMARY:-}" ]; then
      {
        echo "### Unity smoke test run outside CI"
        echo ""
        echo "The editor licence is deliberately not held by this repository, so the Unity"
        echo "layer runs on a developer machine. Result stated: $local_verdict"
      } >> "$GITHUB_STEP_SUMMARY"
    fi
    if [ -n "$override" ]; then
      echo "::notice::smoke_override was also set; the local result takes precedence and the override was not used."
    fi
  elif [ -z "$override" ]; then
    echo "::error::There is no Unity result for this package. Run 'bash .github/smoke/unity-load.sh <editor> <version>' for every supported version and state them in 'local_smoke', or give a reason in 'smoke_override' if it could not be run. Either way it is recorded on the release commit."
    exit 1
  else
    echo "::warning::Releasing without any Unity smoke result. Reason: $override"
    record SMOKE_OVERRIDE_NOTE "$override"
    if [ -n "${GITHUB_STEP_SUMMARY:-}" ]; then
      {
        echo "### Unity smoke test overridden"
        echo ""
        echo "This release was not verified against Unity. Reason given: $override"
      } >> "$GITHUB_STEP_SUMMARY"
    fi
  fi
else
  # if, not `[ -n "$x" ] && echo`: under set -e a false test at the end of an and-list is
  # the script's exit status, so the shorter spelling would block a release for the sole
  # reason that nothing was wrong with it.
  if [ -n "$override" ]; then
    echo "::notice::smoke_override was set but not needed; the Unity smoke test passed."
  fi
  if [ -n "$local_verdict" ]; then
    echo "::notice::local_smoke was set but not needed; the Unity smoke test ran here and passed."
  fi
fi

exit 0
