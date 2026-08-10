#!/usr/bin/env bash
# Move the unshipped analyzer release notes into the shipped file under the version being
# tagged, and leave the unshipped file empty for the next cycle.
#
#   usage: promote-analyzer-releases.sh <version> [<shipped> <unshipped>]
#
# Why a release has to do this at all: the shipped file is the only place that records which
# analyzer version introduced, removed or re-graded a rule, and a consumer reads it to answer
# "when did this start firing at me?". Left unpromoted, every rule reads as never released --
# which is the state this repository was actually in for eight tags.
#
# Two properties matter more than the copying:
#
#   It refuses rather than repeats. A tag is permanent, so a second promotion of the same
#   version would append a duplicate section to a file that is meant to be history. The check
#   is on the version already being present, not on a flag someone has to remember.
#
#   It changes nothing unless it changes everything. Both files are written from temporaries
#   at the very end, so a failure half way leaves a working tree the release can be re-run
#   from, rather than a shipped file with a heading and no rules under it.
set -euo pipefail

version=${1:-}
shipped=${2:-src/UnityPerformanceAnalyzers/AnalyzerReleases.Shipped.md}
unshipped=${3:-src/UnityPerformanceAnalyzers/AnalyzerReleases.Unshipped.md}

fail() { echo "promote-analyzer-releases: $*" >&2; exit 1; }

[ -n "$version" ] || fail "no version given"

# Checked rather than trusted: the version is the section heading, and a heading that is not
# a version makes the file unparseable to the release-tracking analyzer -- which reports it
# at the next build, long after the tag exists.
# A case glob will not do this: [0-9]* matches a digit followed by anything at all, so
# "1x.2y.3z" and "1.2.3-rc" both satisfy the pattern this check appears to enforce. An
# assertion that accepts what it claims to reject is worse than no assertion, because the
# malformed value reaches the heading looking approved.
if ! [[ "$version" =~ ^[0-9]+\.[0-9]+\.[0-9]+$ ]]; then
  fail "version '$version' is not N.N.N"
fi

[ -f "$shipped" ] || fail "no shipped file at $shipped"
[ -f "$unshipped" ] || fail "no unshipped file at $unshipped"

if grep -qE "^## Release $version\$" "$shipped"; then
  fail "$shipped already has a section for $version; nothing was changed"
fi

# The rule rows, not the whole file: a file carrying only its header comment has nothing to
# promote, and that is an ordinary release rather than a mistake.
ids=$(grep -oE '^UPA[0-9]{4}' "$unshipped" | sort -u || true)
if [ -z "$ids" ]; then
  echo "promote-analyzer-releases: nothing unshipped; $shipped left as it is"
  exit 0
fi

# Everything after the leading comment block, which is the tables and their subsection
# headings. Copied verbatim so New / Removed / Changed survive with their own column layouts.
body=$(awk '
  !started && ($0 ~ /^;/ || $0 ~ /^[[:space:]]*$/) { next }
  { started = 1; print }
' "$unshipped")

work=$(mktemp -d)
trap 'rm -rf "$work"' EXIT

cp "$shipped" "$work/shipped"
printf '\n## Release %s\n\n%s\n' "$version" "$body" >> "$work/shipped"

# The unshipped file keeps its header: the project references it as an AdditionalFile, and a
# missing one is a build error rather than an empty section.
grep -E '^;' "$unshipped" > "$work/unshipped"

# Assert the move actually moved things before either file is replaced. A copy that silently
# dropped rows would otherwise produce a green release and a shipped file describing fewer
# rules than the package has -- and the next promotion would bury the evidence.
for id in $ids; do
  grep -qE "^$id \|" "$work/shipped" || fail "$id did not survive the promotion; nothing was changed"
done

mv "$work/shipped" "$shipped"
mv "$work/unshipped" "$unshipped"

echo "promote-analyzer-releases: promoted $(echo "$ids" | wc -w | tr -d ' ') rule rows into $shipped as Release $version"
