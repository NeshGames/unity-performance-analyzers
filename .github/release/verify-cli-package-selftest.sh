#!/usr/bin/env bash
# Pins what verify-cli-package.sh accepts and refuses, with a stand-in `dotnet` on PATH.
#
# The real install is exercised by the pull request workflow, which packs the tool and runs the
# script against it. What this covers is the decision around that install - each way the tool
# can fail to start has to be a refusal, under the runner's `bash -e` as well as without it -
# plus the one property that is easy to lose: the install must not be served a copy of the same
# version already sitting in the package folder, stale from an earlier run or a CI cache.
set -uo pipefail

here=$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)
script=$here/verify-cli-package.sh
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

# The stand-in reads its behaviour from the environment:
#   FAKE_INSTALL   exit code of `dotnet tool install`
#   FAKE_VERSION   what `dotnet upa-cli --version` prints
#   FAKE_RULES     exit code of `dotnet upa-cli --list-rules`
#   FAKE_STALE     a directory whose presence at install time is recorded in FAKE_LOG
mkdir -p "$tmp/bin"
cat > "$tmp/bin/dotnet" <<'EOF'
#!/usr/bin/env bash
case "$1 ${2:-}" in
  "new tool-manifest") exit 0 ;;
  "tool install")
    if [ -e "${FAKE_STALE:-/nonexistent}" ]; then echo present; else echo absent; fi > "$FAKE_LOG"
    exit "$FAKE_INSTALL" ;;
  "upa-cli --version") printf '%s\n' "$FAKE_VERSION"; exit 0 ;;
  "upa-cli --list-rules") exit "$FAKE_RULES" ;;
esac
echo "unexpected: dotnet $*" >&2
exit 99
EOF
chmod +x "$tmp/bin/dotnet"

packages=$tmp/nuget
mkdir -p "$packages"
touch "$packages/NeshGames.UnityPerformanceAnalyzers.Cli.1.2.3.nupkg"

check() {   # $1 = expected (accept|refuse), $2 = name, $3 = package dir, $4 install, $5 version, $6 rules
  # Both invocations, and they must agree - see verify-cli-binary-selftest.sh for why.
  local plain=accept strict=accept
  local env=(PATH="$tmp/bin:$PATH" FAKE_LOG="$tmp/install.log" NUGET_PACKAGES="$tmp/global"
             FAKE_INSTALL="$4" FAKE_VERSION="$5" FAKE_RULES="$6")
  env "${env[@]}" bash    "$script" "$3" 1.2.3 > /dev/null 2>&1 || plain=refuse
  env "${env[@]}" bash -e "$script" "$3" 1.2.3 > /dev/null 2>&1 || strict=refuse

  if [ "$plain" != "$strict" ]; then
    report fail "$2" "differs by caller: plain=$plain, -e=$strict"
  elif [ "$plain" = "$1" ]; then
    report ok "$2"
  else
    report fail "$2" "expected $1, got $plain"
  fi
}

check accept "a package that installs, reports its version and lists rules" "$packages" 0 1.2.3 0
check refuse "no package of the expected version" "$tmp/empty" 0 1.2.3 0
check refuse "a package that does not install" "$packages" 1 1.2.3 0
check refuse "a tool reporting the wrong version" "$packages" 0 9.9.9 0
check refuse "a tool that cannot list its rules" "$packages" 0 1.2.3 1

# The package folder can hold this exact id and version already - restored from a CI cache, or
# left by an earlier run - and install prefers it to the package just built. That copy has to
# be gone by the time install runs; other versions and other packages are left alone.
stale=$tmp/global/neshgames.unityperformanceanalyzers.cli
mkdir -p "$stale/1.2.3" "$stale/1.2.2" "$tmp/global/other.package/1.2.3"
rm -f "$tmp/install.log"
env PATH="$tmp/bin:$PATH" FAKE_LOG="$tmp/install.log" FAKE_INSTALL=0 FAKE_VERSION=1.2.3 FAKE_RULES=0 \
  FAKE_STALE="$stale/1.2.3" NUGET_PACKAGES="$tmp/global" bash "$script" "$packages" 1.2.3 > /dev/null 2>&1
seen=$(cat "$tmp/install.log" 2>/dev/null || true)
if [ "$seen" = absent ] && [ -d "$stale/1.2.2" ] && [ -d "$tmp/global/other.package/1.2.3" ]; then
  report ok "a copy of the same version already in the package folder is removed before install"
else
  report fail "a copy of the same version already in the package folder is removed before install" \
    "at install time it was '${seen:-never checked}'"
fi

echo
if [ "$failures" -eq 0 ]; then
  echo "verify-cli-package self-test: $case_number cases, all pass"
else
  echo "verify-cli-package self-test: $failures of $case_number cases FAILED" >&2
  exit 1
fi
