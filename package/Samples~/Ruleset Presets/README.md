# Severity Presets

> English | [繁體中文](README.zh-TW.md)

Rulesets are the channel Unity actually reads: Unity passes `Assets/Default.ruleset`
(and per-asmdef-folder rulesets) to the C# compiler, while `.editorconfig` files are
**not** passed at all (verified on 2022.3 and Unity 6). The `.editorconfig` variants
here exist only to keep Rider / Visual Studio severities in sync with your ruleset.

## Picking a preset

Which rule ids each preset sets, and to what, is in the preset files themselves; the rule
tables in the repository README and `upa-cli --list-rules` describe what each one reports.
Listing ids here as well only produced a third copy to keep in step, and it had already
fallen behind.

| Preset | Intent |
|---|---|
| `minimal` | Unity correctness rules (`UNT` group) as errors; the correctness rules that are on by default keep their warning. Everything else off — a safe first install. |
| `recommended` | + UPA performance rules as warnings. The everyday default. |
| `strict` | Performance rules become errors, and the rules that are off by default because they ask something of the project — a logging wrapper, a sealed leaf class — start reporting. |
| `cysharp-stack` | + ecosystem rules as errors (UniTask/ZString/R3 adoption). For codebases committed to the Cysharp stack. |

## Install

1. Import this sample from the Package Manager window.
2. Copy the chosen preset into your project as `Assets/Default.ruleset`.
3. Optional per-assembly override: place a `Default.ruleset` inside any asmdef folder —
   it replaces the project-wide file for that assembly only.

## WebGL rules

`webgl-addon.ruleset` grades the platform rules — threading, sockets, synchronous file IO,
`Process`, blocking waits — as warnings. To stack it on any base preset, copy it next to your
`Assets/Default.ruleset` and add inside the `<RuleSet>` element:

```xml
<Include Path="webgl-addon.ruleset" Action="Default" />
```

Then add `UPA_TARGET_WEBGL` to **Project Settings > Player > Scripting Define Symbols**
for every build target — the rules stay active during day-to-day development instead of
only when the active build target is WebGL.

## Editor tooling

Rulesets cannot scope by path. Copy `editor-relaxed.ruleset` into each Editor asmdef
folder and rename it `Default.ruleset`: performance rules go quiet there while `UNT`
correctness rules stay at error.

## IDE parity (`.editorconfig` variants)

Copy the matching `.editorconfig` to your project root (merge with an existing one).
It also carries the `upa_hot_path_*` options — those are honored by IDEs only; Unity
builds always use the built-in hot-path defaults.

## Notes

- `UNT####` severities only take effect where Microsoft.Unity.Analyzers is present
  (e.g. the copy bundled with Visual Studio Tools for Unity); the entries are inert
  otherwise.
- The ecosystem rules run only in assemblies that reference the package they are about
  (UniTask, ZString, R3, DOTween); their preset entries are inert everywhere else.
