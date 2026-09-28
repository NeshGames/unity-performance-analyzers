# AGENTS.md

Repository-wide instructions for coding agents maintaining **unity-performance-analyzers**.

This repository is maintained primarily through coding agents. Optimize for correctness,
small maintenance surfaces, reproducible evidence, and changes another agent can validate without
reconstructing intent from history.

## Scope

These instructions are for agents editing this repository. Agents using the package from a Unity
game should follow `skills/unity-performance-analyzers/SKILL.md` instead.

## Required checks

Run, in order:

```bash
dotnet build UnityPerformanceAnalyzers.sln -c Release
dotnet test UnityPerformanceAnalyzers.sln -c Release --no-build

dotnet run --project src/UnityPerformanceAnalyzers.RuleManifest -c Release --no-build -- --all .
git status --short -- README.md README.zh-TW.md package/Editor/rules.json \
  "package/Samples~/Ruleset Presets" sandbox/UnityProject/Assets/Default.ruleset

bash .github/smoke/analyzer-load.sh
```

The analyzer targets Unity 6. Its Roslyn dependency floor is 4.3.1; load smoke also exercises
Roslyn 4.10. Do not raise the dependency merely because a newer package exists. An analyzer that
references a newer Roslyn than the host may fail with CS8032 and then silently stop running.

Unity Editor / IL2CPP measurements live under `sandbox/` and require a real Unity install. When
you cannot run them, say so explicitly in the PR; never substitute a .NET benchmark for a Unity
performance claim.

## Design rules

- Analyzer instances hold no compilation-specific mutable state. Resolve compilation state inside
  a compilation-start callback and pass it into registered actions.
- Prefer false negatives over false positives when a diagnostic recommends a behaviour-changing
  rewrite.
- Every performance claim needs an IL2CPP measurement with a control before it is enabled in a
  shipping preset.
- Keep hot-path classification in `HotPathDetector`; individual rules must not grow private,
  slightly different definitions of "hot".
- Keep package/ecosystem detection in `UpaProfile`.
- Avoid IDE-only features. The primary consumers are Unity compilation, `upa-cli`, Claude Code,
  and Codex.
- Diagnostic messages must tell the caller what to do next. Do not rely on an IDE code fix.
- Rule IDs are never reused for a different meaning, even when a rule is retired.
- No descriptor defaults above Warning. CI rulesets may promote diagnostics to Error.

## Maintenance bias

This is a single-maintainer, agent-first tool. Backward compatibility with Unity versions older
than Unity 6 is not a goal. Prefer deleting obsolete compatibility branches and duplicate
configuration surfaces over preserving them for hypothetical users.

Before adding a new abstraction, option, preset, output format, release step, or document, ask
whether it removes more maintenance than it adds. A generated artifact is preferable to a second
hand-maintained source of truth.

## Tests

Add a test only when it protects a concrete regression or contract. Prefer extending an existing
test over duplicating the same invariant. For analyzer rules, cover the intended positive case,
the important false-positive boundary, and one outside-hot-path negative where applicable.

Repository-level contract tests are appropriate for silent failure modes such as analyzer loading,
generated-file drift, malformed agent skill metadata, or version skew. Do not keep tests for a
feature after the feature itself is removed.

## Changes and commits

Keep commits reviewable and behaviour-focused. Update `package/CHANGELOG.md` under
`[Unreleased]` for user-visible changes. Never hand-edit files marked generated; change the
source and regenerate them.
