#!/usr/bin/env bash
# Pins the promotion step's decisions. It runs once per release, inside the half of the
# workflow that ends in a permanent tag, so its mistakes are expensive and quiet: a section
# that says a version changed nothing, a duplicate section in a file that is meant to be
# history, or rows that vanish between the two files.
#
# Every case works on throwaway copies. No network, no build.
set -euo pipefail

here=$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)
script=$here/promote-analyzer-releases.sh
tmp=$(mktemp -d)
trap 'rm -rf "$tmp"' EXIT

failures=0
case_number=0

fixture() {   # $1 = dir, $2 = unshipped body ("" for none)
  mkdir -p "$1"
  {
    echo '; Shipped analyzer releases'
    echo '; https://example.invalid/help'
  } > "$1/Shipped.md"
  {
    echo '; Unshipped analyzer releases'
    echo '; https://example.invalid/help'
    if [ -n "$2" ]; then
      echo
      printf '%s\n' "$2"
    fi
  } > "$1/Unshipped.md"
}

new_rules_body='### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
UPA9001 | Performance | Warning | First probe rule
UPA9002 | Correctness | Disabled | Second probe rule'

report() {   # $1 = ok|fail, $2 = name, $3 = detail
  case_number=$((case_number + 1))
  if [ "$1" = ok ]; then
    printf '  %2d ok    %s\n' "$case_number" "$2"
  else
    printf '  %2d FAIL  %s -- %s\n' "$case_number" "$2" "$3"
    failures=$((failures + 1))
  fi
}

# 1. The ordinary case: rows move, and the unshipped file is left ready for the next cycle.
dir=$tmp/promote; fixture "$dir" "$new_rules_body"
if bash "$script" 1.2.3 "$dir/Shipped.md" "$dir/Unshipped.md" > /dev/null; then
  if grep -qE '^## Release 1\.2\.3$' "$dir/Shipped.md" &&
     grep -qE '^UPA9001 \|' "$dir/Shipped.md" &&
     grep -qE '^UPA9002 \|' "$dir/Shipped.md" &&
     grep -qE '^### New Rules$' "$dir/Shipped.md" &&
     ! grep -qE '^UPA[0-9]{4} \|' "$dir/Unshipped.md" &&
     grep -qE '^; Unshipped' "$dir/Unshipped.md"; then
    report ok "promotes the rows and empties the unshipped file"
  else
    report fail "promotes the rows and empties the unshipped file" "wrong contents after promotion"
  fi
else
  report fail "promotes the rows and empties the unshipped file" "script exited non-zero"
fi

# 2. Nothing to promote is an ordinary release, not an error -- and must not leave an empty
#    section behind claiming the version changed the rule set.
dir=$tmp/empty; fixture "$dir" ""
before=$(cat "$dir/Shipped.md")
if bash "$script" 1.2.3 "$dir/Shipped.md" "$dir/Unshipped.md" > /dev/null; then
  if [ "$(cat "$dir/Shipped.md")" = "$before" ]; then
    report ok "an empty unshipped file adds no release section"
  else
    report fail "an empty unshipped file adds no release section" "shipped file was modified"
  fi
else
  report fail "an empty unshipped file adds no release section" "script exited non-zero"
fi

# 3. Running twice must refuse rather than append a second section to history.
dir=$tmp/twice; fixture "$dir" "$new_rules_body"
bash "$script" 1.2.3 "$dir/Shipped.md" "$dir/Unshipped.md" > /dev/null
after_first=$(cat "$dir/Shipped.md")
fixture_unshipped_again() { printf '%s\n%s\n\n%s\n' '; Unshipped analyzer releases' '; https://example.invalid/help' "$new_rules_body" > "$dir/Unshipped.md"; }
fixture_unshipped_again
if bash "$script" 1.2.3 "$dir/Shipped.md" "$dir/Unshipped.md" > /dev/null 2>&1; then
  report fail "refuses a version already in the shipped file" "script exited zero"
elif [ "$(cat "$dir/Shipped.md")" = "$after_first" ]; then
  report ok "refuses a version already in the shipped file"
else
  report fail "refuses a version already in the shipped file" "shipped file was modified anyway"
fi

# 4. A version that is not a version would become an unparseable heading.
dir=$tmp/badversion; fixture "$dir" "$new_rules_body"
before=$(cat "$dir/Shipped.md")
if bash "$script" "main" "$dir/Shipped.md" "$dir/Unshipped.md" > /dev/null 2>&1; then
  report fail "refuses a version that is not N.N.N" "script exited zero"
elif [ "$(cat "$dir/Shipped.md")" = "$before" ]; then
  report ok "refuses a version that is not N.N.N"
else
  report fail "refuses a version that is not N.N.N" "shipped file was modified anyway"
fi

# 4b. Versions that only look like versions. The original check used a case glob, where
#     [0-9]* means "a digit then anything", so each of these passed it.
for bad in 1x.2y.3z 1.2.3-rc 1.2 v1.2.3 1.2.3.4; do
  dir=$tmp/bad-$RANDOM; fixture "$dir" "$new_rules_body"
  before=$(cat "$dir/Shipped.md")
  if bash "$script" "$bad" "$dir/Shipped.md" "$dir/Unshipped.md" > /dev/null 2>&1; then
    report fail "refuses '$bad'" "script exited zero"
  elif [ "$(cat "$dir/Shipped.md")" = "$before" ]; then
    report ok "refuses '$bad'"
  else
    report fail "refuses '$bad'" "shipped file was modified anyway"
  fi
done

# 5. Removed and Changed carry their own column layouts; a promotion that only understood
#    New Rules would drop them, and a removal that vanishes reads as a rule still shipping.
dir=$tmp/kinds
fixture "$dir" '### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
UPA9001 | Performance | Warning | First probe rule

### Removed Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
UPA9003 | Ecosystem | Disabled | Withdrawn probe rule

### Changed Rules

Rule ID | New Category | New Severity | Old Category | Old Severity | Notes
--------|--------------|--------------|--------------|--------------|-------
UPA9004 | Performance | Disabled | Performance | Warning | Re-graded probe rule'
if bash "$script" 2.0.0 "$dir/Shipped.md" "$dir/Unshipped.md" > /dev/null; then
  if grep -qE '^### Removed Rules$' "$dir/Shipped.md" &&
     grep -qE '^### Changed Rules$' "$dir/Shipped.md" &&
     grep -qE '^UPA9003 \|' "$dir/Shipped.md" &&
     grep -qE '^UPA9004 \| Performance \| Disabled \| Performance \| Warning \|' "$dir/Shipped.md"; then
    report ok "carries Removed and Changed sections across unchanged"
  else
    report fail "carries Removed and Changed sections across unchanged" "a subsection did not survive"
  fi
else
  report fail "carries Removed and Changed sections across unchanged" "script exited non-zero"
fi

# 6. A missing file is a setup mistake, and the release must stop rather than write half of it.
dir=$tmp/missing; fixture "$dir" "$new_rules_body"
if bash "$script" 1.2.3 "$dir/Shipped.md" "$dir/NoSuchFile.md" > /dev/null 2>&1; then
  report fail "refuses when a file is missing" "script exited zero"
else
  report ok "refuses when a file is missing"
fi

echo
if [ "$failures" -eq 0 ]; then
  echo "promote-analyzer-releases self-test: $case_number cases, all pass"
else
  echo "promote-analyzer-releases self-test: $failures of $case_number cases FAILED" >&2
  exit 1
fi
