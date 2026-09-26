#!/usr/bin/env bash
# Decides whether a packed upa-cli tool package installs and starts.
#
#   usage: verify-cli-package.sh <package-directory> <expected-version>
#
# dotnet pack succeeding says nothing about whether the tool starts, and the difference is
# invisible until someone installs it - which, for a NuGet version, is after it can no longer
# be withdrawn. So the package is installed the way a user would install it, and has to report
# the version it was packed as and list its rules.
#
# Run three times: on every pull request, before a release is tagged (against the working
# tree), and again before publishing (against the tag). It was written out inline in all three
# places until they differed, which is how a check gets weaker in exactly one of them.
set -uo pipefail

directory=${1:?directory holding the packed .nupkg}
expected=${2:?expected version}

fail() { echo "::error::$*"; exit 1; }

id=NeshGames.UnityPerformanceAnalyzers.Cli
package="$directory/$id.$expected.nupkg"
if [ ! -f "$package" ]; then
  echo "::error::expected $package; the release workflow attaches that exact path."
  ls -1 "$directory" 2>/dev/null
  exit 1
fi
source=$(cd "$directory" && pwd)

work=$(mktemp -d)
trap 'rm -rf "$work"' EXIT

# The package folder may already hold this id at this version - from an earlier run of this
# script, or restored from a CI cache - and install takes a package it already has over the
# one just packed, which would verify the old package and pass. So that copy goes first.
#
# Not a package folder of its own instead: `dotnet tool run` finds a local tool through a
# resolver cache under ~/.dotnet keyed by id and version, and an install into a throwaway
# folder leaves an entry there that points into a directory the next run finds deleted.
packages=${NUGET_PACKAGES:-$HOME/.nuget/packages}
rm -rf "$packages/$(printf '%s' "$id" | tr '[:upper:]' '[:lower:]')/$expected"

cd "$work" || fail "cannot enter $work"
dotnet new tool-manifest > /dev/null || fail "could not create a tool manifest in $work"
dotnet tool install "$id" --add-source "$source" --version "$expected" ||
  fail "$package did not install as a tool"

reported=$(dotnet upa-cli --version) || fail "the installed tool could not print its version"
if [ "$reported" != "$expected" ]; then
  fail "packed tool reports $reported, expected $expected"
fi

dotnet upa-cli --list-rules > /dev/null || fail "the installed tool could not list its rules"

echo "ok: $id $expected installs, reports its version and lists its rules"
