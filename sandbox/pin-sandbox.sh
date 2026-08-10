#!/usr/bin/env bash
# Pins sandbox/UnityProject to one editor and adds the dependencies that differ between
# editors.
#
#   usage: pin-sandbox.sh <project-dir> <editor-version>
#
# pin-editor.sh writes the manifest from one template, which is what the smoke probe and the
# lightbulb probe want: the same dependencies whichever editor opens them. The measurement
# project cannot do that, because TextMeshPro is not the same package on both:
#
#   2022.3  com.unity.textmeshpro 3.0.7   shipped as a tarball inside the editor
#   Unity 6 com.unity.textmeshpro 5.0.0   a shim whose description says it is no longer
#                                         supported and whose only dependency is
#                                         com.unity.ugui 2.0.0, where TMP now lives
#
# Both resolve from the editor installation, so neither needs the network. Asking for 3.0.7
# on Unity 6 fails; asking for ugui 2.0.0 on 2022.3 fails. Hence a wrapper rather than a
# second template: the version-dependent part is one line, and it belongs next to the reason.
set -euo pipefail

here=$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)
root=$(cd "$here/.." && pwd)

project=${1:?project directory}
version=${2:?editor version}

bash "$root/.github/smoke/pin-editor.sh" "$project" "$version"

case "$version" in
  2022.*) text_package='"com.unity.textmeshpro": "3.0.7"' ;;
  *)      text_package='"com.unity.ugui": "2.0.0"' ;;
esac

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
