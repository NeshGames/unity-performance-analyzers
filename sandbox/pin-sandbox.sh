#!/usr/bin/env bash
# Pins sandbox/UnityProject to a Unity 6 editor and adds the measurement dependencies.
#
#   usage: pin-sandbox.sh <project-dir> <editor-version>
#
# Unity 6 carries TextMeshPro through com.unity.ugui 2.0.0. Older editor branches are
# intentionally unsupported, so there is no version-dependent package branch here anymore.
set -euo pipefail

here=$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)
root=$(cd "$here/.." && pwd)

project=${1:?project directory}
version=${2:?editor version}

bash "$root/.github/smoke/pin-editor.sh" "$project" "$version"

text_package='"com.unity.ugui": "2.0.0"'

manifest=$project/Packages/manifest.json

# Inserted after the opening of "dependencies" so the file stays valid JSON without needing a
# JSON parser here - the same reason verify.sh greps rather than calling jq.
tmp=$manifest.tmp
sed "s|\"dependencies\": {|\"dependencies\": {\n    $text_package,|" "$manifest" > "$tmp"
mv "$tmp" "$manifest"

grep -q "$text_package" "$manifest" || {
  echo "pin-sandbox.sh: failed to add $text_package to $manifest" >&2
  exit 1
}

# ZString's UPM package does not carry System.Runtime.CompilerServices.Unsafe, and its own
# Unity project keeps that DLL in Assets/Plugins - so a consumer is expected to supply it.
# Without it ZString itself does not compile, which fails the whole project rather than only
# the measurement that needs it.
#
# Fetched from the same repository the package comes from, pinned to the same commit, and not
# committed here: it is Microsoft's assembly redistributed by Cysharp, and downloading it is
# already implied by taking the package from that repository at all.
unsafe_dll=$project/Assets/Plugins/System.Runtime.CompilerServices.Unsafe.dll
zstring_commit=604dc1eb5ada260a7be546d3d647482dc5bd0578

if [ ! -s "$unsafe_dll" ]; then
  mkdir -p "$(dirname "$unsafe_dll")"
  url=https://raw.githubusercontent.com/Cysharp/ZString/$zstring_commit/src/ZString.Unity/Assets/Plugins/System.Runtime.CompilerServices.Unsafe.dll
  echo "   fetching System.Runtime.CompilerServices.Unsafe.dll for ZString"
  curl -fsSL "$url" -o "$unsafe_dll" || {
    echo "pin-sandbox.sh: could not fetch $url" >&2
    rm -f "$unsafe_dll"
    exit 1
  }
fi

# A truncated or HTML-error download compiles no better than a missing file, and the error it
# produces names ZString rather than this step.
[ "$(wc -c < "$unsafe_dll")" -gt 10000 ] || {
  echo "pin-sandbox.sh: $unsafe_dll is too small to be the assembly; delete it and re-run" >&2
  exit 1
}
