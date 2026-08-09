# Changelog

All notable changes to this package will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [0.7.0] - 2026-08-09

Three version lines of work released as one. 0.7.0 and 0.8.0 were developed and their code
went to the default branch, but neither was ever tagged, so nothing was ever installable
under those numbers; collapsing them costs nobody a version they had.

### Added

- **UPA0031 — `Instantiate` or `Destroy` in a per-frame method.** One id with two messages:
  creating and discarding objects every frame is one decision, and the answer to both halves
  is a pool. Matching is on the method symbol rather than on how the call is written, because
  inside a `MonoBehaviour` the usual line is `Instantiate(prefab)` with no receiver at all.

- The code fix assembly now ships with the package, so the fixes for UPA0019, UPA0021 and
  UPA0022 reach the IDE instead of existing only in the repository. Verified on Unity
  2022.3 and Unity 6: both assemblies load as analyzers with no CS8032, and the bulbs were
  confirmed by hand in the Editor — the rewrites they preview are the ones documented.

- **Baselines**: `upa-cli --write-baseline <path>` records what a project violates today,
  and `--baseline <path>` reports only what comes after. An existing project can then fail
  CI on new violations without first fixing every old one. The file is meant to be
  committed — it is a contract the team shares, not a local cache. Writing one needs
  `--whole-assembly`, and is refused when an analyzer failed or the project did not
  compile, since what would be frozen otherwise is a run that never saw the code.

- **Arguments can come from a file**: `upa-cli @args.rsp`, one argument per line. Using the
  tool as a gate means passing an assembly's whole source list, its defines, and a
  reference per package — tens of thousands of characters, where a Windows command line
  stops at 32,767. Until now the documented setup could not be expressed on Windows at all.

- **Compile errors are listed, not just counted.** They are still not findings and still do
  not count toward `--fail-on`, but a run refused for compile errors used to report only
  how many there were, which left nothing to act on: under `--whole-assembly` those errors
  are fatal and a baseline cannot be written, and knowing *which* type failed to resolve is
  the whole difference between fixable and not. The text output lists the first twenty and
  counts the rest; JSON gains a `compileErrors` array with all of them.

- Every rule's specification now declares the assumptions it makes, against a fixed
  six-category checklist. This is internal, but it produced two missing tests and one real
  defect (the UPA0009 change below), so it is worth knowing it happened.

### Changed

- **UPA0009 reports less, on purpose.** It advises hoisting `Count` out of a loop, which is
  only correct while the collection does not change, and it decided that by comparing
  receiver names in the loop body -- so an alias, or the collection passed to a method that
  mutates it, was invisible. Following the advice would then break the program, which is
  worse than a false positive. It now stays quiet whenever the loop body could reach the
  collection other than by reading it. Element assignment (`list[i] = x`) still reports: it
  cannot change how many items there are.

- **UPA2000 reports at warning in the `recommended` preset** (was `none`). The rule is
  deliberately not conditional on ZString so that projects without it still hear about
  hot-path string building — and leaving it off in the everyday preset meant those
  projects heard nothing. Expect new warnings on existing code; `Rule Manager` can lower
  it per project.

- The rule tables and rule count in both READMEs are generated from the analyzer assembly
  and checked on every pull request. They were maintained by hand and had drifted.

- The option list in `rules.json` and the commented defaults in every preset
  `.editorconfig` are generated from one declaration instead of three hand-kept copies. The
  preset comments now show each option's real default, which is longer for
  `upa_hot_path_messages` and adds the two options that were missing entirely.

### Fixed

- **Two options only ever worked in the IDE.** `upa_shader_property_hot_path_only`
  (UPA0003) and `upa_log_wrapper_types` (UPA0005) read `.editorconfig` directly instead of
  going through the layered lookup, and Unity does not pass `.editorconfig` to the
  compiler. Setting either one changed what your IDE showed and nothing about what your
  build reported. Both now read the options file first, like every other option. If you had
  set them and worked around them doing nothing, they will start taking effect.

- **`.editorconfig` sections now apply per file for UPA0003.** The option was read once for
  the whole compilation, from whichever source file happened to be first, so a section
  scoped to one folder silently applied to all of them or to none.

- The README claimed 41 rules; the package has 45.

- Every documented example of `upa_hot_path_attributes` named `PerfCritical`, an attribute
  the analyzers have never recognised. The second built-in name is `PerformanceCritical`.
  Because the option *replaces* the default set rather than adding to it, anyone who copied
  the line out of a rule page or a preset comment turned off `[PerformanceCritical]`
  detection and got no indication that they had.

### Note on the command line

`upa-cli` is built from this repository rather than installed from NuGet. A package id, a
command name, and every version pushed under them are permanent, so that channel opens at
1.0. The release attaches a `.nupkg` for anyone who wants to install it from a local source.

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
- **UPA0029** — sequential `Add` replaceable with `AddRange`. Only reported when the
  source implements `ICollection<T>`, which is what lets `AddRange` pre-size; over a plain
  `IEnumerable<T>` it adds one at a time and there is nothing to gain. Reported without an
  automatic fix on purpose: two distinct references can point at the same list at runtime,
  where the loop throws or never terminates and the rewrite would quietly do neither.
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
  *(Correction, 0.7.0: these were built but never shipped — the package carried only the
  analyzer assembly until 0.7.0. Installing 0.3.0 through 0.6.0 gave you the diagnostics
  without the fixes.)*

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
