# Load smoke tests

An analyzer can fail in a way that no build notices. If it was built against a newer
Roslyn than the compiler hosting it, the host cannot create it, reports `CS8032`, and
carries on compiling as though no analyzer had been passed. The build is green, the
package ships, and every rule silently stops running. Unity has its own version of the
same failure: it hands a DLL to the compiler only when the `.meta` beside it carries the
`RoslynAnalyzer` label, and a regenerated `.meta` loses that label without an error
anywhere.

Both failures look exactly like a codebase with nothing to report. These tests are here
to tell the two apart.

## Two layers

**Pinned compilers** — runs on every pull request, takes seconds, needs no Unity.
`analyzer-load.sh` downloads the exact C# compilers the supported Unity versions ship
(listed in `toolsets.proj`), compiles the probe against each with both analyzer DLLs
loaded, and asserts on the diagnostics. It also checks the `.meta` labels.

**Unity itself** — runs before a release tag is cut. `unity-load.sh` installs the
assembled package into a throwaway project, compiles it in batch mode, and applies the
same assertions to Unity's log. This is the only check that goes through Unity's own
import pipeline: the package layout, the `.meta` labels, the ruleset lookup and the
compiler arguments Unity assembles are all Unity's, and none of them are exercised by
invoking a compiler directly.

## The assertions

Both layers share `assert-diagnostics.sh`, which requires all four:

1. no `CS8032`, `CS8033` or `AD0001` — nothing failed to load and nothing threw
2. every rule `Probe.cs` is marked to trigger was reported
3. `NoTrigger.cs` reported nothing
4. the probe compiled

The expected rule IDs are read from `Probe.cs`'s own `// expect` markers, so the probe
and the assertions cannot drift apart. The negative file is what keeps the positive
assertions meaningful: "every rule fires on everything" would satisfy them on its own.

The pinned-compiler layer adds a control run with no analyzer passed, and requires the
rule IDs to disappear. Without it, an assertion that matched on something other than a
live diagnostic would pass forever and mean nothing.

## Running them yourself

```bash
dotnet build UnityPerformanceAnalyzers.sln -c Release
bash .github/smoke/analyzer-load.sh
```

For the Unity layer, build first, copy the two DLLs into `package/Analyzers/`, then point
the script at an editor:

```bash
bash .github/smoke/unity-load.sh "/path/to/Unity" 2022.3.62f2
```

`unity-load.sh` reports three outcomes, and the difference matters to the release
workflow: `0` pass, `1` the project compiled and the result was wrong, `2` the project
never compiled, so nothing about the package was tested. `release-gate.sh` acts on those:
a package failure blocks a release outright, while an infrastructure failure can be
released past only with a stated reason, which is then recorded on the release commit.

`assert-selftest.sh` and `release-gate-selftest.sh` check that those checks actually
fail — a safety net that reports success regardless is worse than none, because the
success is what people act on.
