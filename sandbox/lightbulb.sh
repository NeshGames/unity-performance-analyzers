#!/usr/bin/env bash
# Prepare sandbox/LightbulbProbe so the code fixes can be checked by hand in an IDE.
#
#   usage: sandbox/lightbulb.sh
#
# There is one thing about the code fixes that cannot be checked without a person: whether
# the lightbulb actually appears. The analyzers are verified in two editors by
# sandbox/verify.sh, and the fixes are verified by unit tests -- but both of those exercise
# Roslyn directly, and neither says whether Rider or Visual Studio loaded the code-fix
# assembly and offered the rewrite. That is what this project is for.
#
# It does not stamp ProjectVersion.txt, unlike sandbox/verify.sh. That script wants the
# project pinned so two editors can compile the same thing; this one wants Unity to treat it
# as a new project and fill in its own defaults -- including the IDE integration package for
# whichever editor opens it, without which no .csproj is generated and there is nothing for
# an IDE to load the analyzers from.
set -euo pipefail

here=$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)
root=$(cd "$here/.." && pwd)
project=$here/LightbulbProbe

echo "== building the analyzers and the code fixes"
dotnet build "$root/src/UnityPerformanceAnalyzers" -c Release >/dev/null
dotnet build "$root/src/UnityPerformanceAnalyzers.CodeFixes" -c Release >/dev/null

# Both assemblies, because the fixes live in the second one and its .meta carries the
# RoslynAnalyzer label that is the only reason Unity hands either of them to the compiler.
cp "$root/src/UnityPerformanceAnalyzers/bin/Release/netstandard2.0/UnityPerformanceAnalyzers.dll" \
   "$root/package/Analyzers/"
cp "$root/src/UnityPerformanceAnalyzers.CodeFixes/bin/Release/netstandard2.0/UnityPerformanceAnalyzers.CodeFixes.dll" \
   "$root/package/Analyzers/"

# The Traditional Chinese satellite. This script did not install it until 2026-08-10, and
# the omission was invisible in the worst way: whatever satellite happened to be sitting in
# package/Analyzers survived, so the probe showed Chinese diagnostics that were real but
# stale -- a translation from an earlier build, checked by a person, and read as current.
# An IDE running in Chinese is the only place this file is ever seen.
mkdir -p "$root/package/Analyzers/zh-Hant"
cp "$root/src/UnityPerformanceAnalyzers/bin/Release/netstandard2.0/zh-Hant/UnityPerformanceAnalyzers.resources.dll" \
   "$root/package/Analyzers/zh-Hant/"

# The template carries com.unity.ide.visualstudio, which JSON gives no way to explain in
# place. Unity only adds an IDE integration package to projects it creates itself; where a
# manifest already exists it leaves it alone, and without that package no .csproj or .sln is
# generated at all. "Open C# Project" then hands the IDE a path to nothing -- which is what
# happened on 2026-08-10, with a solution file three days stale and no sign of why.
# Both IDE packages are listed, so the probe opens in whichever editor is configured
# without a Package Manager detour. The versions are the ones Unity 2022.3.62f2 resolved
# on 2026-08-10; they are pinned rather than guessed because a version an editor cannot
# resolve leaves the project with no .csproj at all, which is the failure above.
cp "$project/Packages/manifest.template.json" "$project/Packages/manifest.json"

# The recommended preset, copied in the way the documentation tells a consumer to install it.
# Not every rule with a fix is on by default - UPA0009 is not - so the preset is doing real
# work here, and a probe that skips the step every real project takes is checking a setup
# nobody has.
cp "$root/package/Samples~/Ruleset Presets/recommended.ruleset" "$project/Assets/Default.ruleset"

# One override on top: UPA2012 is none in the recommended preset (it is an ecosystem rule,
# on only in cysharp-stack), and this probe exists to see every shipping fix. Raising the one
# rule is smaller than installing a whole different preset, which would also turn several
# rules up to error and stop the project compiling.
sed -i 's|<Rule Id="UPA2012" Action="None" />|<Rule Id="UPA2012" Action="Warning" />|'   "$project/Assets/Default.ruleset"
rm -f "$project/Packages/packages-lock.json"

echo
echo "Ready. Both assemblies are in package/Analyzers and the manifest points at them."
echo
echo "  1. Open $project in Unity Hub, with either supported editor."
echo "     Try 2022.3 first: its compiler is the older of the two, so if the code-fix"
echo "     assembly were built against a newer Roslyn than its host, that is where it shows."
echo "  2. Let the import finish, then Assets -> Open C# Project. Unity writes the .csproj"
echo "     and .sln and hands them to your IDE -- that is what carries the analyzers across."
echo "  3. Open Assets/LightbulbProbe.cs and follow $project/README.md."
