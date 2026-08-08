# Changelog

All notable changes to this package will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [0.6.0] - 2026-08-08

Four rules for allocations that are invisible at the call site — the cost hides in
overload resolution, in the comparer a collection picks, or inside a BCL method. Every
entry in the new lists, and every replacement they recommend, was measured in a Unity
sandbox across Mono and IL2CPP on 2022.3 and Unity 6 before shipping.

### Added

- **UPA0027** — params array allocated at call site. Expanded-form `params` calls create
  an array per call with nothing in the source to say so; `Mathf.Max(a, b, c)` is the
  common case, since Unity ships arity-2 overloads only. `params object[]` additionally
  boxes each value-type argument, reported with a separate message that counts them.
- **UPA0028** — value type used as collection key without `IEquatable<T>`. A struct key
  without both `IEquatable<T>` and a `GetHashCode` override falls to a comparer that boxes
  both operands per comparison, and to reflection when `Equals` is not overridden either.
  Not hot-path scoped: the cost is the same wherever the lookup happens.
- **UPA0029** — sequential `Add` replaceable with `AddRange`, with a code fix. Only
  reported when the source implements `ICollection<T>`, which is what lets `AddRange`
  pre-size; over a plain `IEnumerable<T>` it adds one at a time and there is nothing to
  gain.
- **UPA0030** — known-allocating BCL API in per-frame method. A closed list of `string`
  and `Enum` members that allocate on every call. Note that `Trim` returns the original
  instance when there is nothing to trim, and so may cost nothing at runtime.

### Changed

- **UPA0018** now covers methods as well as properties, gaining
  `Animator.GetCurrentAnimatorClipInfo(int)` and the `Texture2D` pixel readers. The
  generic `GetRawTextureData<T>()` and the `List` overload of `GetCurrentAnimatorClipInfo`
  are the recommended replacements and are never reported themselves.
- **UPA0006** no longer reports the boxing of value-type arguments inside a `params`
  expansion. That allocation belongs to UPA0027, which names the call being made; before
  this change one allocation would have produced two diagnostics. Boxing anywhere else is
  unaffected.
- The `editor-relaxed` presets keep UPA0028 at its normal severity instead of switching it
  off with the rest. Relaxation exists because per-frame cost does not matter in editor
  tooling, and that rule is not about per-frame cost.

### Documentation

- [Enum dictionary keys](https://github.com/NeshGames/unity-performance-analyzers/blob/main/docs/rules/enum-dictionary-keys.md)
  — the widely repeated advice that enum dictionary keys box no longer holds. Measurement
  across five Mono and IL2CPP combinations shows the runtime has dedicated non-boxing
  comparers for enums. The same measurements show the cost is real for struct keys, which
  is what UPA0028 reports. No rule number is assigned; the page explains why.

## [0.5.0] - 2026-08-08

Rule behavior is unchanged from 0.4.0; this release adds tooling around the analyzers.

### Added

- **`upa-cli`** (`src/UnityPerformanceAnalyzers.Cli`): runs every rule outside Unity in
  under a second, for CI jobs and quick local checks. Analyzes any set of files with
  `--reference <assembly>` to simulate an installed package, `--define` for platform
  rules, `--ruleset`/`--editorconfig`/`--additionalfile` for configuration, and
  `--format json` for machine-readable output. Exit codes: `0` clean, `1` diagnostics at
  or above `--fail-on` (default `warning`), `2` usage or execution error.
  `--list-rules` prints the catalog straight from the loaded analyzer assembly.
  Rules that judge a whole assembly are skipped unless `--whole-assembly` is passed, so
  a partial file set cannot produce a false report; see the README for the other
  approximations this trades for speed.

### Changed

- PR CI now fails when the package version regresses below the newest release tag, or
  when the build version trails it.

## [0.4.0] - 2026-08-08

Internal architecture release: rule behavior, severities, and diagnostic messages are
unchanged from 0.3.0.

### Added

- `editor-relaxed.editorconfig` — the IDE-parity twin the ruleset always had.

### Changed

- All severity presets (and the sandbox verification ruleset) are now generated from a
  single severity table and carry a "generated file" header; CI regenerates them and
  fails on drift, so presets can no longer rot by hand-editing.
- Rule metadata (hot-path scope, ecosystem/WebGL activation conditions) is declared as
  attributes on the analyzers themselves and read by reflection when the rule catalog
  (`Editor/rules.json`) is generated.
- Analyzer internals share one Initialize skeleton, one base-type walk, and one
  descriptor factory; release notes and string resources are guarded by bidirectional
  consistency tests.

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
