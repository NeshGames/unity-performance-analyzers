# Changelog

All notable changes to this package will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [0.3.0] - 2026-08-08

### Added

- **Thirteen performance rules UPA0014–UPA0026**: scene-search APIs on hot paths
  (UPA0014), `Camera.main` on hot paths (UPA0015, Info), `SendMessage`/`BroadcastMessage`
  anywhere (UPA0016), array-returning `GetComponents` overloads (UPA0017), an
  allocating array-returning API deny-list — `Input.touches`, `Animator.parameters`,
  `Renderer.sharedMaterials`, `Camera.allCameras` (UPA0018), boxed coroutine yields
  (UPA0019), lambdas in `WaitUntil`/`WaitWhile` construction (UPA0020, off by default),
  `magnitude`/`Distance` threshold comparisons (UPA0021), `Enum.HasFlag` on hot paths
  (UPA0022), `OnGUI` in player code (UPA0023, Info, off by default), `Resources.Load`
  on hot paths (UPA0024, off by default), finalizers in runtime code (UPA0025), and
  value types boxed by inherited `ToString`/`GetHashCode`/`Equals(object)`/`GetType`
  calls (UPA0026).
- **DOTween-conditional rules UPA2030–UPA2032** (active only when the project
  references the `DOTween` assembly): tween creation in per-frame methods (UPA2030),
  discarded infinite tweens without `SetLink` (UPA2031), and string tween IDs
  (UPA2032, Info). All three are off by default; recommended and stricter presets
  enable UPA2030/2031.
- **First code fixes** (IDE-only): `yield return <boxed value>` → `yield return null`
  (UPA0019), squared-threshold rewrite for `magnitude`/`Distance` comparisons against
  numeric literals (UPA0021), and `x.HasFlag(y)` → `(x & y) == y` (UPA0022).

### Changed

- **UPA0006 now reports boxing in string interpolation holes** (`$"hp {hp}"` with a
  value-type hole). The boxing happens when the compiler lowers the interpolation to
  `string.Format` and was previously invisible to the rule; string-typed holes still
  belong to UPA2000.
- **UPA0023/UPA0025 skip editor assemblies by name** (`Assembly-CSharp-Editor` or a
  `.Editor` suffix). Unity injects UnityEditor references into every editor-domain
  compilation, so reference-based detection would disable these rules exactly where
  they should run.
- All presets gained entries for the sixteen new rules; `editor-relaxed.ruleset` now
  also silences UPA3004 and the DOTween group.

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
