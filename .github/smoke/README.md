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

**Unity itself** — runs before a release tag is cut, on a developer machine. `unity-load.sh`
installs the assembled package into a throwaway project, compiles it in batch mode, and
applies the same assertions to Unity's log. This is the only check that goes through Unity's
own import pipeline: the package layout, the `.meta` labels, the ruleset lookup and the
compiler arguments Unity assembles are all Unity's, and none of them are exercised by
invoking a compiler directly.

This layer is deliberately not run in CI. Doing so would mean storing a `.ulf` here, and
that file is a credential bound to a Unity account rather than to this project - anyone
with write access could read it back out of a workflow. The script takes the editor path
as an argument for exactly this reason, so the check is kept and only its location moves.
The release workflow asks for the result in its `local_smoke` input and records it on the
release commit; it runs the Unity jobs itself only if a `UNITY_LICENSE` secret is present,
so nothing has to be rewritten if that ever changes.

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

Run it once per version in `unity-versions.json` before releasing, and state every one of
them in the release workflow's `local_smoke` input.

`unity-load.sh` reports three outcomes, and the difference matters to the release
workflow: `0` pass, `1` the project compiled and the result was wrong, `2` the project
never compiled, so nothing about the package was tested. `release-gate.sh` acts on those.
A package failure blocks a release outright and nothing gets past it. Everything else
needs one of two statements, kept apart because they are not the same claim:

- `local_smoke` — the check was run, elsewhere. Every supported version must be named
  *and reported as passing*, in exactly this shape and nothing else:

  ```
  2022.3.62f2=pass 6000.5.3f1=pass
  ```

  Whitespace, commas and semicolons separate; spaces around the `=` are fine; case is not
  significant. Anything that is not a `<version>=pass` token rejects the whole value.

  The value is parsed against that, not searched for a pass inside it, and pasting the
  script's own output is not accepted. Searching kept almost working: `2022.3.62f2 failed`
  holds the version, `NOT PASS [Unity 2022.3.62f2]` holds the pass line,
  `2022.3.62f2=pass=false` holds the token and then takes it back, and
  `PASS [Unity 2022.3.62f2] UPA9999 reported, PACKAGE_FAILED silent, no loader failures`
  holds every part the pattern asked for. Free text can always be arranged to carry a pass
  inside a report of failure, and any field left open is somewhere to write one. This form
  leaves none open: a token either equals a supported version followed by `=pass`, or the
  statement is refused.
- `smoke_override` — the check was not run, and here is why. This one says on the release
  commit that the package is untested against Unity.

`assert-selftest.sh` and `release-gate-selftest.sh` check that those checks actually
fail — a safety net that reports success regardless is worse than none, because the
success is what people act on.
