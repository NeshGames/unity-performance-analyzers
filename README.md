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
https://github.com/NeshGames/unity-performance-analyzers.git?path=/package#v0.4.0
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

## Repository layout

| Path | Purpose |
|---|---|
| `src/UnityPerformanceAnalyzers/` | Analyzer assembly (netstandard2.0, Roslyn 3.8) |
| `src/UnityPerformanceAnalyzers.CodeFixes/` | IDE-only code fixes |
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
