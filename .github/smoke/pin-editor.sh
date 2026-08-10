#!/usr/bin/env bash
# Pins a Unity project to one editor version, so the next editor to open it does not
# upgrade it.
#
#   usage: pin-editor.sh <project-dir> <editor-version>
#
# A Unity project carries the editor that owns it, and opening it in a newer one is an
# upgrade: the newer editor rewrites Packages/manifest.json with its own module set, and
# the older editor then cannot resolve those packages at all. It exits before compiling
# anything, with a log that says nothing about analyzers - so the failure reads as
# "the editor is broken", not "you ran two versions against one project".
#
# Both supported editors have to run against the same sources, which is the entire point
# of this repository, so the manifest and the version stamp are written per run rather
# than committed. This script is that write, and nothing else: what to delete beforehand
# differs by caller - the smoke probe rebuilds its whole Assets folder, while the sandbox
# keeps sources that are committed - and merging those would let one caller's cleanup
# policy destroy the other's project.
set -euo pipefail

project=${1:?project directory}
version=${2:?editor version}

template="$project/Packages/manifest.template.json"
[ -f "$template" ] || { echo "no manifest template at $template" >&2; exit 1; }

mkdir -p "$project/Packages" "$project/ProjectSettings"
cp "$template" "$project/Packages/manifest.json"

# The lock file records resolved versions from whichever editor wrote it, and a stale one
# reintroduces exactly the packages the template was reset to avoid.
rm -f "$project/Packages/packages-lock.json"

printf 'm_EditorVersion: %s\n' "$version" > "$project/ProjectSettings/ProjectVersion.txt"
