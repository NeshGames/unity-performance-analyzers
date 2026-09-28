# Agent severity profiles

> English | [繁體中文](README.zh-TW.md)

These files are deliberately small in number because coding agents are the primary consumers.

| File | Use |
|---|---|
| `unity.ruleset` | Copy to `Assets/Default.ruleset`. It contains no Error entries, so analyzer findings cannot push the Editor into Safe Mode. |
| `ci.ruleset` | Use with `upa-cli` in CI. Core rules are promoted to Error; Optional / House policy rules stay Warning or Info. Do not use it as the live Unity ruleset. |
| `webgl.ruleset` | Optional overlay for UPA3000–UPA3004. Define `UPA_TARGET_WEBGL` and include it from the active ruleset. |

## Unity

Copy:

```text
unity.ruleset -> Assets/Default.ruleset
```

A `Default.ruleset` inside an asmdef folder overrides the project-wide one for that assembly. If
tooling/generated code needs different policy, put a small custom ruleset in that asmdef folder
rather than maintaining a separate shipped preset.

## CI

Run the stricter profile outside the Editor:

```bash
upa-cli @upa-args.rsp --ruleset ci.ruleset --format json --fail-on error
```

This is intentionally separate from Unity's own ruleset. An Error diagnostic in Unity fails script
compilation, and an Editor that starts with compile failures can enter Safe Mode and become
unavailable to an automation agent.

## WebGL

Place `webgl.ruleset` next to the base ruleset and include it:

```xml
<Include Path="webgl.ruleset" Action="Default" />
```

Also define `UPA_TARGET_WEBGL`. The overlay keeps its diagnostics at Warning; CI decides whether
warnings fail through `upa-cli --fail-on`.

Rule behaviour is documented under `docs/rules/`; `upa-cli --list-rules` is the live catalog.
