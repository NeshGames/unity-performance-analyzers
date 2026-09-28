# CLAUDE.md

Read `AGENTS.md` first. It is the canonical repository-wide contract for coding agents.

This file only adds Claude-specific context. Guidance for a Claude agent working in a Unity game
that *uses* this package lives in `skills/unity-performance-analyzers/SKILL.md`, not here.

## Commands

```bash
dotnet build UnityPerformanceAnalyzers.sln -c Release      # 0 warnings: warnings are errors
dotnet test  UnityPerformanceAnalyzers.sln -c Release --no-build

# Regenerate presets, README rule tables and package/Editor/rules.json; CI fails on any drift
dotnet run --project src/UnityPerformanceAnalyzers.RuleManifest -c Release --no-build -- --all .

# Load the built analyzer into the Unity 6 compiler range we support (Roslyn 4.3.1 / 4.10)
bash .github/smoke/analyzer-load.sh
```

The SDK is pinned by `global.json` (8.0 band). Nothing here needs Unity; `sandbox/` does, and
its scripts are for a maintainer with editors installed.

## Layout

| Path | What |
|---|---|
| `src/UnityPerformanceAnalyzers/` | The analyzers (netstandard2.0, **Roslyn 4.3.1 floor for Unity 6**: a newer dependency than the host compiler can silently stop the DLL loading) |
| `src/UnityPerformanceAnalyzers.Cli/` | `upa-cli`. Exit codes 0/1/2 and the JSON `schemaVersion` are published contracts |
| `src/UnityPerformanceAnalyzers.RuleManifest/` | Generators. `PresetTable.cs` is the source of truth for every preset |
| `src/UnityPerformanceAnalyzers.Tests/` | xUnit. Rule tests go through `RuleVerifier`; repository-reading tests use `TestRepository.Root` |
| `src/UnityStubs/` | Hand-written `UnityEngine`/`UnityEditor` stand-ins the tests compile against |
| `package/` | The UPM package. `Analyzers/*.dll` is placed by the release workflow — do not commit a DLL by hand |
| `docs/rules/` | One page per rule, English and `.zh-TW.md`, always both |
| `skills/`, `.claude-plugin/`, `.codex-plugin/` | The agent plugin for consumers. Plugin versions must equal `<Version>` in `src/Directory.Build.props` |

## Invariants the tests enforce

- **Analyzers:** derive from the shared base class; `SupportedDiagnostics` is a static readonly
  array; **no instance fields and no cache keyed by `Compilation`** (Roslyn reuses analyzer
  instances across compilations; a stale cache looks exactly like a correct answer). Caches keyed
  by an immutable `SourceText` are fine. File IO only through `AdditionalFiles`.
- **Every rule has:** a row in `AnalyzerReleases.Unshipped.md`, messages in
  `Resources/Strings.resx`, both doc pages, a README blurb in the RuleManifest, and a preset grade
  (or a listed deliberate absence). A new rule enters the presets one version after it ships.
- **Generated files are never hand-edited:** `package/Samples~/Ruleset Presets/*.ruleset`,
  `sandbox/UnityProject/Assets/Default.ruleset`, the `<!-- generated:... -->` README blocks, and
  `package/Editor/rules.json` (it carries the version, so a version bump means regenerating).
- XML comments in generated rulesets must not contain `--` — csc rejects the whole file (CS8035).

## Decisions that are not up to an agent

- **Rule ids** are allocated by the maintainer and never reused or re-meant.
- **No default severity above Warning.** Presets may use Error; defaults may not.
- **Every performance claim needs an IL2CPP measurement** with a control. Reasoning from IL or
  .NET behaviour is not evidence. Rules whose premise stops holding are deprecated, not kept.
- **No IDE-only features.** Code fixes, IDE translations and IDE-only `.editorconfig` presets
  were removed deliberately: consumers' code is written by agents that read the Unity compile,
  the Unity CLI and `upa-cli`. Put the fix in the diagnostic message and the rule page instead.
- **Messages are the fix.** A message states the problem and what to write instead.

## Tests

A test earns its place when a credible regression fails it and no stronger test already would.
Before adding one: know what behaviour it protects, check it fails on the old code for the
intended reason, and prefer extending an existing case. Two traps specific to this repo:

- The analyzer testing framework enables **every** descriptor of the analyzer under test, so a
  test cannot observe a rule being off by default — that is pinned by release tracking and
  `VersioningPolicyTests`, not by rule tests.
- Hot-path classification is owned by `HotPathDetectorTests`; a rule's own tests need one
  "outside the hot path" negative, not a replay of the classifier.

## Consumers drive Unity through the Unity CLI

Keep that in mind for anything user-facing: an Error-severity finding fails Unity's compile, and an
Editor launched on a project that does not compile opens in Safe Mode, where `unity command` cannot
connect. That is why the docs steer agent workflows to `recommended` in Unity and the stricter
presets in `upa-cli` gates.

## Commits

Say what changed and what it costs the reader, not which files moved. Comments explain why, where a
reader would otherwise assume the obvious thing was overlooked. Update `package/CHANGELOG.md` under
`[Unreleased]` for anything a user would notice.
