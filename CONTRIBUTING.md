# Contributing

[繁體中文](CONTRIBUTING.zh-TW.md)

The most valuable thing you can send this project is **a rule that fired on correct code**.
An analyzer's worth is decided by its false-positive rate, and that rate is only visible
from projects that are not this one. A repro snippet is worth more here than a patch.

## What you need

- .NET SDK 8.0 — everything but the sandbox builds and tests without Unity
- Unity 2022.3 LTS or Unity 6, only if you are touching `sandbox/` or verifying in-editor

```bash
dotnet build -c Release
dotnet test
```

Both must be green before anything else is worth reading. Tests run against a minimal
`UnityEngine` stand-in (`src/UnityStubs/`), so a real Unity installation is not needed.

---

## Reporting a false positive

Open an issue with the **False positive** template. What makes one quick to fix:

- the rule ID and the line it fired on,
- **the smallest snippet that still triggers it** — this is the part that decides how fast
  it gets fixed,
- the Unity version, and whether the assembly is Editor or player code,
- which packages that assembly references (UniTask, ZString, R3, DOTween), since several
  rules only exist when one of them is present,
- any `upa_*` options you set,
- what you expected instead: no report at all, or a report somewhere else.

The CLI reproduces it outside Unity in one line, which is usually faster than screenshots
and gives an exact quotable result:

```bash
dotnet run --project src/UnityPerformanceAnalyzers.Cli -c Release -- \
  Assets/Scripts/Thing.cs --all-warn --format json
```

A confirmed false positive is a patch release. Until it ships, `#pragma warning disable
UPA####` or a ruleset entry silences it, and neither becomes wrong later — a rule ID never
changes meaning. See [versioning and rule governance](docs/versioning.md).

---

## Proposing a rule

Rules here carry an evidence bar that most analyzer projects do not have:

> **Every performance claim must survive measurement on IL2CPP.** Reasoning from IL
> semantics, from .NET behaviour, or from what an optimization "should" do is not evidence.
> Mono numbers are recorded for contrast; IL2CPP decides, because that is what ships.

So a rule proposal needs, at minimum:

1. **The pattern**, as code, and what you would write instead
2. **What it costs**, measured — allocation or time, on IL2CPP, with the alternative as a
   control. A measurement with no control cannot tell "this is free" from "we measured the
   wrong thing"
3. **How often it is wrong** — where the pattern is legitimate, and how the rule avoids
   firing there

The bar exists because a rule whose premise has expired is worse than no rule: it asks for
a change that buys nothing, and spends the reader's attention every time it fires. Version
0.8.0 retired two rules and narrowed two more for exactly that reason.

If you have the pattern but not the measurement, open the issue anyway and say so. The
measurement is work this project can do; knowing what to measure is the harder half.

---

## Adding a rule

Rule numbers are allocated by the maintainer — an ID is permanent once it ships, so it is
not something a pull request should pick. Open a rule proposal first, then:

**1. The analyzer** — `src/UnityPerformanceAnalyzers/UPA####Something.cs`

Inherit the shared base class rather than `DiagnosticAnalyzer` directly; it builds the
per-compilation context (profile, hot-path classification, type lookups) and hands it to
your `InitializeCore`. Three constraints matter and are enforced by tests:

- `SupportedDiagnostics` is a `static readonly ImmutableArray`, not rebuilt per call
- no instance fields, and no cache keyed by `Compilation` — Roslyn serves several
  compilations from one analyzer instance, and a stale cache produces output that looks
  exactly like the correct answer
- no file IO except through `ctx.Options.AdditionalFiles`

**2. Release tracking** — add the row to
`src/UnityPerformanceAnalyzers/AnalyzerReleases.Unshipped.md`. The build fails without it,
and fails again if you later change the rule's severity without recording that too.

**3. Tests** — `src/UnityPerformanceAnalyzers.Tests/UPA####SomethingTests.cs`, at least
four: it fires where it should, it stays quiet where it should not, one boundary case, and
the code fix if there is one. Assert positions with inline markup (`{|UPA####:...|}`),
never a hand-written span.

**4. Both documentation pages** — `docs/rules/UPA####.md` and `docs/rules/UPA####.zh-TW.md`.
A test asserts both exist, that they agree with the descriptor about severity and default
state, that they link to each other, and that they agree about whether a code fix exists.

**5. The README tables** — generated, not hand-edited:

```bash
dotnet run --project src/UnityPerformanceAnalyzers.RuleManifest -c Release -- --readme .
```

The one-line summary is curated in `src/UnityPerformanceAnalyzers.RuleManifest/`; a rule
with no entry fails the build rather than rendering an empty cell.

**6. The presets** — also generated, from the same table:

```bash
dotnet run --project src/UnityPerformanceAnalyzers.RuleManifest -c Release -- --presets .
```

Every rule must be graded by a preset or listed as deliberately absent. A new rule enters
the presets one version after it ships, so nobody's build starts failing on a rule they
have not read about yet.

**7. A code fix, if the change is mechanical** —
`src/UnityPerformanceAnalyzers.CodeFixes/`. Only offer one when the rewrite is provably
equivalent. A fix that changes behaviour in a case you did not think of is worse than no
fix, because it is applied without being read.

Then `dotnet build -c Release && dotnet test`. The meta-tests will tell you which of the
seven steps you skipped.

---

## Style

- Code, identifiers, commit messages and `docs/rules/*.md` are in English
- Diagnostic messages live in `Resources/Strings.resx`, never inline
- Comments explain **why**, and are worth writing where a reader would otherwise assume the
  obvious thing was overlooked. Comments that restate the code are noise
- Commit messages say what changed and what it costs the reader, not which files moved

## What gets rejected

- A rule with no measurement behind its performance claim
- A rule whose default severity is above Warning — nothing in this package decides on its
  own that a build should fail
- A code fix that is right in the common case and wrong in an uncommon one
- Vendoring another analyzer package, or copying another project's rule text

## Security

Report vulnerabilities privately through GitHub's security advisory form rather than in a
public issue. See [SECURITY.md](SECURITY.md).
