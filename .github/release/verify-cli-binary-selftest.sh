#!/usr/bin/env bash
# Pins what verify-cli-binary.sh accepts and refuses, using stand-in binaries.
#
# The case that matters most is the first one, and it is why this file exists: the check has
# to survive its subject exiting non-zero, because upa-cli exits 1 exactly when it has found
# what the check is looking for. Written inline in the workflow, that logic ran under the
# runner's `bash -e` and died on the passing case. Every case below is also run with -e
# turned on in this shell, so the arrangement that failed cannot come back unnoticed.
set -uo pipefail

here=$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)
script=$here/verify-cli-binary.sh
tmp=$(mktemp -d)
trap 'rm -rf "$tmp"' EXIT

failures=0
case_number=0

report() {   # $1 = ok|fail, $2 = name, $3 = detail
  case_number=$((case_number + 1))
  if [ "$1" = ok ]; then
    printf '  %2d ok    %s\n' "$case_number" "$2"
  else
    printf '  %2d FAIL  %s -- %s\n' "$case_number" "$2" "$3"
    failures=$((failures + 1))
  fi
}

# $1 = name, $2 = version it reports, $3 = exit code when analyzing, $4 = what it prints
stub() {
  local path=$tmp/$1
  cat > "$path" <<EOF
#!/usr/bin/env bash
if [ "\$1" = "--version" ]; then printf '%s\n' '$2'; exit 0; fi
printf '%s\n' '$4'
exit $3
EOF
  chmod +x "$path"
  printf '%s' "$path"
}

check() {   # $1 = expected (accept|refuse), $2 = name, $3 = stub path
  # Both invocations, and they must agree.
  #
  # `bash script` is what the release workflow actually does, and bash options do not cross a
  # process boundary, so the runner's -e never reaches the script through that call. `bash -e
  # script` is the stricter arrangement -- the one an inlined copy would face, and the one the
  # original failure happened under. Testing only the strict form pins a property production
  # does not depend on; testing only the loose form would have let the original defect through.
  #
  # Not `( set -e; bash ... )`: a subshell's -e does not reach a child either, so that form
  # asserted nothing. It passed all five cases even with the capture written the way that
  # failed in CI.
  local plain=accept strict=accept
  bash    "$script" "$3" 1.2.3 > /dev/null 2>&1 || plain=refuse
  bash -e "$script" "$3" 1.2.3 > /dev/null 2>&1 || strict=refuse

  if [ "$plain" != "$strict" ]; then
    report fail "$2" "differs by caller: plain=$plain, -e=$strict"
    return
  fi

  local got=$plain
  if [ "$got" = "$1" ]; then
    report ok "$2"
  else
    report fail "$2" "expected $1, got $got"
  fi
}

check accept "a binary that reports violations and exits 1" \
  "$(stub good 1.2.3 1 'Probe.cs(7,9): warning UPA0005: Debug.Log is called directly.')"

check refuse "a binary that finds nothing and exits 0" \
  "$(stub silent 1.2.3 0 'No diagnostics.')"

check refuse "a binary reporting the wrong version" \
  "$(stub wrongver 9.9.9 1 'Probe.cs(7,9): warning UPA0005: Debug.Log is called directly.')"

check refuse "a binary that exits 1 for some other reason" \
  "$(stub other 1.2.3 1 'error: could not open the file')"

check refuse "a path with no binary at it" "$tmp/does-not-exist"

echo
if [ "$failures" -eq 0 ]; then
  echo "verify-cli-binary self-test: $case_number cases, all pass"
else
  echo "verify-cli-binary self-test: $failures of $case_number cases FAILED" >&2
  exit 1
fi
