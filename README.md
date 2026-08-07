# unity-performance-analyzers

> English | [繁體中文](README.zh-TW.md)

Roslyn analyzers that turn Unity performance and correctness conventions into
compile-time checks. Rules adapt automatically to the packages each assembly references
(UniTask, ZString, R3) and to whether the project targets WebGL.

Distributed as a UPM package. Supports **Unity 2022.3 LTS through Unity 6**.

> **Status: pre-release.** All 24 v0.1 rules are implemented and verified against
> Unity 2022.3 and Unity 6 sandbox builds; v0.1.0 has not been tagged yet.

## Install

Package Manager > *Add package from git URL…*:

```
https://github.com/NeshGames/unity-performance-analyzers.git?path=/package#v0.1.0
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
| `cysharp-stack` | + ecosystem rules as errors (LINQ ban, UniTask/ZString/R3 adoption) |

Extras in the same sample:

- `webgl-addon.ruleset` — stacks UPA3000–3003 onto any preset via ruleset `<Include>`;
  requires the `UPA_TARGET_WEBGL` scripting define (see the sample README)
- `editor-relaxed.ruleset` — drop into Editor asmdef folders as `Default.ruleset` to
  silence performance rules in tooling code
- `.editorconfig` variants of each preset for Rider / Visual Studio parity

Unity reads rulesets only — it does not pass `.editorconfig` to the compiler (verified
on 2022.3 and Unity 6). A `Default.ruleset` inside an asmdef folder overrides the
project-wide one for that assembly.

Import the **Smoke Test** sample to verify the analyzer is loaded: it violates several
rules on purpose and should light up the Console immediately.

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

### Correctness

| ID | Reports |
|---|---|
| [UPA1000](docs/rules/UPA1000.md) | Leaf classes not sealed *(off by default)* |
| [UPA1001](docs/rules/UPA1001.md) | Enum switches missing declared members |

### Ecosystem (all off by default; advice adapts to referenced packages)

| ID | Reports | Package awareness |
|---|---|---|
| [UPA2000](docs/rules/UPA2000.md) | String building in per-frame methods | ZString switches the advice |
| [UPA2001](docs/rules/UPA2001.md) | `System.Linq` calls in per-frame methods | |
| [UPA2010](docs/rules/UPA2010.md) | `async Task` methods | Runs only with UniTask referenced |
| [UPA2011](docs/rules/UPA2011.md) | Coroutine `IEnumerator` methods on MonoBehaviours | |
| [UPA2012](docs/rules/UPA2012.md) | `async void` / discarded task calls | UniTask switches the advice |
| [UPA2021](docs/rules/UPA2021.md) | Public `Action` events modelling observable state | Runs only with R3 referenced |

### Platform (off by default; run only when `UPA_TARGET_WEBGL` is defined)

| ID | Reports |
|---|---|
| [UPA3000](docs/rules/UPA3000.md) | Threading APIs (`Thread`, `Task.Run`, `Task.Delay`, …) unsupported on WebGL |
| [UPA3001](docs/rules/UPA3001.md) | `System.Net.Sockets` unsupported on WebGL |
| [UPA3002](docs/rules/UPA3002.md) | Synchronous file IO unsupported on WebGL |
| [UPA3003](docs/rules/UPA3003.md) | `System.Diagnostics.Process` unsupported on WebGL |

Package detection is by referenced assembly name (`UniTask`, `ZString`, `R3`) —
per-assembly, automatic, zero configuration.

## Tuning and suppressing

Every rule doc has a "How to configure or suppress" section. The short version:

- **One call site**: `#pragma warning disable UPA0006` / `#pragma warning restore UPA0006`
- **One assembly**: a `Default.ruleset` in that asmdef folder (see `editor-relaxed.ruleset`)
- **Whole project**: change the rule's line in your `Assets/Default.ruleset`
- **Hot-path classification** (which methods count as per-frame) is configurable via
  `.editorconfig` — **in IDEs only**; Unity builds always use the built-in defaults:

  ```ini
  upa_hot_path_messages = Update,FixedUpdate,LateUpdate
  upa_hot_path_attributes = HotPath,PerfCritical
  upa_hot_path_include_lambdas = true
  ```

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
