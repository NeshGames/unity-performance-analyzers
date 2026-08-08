# unity-performance-analyzers

> English | [繁體中文](README.zh-TW.md)

Roslyn analyzers that turn Unity performance and correctness conventions into
compile-time checks. Rules adapt automatically to the packages each assembly references
(UniTask, ZString, R3, DOTween) and to whether the project targets WebGL.

Distributed as a UPM package. Supports **Unity 2022.3 LTS through Unity 6**.

> **Status: pre-1.0.** All 41 rules are implemented and verified against
> Unity 2022.3 and Unity 6 sandbox builds. Rule IDs are stable — once released,
> an ID is never reused.

## Install

Package Manager > *Add package from git URL…*:

```
https://github.com/NeshGames/unity-performance-analyzers.git?path=/package#v0.5.0
```

The analyzer applies to **every assembly in the project** automatically — no asmdef
references needed (the package deliberately contains no asmdef; adding one would narrow
the analyzer's scope to referencing assemblies only).

Then pick a severity preset (below). Without one, only the rules that are enabled by
default report, at Warning.

## Severity presets

Import the **Ruleset Presets** sample from the Package Manager window, then copy one
preset to `Assets/Default.ruleset`:

| Preset | Intent |
|---|---|
| `minimal` | Unity correctness rules (`UNT` group) as errors; UPA1001 stays at its default warning — a safe first install |
| `recommended` | + UPA performance rules as warnings. The everyday default |
| `strict` | Performance rules become errors; opinionated rules start reporting |
| `cysharp-stack` | + ecosystem rules as errors (UniTask/ZString/R3 adoption) |

Extras in the same sample:

- `webgl-addon.ruleset` — stacks UPA3000–3004 onto any preset via ruleset `<Include>`;
  requires the `UPA_TARGET_WEBGL` scripting define (see the sample README)
- `editor-relaxed.ruleset` — drop into Editor asmdef folders as `Default.ruleset` to
  silence performance rules in tooling code
- `.editorconfig` variants of each preset for Rider / Visual Studio parity

Unity reads rulesets only — it does not pass `.editorconfig` to the compiler (verified
on 2022.3 and Unity 6). A `Default.ruleset` inside an asmdef folder overrides the
project-wide one for that assembly.

Import the **Smoke Test** sample to verify the analyzer is loaded: it violates several
rules on purpose and should light up the Console immediately.

## Rule Manager window

**Tools ▸ Unity Performance Analyzers ▸ Rule Manager** manages all of the above without
hand-editing XML:

- **Rules tab** — a severity dropdown for every UPA rule (grouped by category, with
  condition badges) plus a Microsoft.Unity.Analyzers foldout; one-click preset apply
  (read straight from the package, no sample import needed); and a WebGL toggle that
  maintains the `UPA_TARGET_WEBGL` define across **all** build targets together with the
  `webgl-addon.ruleset` Include. Per-asmdef ruleset overrides are listed read-only.
- **Options tab** — edits the universal options file (next section), with an optional
  mirror into `.editorconfig`.

The window edits `Assets/Default.ruleset` conservatively: entries belonging to other
analyzers, `<Include>` lines and comments are preserved as-is.

## Analyzer options (universal options file)

`Assets/Rules.UnityPerformanceAnalyzers.additionalfile` carries every analyzer option in
`key = value` form and is honored by **Unity builds and IDE analysis alike** — Unity
passes additional files to the compiler, which it never does for `.editorconfig`.
Resolution is per key: the options file wins over `.editorconfig`, which wins over the
built-in defaults.

```ini
upa_hot_path_messages = Update,FixedUpdate,LateUpdate,OnTriggerEnter
upa_hot_path_attributes = HotPath,PerfCritical
upa_hot_path_include_lambdas = true
upa_enum_switch_allow_default = true
```

| Key | Type | Default | Effect |
|---|---|---|---|
| `upa_hot_path_messages` | comma-separated list | `Update`, `FixedUpdate`, `LateUpdate`, `OnGUI`, `OnAnimatorMove`, `OnAnimatorIK`, `OnPreCull`, `OnPreRender`, `OnPostRender`, `OnRenderObject`, `OnWillRenderObject`, `OnRenderImage`, `OnTriggerStay`, `OnTriggerStay2D`, `OnCollisionStay`, `OnCollisionStay2D`, `OnParticleUpdateJobScheduled` | Which Unity messages on MonoBehaviour types count as per-frame. **Replaces** the default set — list every message you want, including the standard ones. Governs all hot-path rules |
| `upa_hot_path_attributes` | comma-separated list | `HotPath`, `PerformanceCritical` | Attribute short names that mark any method as a hot path, matched by name with the `Attribute` suffix optional. Lets non-message methods opt in |
| `upa_hot_path_include_lambdas` | `true` / `false` | `true` | Whether lambdas and local functions declared inside a hot-path method count as hot. Set `false` if you invoke them elsewhere |
| `upa_enum_switch_allow_default` | `true` / `false` | `true` | For UPA1001, whether a `default` branch (or discard arm) counts as covering the remaining members |

Parsing is tolerant by design: `#` comments, unknown keys and malformed lines are
ignored, an invalid value falls through to the next channel, and a duplicated key keeps
its last value. The Rule Manager's Options tab edits this file for you.

## Rules

Full documentation per rule: [`docs/rules/`](docs/rules/).

### Performance (on by default unless noted)

| ID | Reports | Hot-path only |
|---|---|---|
| [UPA0001](docs/rules/UPA0001.md) | `GetComponent` family called in per-frame methods | ✓ |
| [UPA0002](docs/rules/UPA0002.md) | `name` / `tag` accessed in per-frame methods | ✓ |
| [UPA0003](docs/rules/UPA0003.md) | String-based shader/animator property access | |
| [UPA0004](docs/rules/UPA0004.md) | Instantiating accessors (`Renderer.material`, …) in per-frame methods | ✓ |
| [UPA0005](docs/rules/UPA0005.md) | Direct `Debug.Log` calls *(off by default)* | |
| [UPA0006](docs/rules/UPA0006.md) | Reference-type allocation / boxing in per-frame methods | ✓ |
| [UPA0007](docs/rules/UPA0007.md) | Capturing lambdas in per-frame methods | ✓ |
| [UPA0008](docs/rules/UPA0008.md) | `stackalloc` inside a loop | |
| [UPA0009](docs/rules/UPA0009.md) | `List<T>.Count` not hoisted out of `for` loops *(off by default)* | ✓ |
| [UPA0010](docs/rules/UPA0010.md) | Raycasts without explicit `maxDistance` / `layerMask` | |
| [UPA0011](docs/rules/UPA0011.md) | `SetActive` used to toggle UI visibility *(off by default)* | |
| [UPA0012](docs/rules/UPA0012.md) | TMP `text` assignment instead of `SetText` *(off by default)* | ✓ |
| [UPA0013](docs/rules/UPA0013.md) | `System.Linq` calls in per-frame methods *(off by default; formerly UPA2001)* | ✓ |
| [UPA0014](docs/rules/UPA0014.md) | Scene-search APIs (`GameObject.Find`, `FindObjectOfType`, …) in per-frame methods | ✓ |
| [UPA0015](docs/rules/UPA0015.md) | `Camera.main` in per-frame methods *(Info)* | ✓ |
| [UPA0016](docs/rules/UPA0016.md) | `SendMessage` / `SendMessageUpwards` / `BroadcastMessage` calls | |
| [UPA0017](docs/rules/UPA0017.md) | Array-returning `GetComponents` overloads (use the `List<T>` overloads) | ✓ |
| [UPA0018](docs/rules/UPA0018.md) | Allocating array-returning APIs (`Input.touches`, `Animator.parameters`, `Renderer.sharedMaterials`, `Camera.allCameras`) | ✓ |
| [UPA0019](docs/rules/UPA0019.md) | Value types yielded from coroutines (boxing; Unity treats them as `null`) | |
| [UPA0020](docs/rules/UPA0020.md) | Lambdas in `WaitUntil` / `WaitWhile` construction *(off by default)* | |
| [UPA0021](docs/rules/UPA0021.md) | `magnitude` / `Distance` compared where `sqrMagnitude` suffices | |
| [UPA0022](docs/rules/UPA0022.md) | `Enum.HasFlag` in per-frame methods (boxes on Unity's Mono) | ✓ |
| [UPA0023](docs/rules/UPA0023.md) | `OnGUI` declared in player code *(Info, off by default)* | |
| [UPA0024](docs/rules/UPA0024.md) | `Resources.Load` in per-frame methods *(off by default)* | ✓ |
| [UPA0025](docs/rules/UPA0025.md) | Finalizers declared in runtime code | |
| [UPA0026](docs/rules/UPA0026.md) | Value types boxed by inherited `ToString` / `GetHashCode` / `Equals(object)` / `GetType` calls | ✓ |
| [UPA0027](docs/rules/UPA0027.md) | `params` overloads called in expanded form, which allocate an array per call | ✓ |
| [UPA0028](docs/rules/UPA0028.md) | Structs used as collection keys without `IEquatable<T>` and `GetHashCode` | |
| [UPA0029](docs/rules/UPA0029.md) | Copy loops that `AddRange` would do with one allocation *(code fix)* | |
| [UPA0030](docs/rules/UPA0030.md) | Known-allocating `string` and `Enum` members in per-frame methods | ✓ |

> Not a rule: [Enum dictionary keys](docs/rules/enum-dictionary-keys.md) documents why the
> familiar "enum keys box, supply a comparer" advice no longer applies, with measurements
> across Mono and IL2CPP — and where the cost actually is.

### Correctness

| ID | Reports |
|---|---|
| [UPA1000](docs/rules/UPA1000.md) | Leaf classes not sealed *(off by default)* |
| [UPA1001](docs/rules/UPA1001.md) | Enum switches missing declared members |

### Ecosystem (all off by default; advice adapts to referenced packages)

| ID | Reports | Package awareness |
|---|---|---|
| [UPA2000](docs/rules/UPA2000.md) | String building in per-frame methods | ZString switches the advice |
| [UPA2010](docs/rules/UPA2010.md) | `async Task` methods | Runs only with UniTask referenced |
| [UPA2011](docs/rules/UPA2011.md) | Coroutine `IEnumerator` methods on MonoBehaviours | Runs only with UniTask referenced |
| [UPA2012](docs/rules/UPA2012.md) | `async void` / discarded task calls | UniTask switches the advice |
| [UPA2021](docs/rules/UPA2021.md) | Public `Action` events modelling observable state | Runs only with R3 referenced |
| [UPA2030](docs/rules/UPA2030.md) | Tweens created in per-frame methods | Runs only with DOTween referenced |
| [UPA2031](docs/rules/UPA2031.md) | Discarded infinite tweens (`SetLoops(-1)`) without `SetLink` | Runs only with DOTween referenced |
| [UPA2032](docs/rules/UPA2032.md) | String tween IDs *(Info)* | Runs only with DOTween referenced |

### Platform (off by default; run only when `UPA_TARGET_WEBGL` is defined)

| ID | Reports |
|---|---|
| [UPA3000](docs/rules/UPA3000.md) | Threading APIs (`Thread`, `Task.Run`, `Task.Delay`, …) unsupported on WebGL |
| [UPA3001](docs/rules/UPA3001.md) | `System.Net.Sockets` unsupported on WebGL |
| [UPA3002](docs/rules/UPA3002.md) | Synchronous file IO unsupported on WebGL |
| [UPA3003](docs/rules/UPA3003.md) | `System.Diagnostics.Process` unsupported on WebGL |
| [UPA3004](docs/rules/UPA3004.md) | Blocking waits on async operations (`WaitForCompletion`, `Task.Wait`, `.Result`, `GetAwaiter().GetResult()`) — deadlock on single-threaded WebGL |

Package detection is by referenced assembly name (`UniTask`, `ZString`, `R3`,
`DOTween`) — per-assembly, automatic, zero configuration.

## Tuning and suppressing

Every rule doc has a "How to configure or suppress" section. The short version:

- **One call site**: `#pragma warning disable UPA0006` / `#pragma warning restore UPA0006`
- **One assembly**: a `Default.ruleset` in that asmdef folder (see `editor-relaxed.ruleset`)
- **Whole project**: change the rule's line in your `Assets/Default.ruleset`
- **Hot-path classification** (which methods count as per-frame) and every other
  analyzer option: set them in the universal options file (see
  [Analyzer options](#analyzer-options-universal-options-file)) — effective in Unity
  builds and IDEs alike; `.editorconfig` still works as an IDE-side fallback.

Cold branches inside hot methods (lazy init, rare debug paths) will still be reported —
the analyzers do no flow analysis. Suppress those locally rather than disabling rules.

## Microsoft.Unity.Analyzers compatibility

The presets also grade `UNT####` rules; those entries only take effect where
Microsoft.Unity.Analyzers is present (e.g. bundled with Visual Studio Tools for Unity).
If you install it into the project yourself, its Roslyn requirement must not exceed the
compiler Unity bundles, or it fails with a **silent** `CS8032` warning:

| Unity | Bundled Roslyn | Safe Microsoft.Unity.Analyzers |
|---|---|---|
| 2022.3 LTS / Unity 6 | 4.3.1 (6000.5: 4.10) | Latest (1.27.0) — **except 1.23.0** |
| 2021.3 LTS *(not supported by this package)* | 3.9 | ≤ 1.22.0 |

⚠️ **Never install Microsoft.Unity.Analyzers 1.23.0**: it references Roslyn 4.14, which
no current Unity bundles — it silently does nothing on every Unity version.

This package itself targets Roslyn 3.8 and loads on every supported Unity version.

## Command-line runner (`upa-cli`)

Runs the same analyzers outside Unity, in under a second, so a CI job or a quick local
check does not need an Editor licence or a full project import.

```bash
# analyze some files (exit 1 if anything reaches the fail threshold)
dotnet run --project src/UnityPerformanceAnalyzers.Cli -- Assets/Scripts/Player.cs

# a CI gate over one assembly's full source set
# (quote the pattern: the tool expands it, so it behaves the same in every shell)
dotnet run --project src/UnityPerformanceAnalyzers.Cli -- \
  "Assets/Scripts/**/*.cs" --whole-assembly --format json --fail-on error

# pretend the project references UniTask and targets WebGL
dotnet run --project src/UnityPerformanceAnalyzers.Cli -- Assets/Scripts/Loader.cs \
  --reference UniTask --define UPA_TARGET_WEBGL

# what rules does this build know about?
dotnet run --project src/UnityPerformanceAnalyzers.Cli -- --list-rules
```

Exit codes: `0` clean, `1` diagnostics at or above `--fail-on` (default `warning`),
`2` usage or execution error — which includes an analyzer that failed to run, regardless
of `--fail-on`: a rule that crashed produced no findings to weigh, so the run cannot be
called clean.

### Options

| Option | Meaning | Example |
|---|---|---|
| `<file...>` | The `.cs` files to analyze (at least one). Patterns with `*`, `?` or `**` are expanded by the tool itself — quote them so your shell does not expand them first, and they behave identically everywhere. A pattern that matches nothing is an error | `upa-cli "Assets/**/*.cs"` |
| `--reference <name\|path>` | A **name** makes a package look present, which is all the package-conditional rules check. A **path** to a DLL loads the real assembly, which code calling into that package needs in order to resolve. Repeatable; the forms mix | `--reference UniTask`<br>`--reference Assets/Plugins/DOTween/DOTween.dll` |
| `--define <symbol>` | Add a preprocessor symbol. Repeatable | `--define UPA_TARGET_WEBGL` |
| `--assembly-name <name>` | Compilation assembly name, default `Assembly-CSharp`. Player-code rules skip `*.Editor` assemblies | `--assembly-name MyGame.Tools.Editor` |
| `--ruleset <path>` | Apply severities from a `.ruleset` | `--ruleset Assets/Default.ruleset` |
| `--editorconfig <path>` | Apply severities **and** `upa_*` analyzer options from an `.editorconfig` | `--editorconfig .editorconfig` |
| `--additionalfile <path>` | Pass an additional file, such as the universal options file. Repeatable | `--additionalfile Assets/Rules.UnityPerformanceAnalyzers.additionalfile` |
| `--unity-dll-dir <dir>` | Use a real Unity managed directory instead of the bundled stubs | `--unity-dll-dir <UnityEditor>/Data/Managed/UnityEngine` |
| `--all-warn` | Force every rule on at warning, overriding ruleset and editorconfig | `--all-warn` |
| `--whole-assembly` | Declare the files a complete assembly: enables whole-assembly rules and makes compile errors fatal | `--whole-assembly` |
| `--fail-on <level>` | Threshold for exit code 1: `none`, `info`, `warning` (default), `error` | `--fail-on error` |
| `--format <format>` | `text` (default) or `json` | `--format json` |
| `--list-rules` | Print this build's rule catalog instead of analyzing | `upa-cli --list-rules --format json` |
| `--version` | Print the tool version | `upa-cli --version` |
| `--help`, `-h` | Print usage | `upa-cli --help` |

Severity precedence, weakest first: a ruleset's `<IncludeAll>` action, then its per-rule
entries, then `--editorconfig` (which can scope a rule to one file pattern), then
`--all-warn`.

**Using it as a gate?** Pass `--whole-assembly` with an assembly's complete source set,
**plus a `--reference <path>` for every package that source calls into** (and
`--unity-dll-dir` if it uses Unity APIs the bundled stubs do not cover). Together those
turn the run from advisory into authoritative: the whole-assembly rules start reporting,
and a compilation that does not build exits 2 instead of reporting a clean result it
could not actually verify. A gate that lacks a package's DLL will fail loudly on the
unresolved types rather than quietly under-report.

**Where it differs from a Unity build** — Unity's own compilation stays the source of
truth:

- The file list you pass is not an assembly boundary, so rules that judge the whole
  assembly (currently UPA1000) are skipped unless you pass `--whole-assembly`.
- `--reference <name>` only makes a package *look* present, which is enough to activate
  its rules but leaves that package's APIs unresolved. Pass the DLL by path when the code
  actually calls into it — `--reference Assets/Plugins/DOTween/DOTween.dll`.
- Files that reference types you did not pass produce compile errors, which weaken the
  analysis: rules match on resolved symbols, so unresolved types can silently suppress a
  finding. The runner reports the count and keeps going, *except* under
  `--whole-assembly` — there you have declared a complete compilation, so it exits 2
  rather than let a gate pass code it could not analyze properly.

## Repository layout

| Path | Purpose |
|---|---|
| `src/UnityPerformanceAnalyzers/` | Analyzer assembly (netstandard2.0, Roslyn 3.8) |
| `src/UnityPerformanceAnalyzers.CodeFixes/` | IDE-only code fixes |
| `src/UnityPerformanceAnalyzers.Cli/` | `upa-cli` — run the rules without Unity |
| `src/UnityPerformanceAnalyzers.Tests/` | xUnit analyzer tests (net8.0) |
| `src/UnityStubs/` | Minimal hand-written UnityEngine stand-ins for tests |
| `package/` | UPM publishing root |
| `sandbox/UnityProject/` | Consumer-side verification project (Unity 2022.3) |
| `docs/rules/` | Per-rule documentation |

## Building

```bash
dotnet build UnityPerformanceAnalyzers.sln -c Release
dotnet test UnityPerformanceAnalyzers.sln -c Release --filter "Category!=RequiresUnity"
```

## License

MIT — see [LICENSE.md](LICENSE.md). Third-party relationships are documented in
[`package/Third Party Notices.md`](package/Third%20Party%20Notices.md).
