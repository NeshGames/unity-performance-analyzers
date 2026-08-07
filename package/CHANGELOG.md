# Changelog

All notable changes to this package will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [0.2.0] - 2026-08-07

### Added

- **Rule Manager window** (`Tools > Unity Performance Analyzers > Rule Manager`):
  per-rule severity editing of `Assets/Default.ruleset` (UPA rules plus a
  Microsoft.Unity.Analyzers foldout), one-click preset apply, a WebGL toggle that
  manages the `UPA_TARGET_WEBGL` define across all build targets together with the
  webgl-addon Include, a read-only list of per-asmdef ruleset overrides, and an
  Options tab. The window preserves entries of other analyzers, Includes and comments.
- **Universal options file** `Assets/Rules.UnityPerformanceAnalyzers.additionalfile`:
  every analyzer option (`upa_hot_path_*`, `upa_enum_switch_allow_default`) now takes
  effect in Unity builds as well as IDE analysis. Per key, the options file wins over
  `.editorconfig`, which wins over built-in defaults; parsing tolerates comments,
  unknown keys and malformed lines.
- **UPA3004** (WebGL group): blocking waits on asynchronous operations —
  Addressables `AsyncOperationHandle.WaitForCompletion`, `Task.Wait`/`WaitAll`/`WaitAny`,
  `Task<TResult>.Result` and the `GetAwaiter().GetResult()` idiom — deadlock
  single-threaded WebGL players. Enabled at warning by `webgl-addon.ruleset`.
- Rule catalog `Editor/rules.json` shipped with the package (regenerated on release),
  backing the Rule Manager window.

### Changed

- **Breaking: UPA2001 renamed to UPA0013.** The hot-path LINQ rule no longer depends on
  any ecosystem package and moved to the performance group. Update ruleset entries,
  `.editorconfig` lines and `#pragma` suppressions; the UPA2001 ID is permanently
  retired. Preset severities for the rule are unchanged.
- **Breaking: UPA2011 now runs only when the assembly references UniTask**, and its
  advice names the rewrite (`async UniTask` / `UniTaskVoid`) instead of staying
  neutral — without UniTask there was no allocation-free replacement to suggest.

## [0.1.0] - 2026-08-07

### Added

- Initial rule set UPA0001–UPA0012, UPA1000, UPA1001 (performance and correctness rules).
- Ecosystem rules UPA2000, UPA2001, UPA2010, UPA2011, UPA2012, UPA2021 — advice and
  activation adapt to referenced packages (UniTask, ZString, R3), and UPA2000/2001
  report on per-frame hot paths only.
- WebGL platform rules UPA3000–UPA3003, active when `UPA_TARGET_WEBGL` is defined.
- Severity presets (minimal / recommended / strict / cysharp-stack, plus the webgl-addon
  and editor-relaxed rulesets) with `.editorconfig` variants for IDE parity, and a
  Smoke Test sample.
- Hot-path detection configurable via `upa_hot_path_*` options (IDE analysis only).
- Every diagnostic carries a `snippet` property for stable violation identification in
  downstream tooling.
- Documentation in English and Traditional Chinese.
