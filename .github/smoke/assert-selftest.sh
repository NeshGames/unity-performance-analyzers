#!/usr/bin/env bash
# Checks that assert-diagnostics.sh actually fails.
#
# The smoke test is a safety net, and a safety net with a hole in it is worse than none:
# it reports PASS either way, and the PASS is what people act on. So each way the smoke
# test is supposed to catch a problem gets a synthetic log here that contains exactly
# that problem, and this script fails if the assertions let it through.
#
# Pure text processing - no compiler, no Unity, milliseconds.
set -euo pipefail

here=$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)
tmp=$(mktemp -d)
trap 'rm -rf "$tmp"' EXIT

cat > "$tmp/Fake.cs" <<'EOF'
// expect-none UPA0005
void M() {
    A();   // expect UPA0001
    B();   // expect UPA0002
    C();   // expect UPA0006
}
EOF
: > "$tmp/FakeSilent.cs"

good() {
  cat <<'EOF'
Fake.cs(3,5): warning UPA0001: message
Fake.cs(4,5): warning UPA0002: message
Fake.cs(5,5): warning UPA0006: message
EOF
}

failures=0

expect() {   # $1 = expected outcome (pass|fail), $2 = case name, stdin = log
  local want=$1 name=$2
  cat > "$tmp/log.txt"
  local got=pass
  "$here/assert-diagnostics.sh" "$tmp/log.txt" "selftest" "$tmp/Fake.cs" "$tmp/FakeSilent.cs" \
    > "$tmp/out.txt" 2>&1 || got=fail
  if [ "$got" != "$want" ]; then
    echo "SELFTEST FAIL: '$name' should $want, got $got"
    sed 's/^/    /' "$tmp/out.txt"
    failures=1
  else
    echo "ok  $name -> $got"
  fi
}

good | expect pass "a clean log"

{ good; echo "warning CS8032: An instance of analyzer cannot be created from x.dll"; } \
  | expect fail "the analyzer could not be created"

{ good; echo "warning CS8033: x.dll does not contain any analyzers"; } \
  | expect fail "the assembly carries no analyzers"

{ good; echo "warning AD0001: Analyzer 'X' threw an exception of type 'System.NullReferenceException'"; } \
  | expect fail "an analyzer threw"

good | grep -v UPA0002 | expect fail "an expected rule did not fire"

{ good; echo "Fake.cs(9,5): warning UPA0005: message"; } \
  | expect fail "a rule configured off still fired"

{ good; echo "FakeSilent.cs(4,9): warning UPA0001: message"; } \
  | expect fail "the negative file was flagged"

{ good; echo "Fake.cs(7,1): error CS0103: The name 'A' does not exist"; } \
  | expect fail "the probe did not compile"

printf '' | expect fail "an empty log"

# The help link in a message ends in the rule ID, and a message can quote a rule ID.
# Neither is a diagnostic, and neither may be mistaken for one.
good | grep -v UPA0006 \
  | { cat; echo "Fake.cs(5,5): warning UPA0001: see (https://example.invalid/rules/UPA0006.md)"; } \
  | expect fail "a rule ID mentioned inside a message is not a report of that rule"

exit $failures
