---
name: unity-performance-analyzers
description: Checks Unity C# for per-frame performance and correctness problems with the unity-performance-analyzers rules (UPA0001–UPA3004) and acts on the findings. Use after writing or editing C# under Assets/ or a local package in a Unity project that has the com.neshgames.unity-performance-analyzers package installed, when the Unity Console or Editor.log shows a UPA#### warning or error, when a CI job fails on upa-cli, or when the user asks to find allocations, GetComponent/Camera.main/Find calls, or other per-frame costs in Update and similar methods.
---

# unity-performance-analyzers

The package's rules run inside Unity's own compile, so every UPA#### finding also appears in the
Console and `Editor.log`. `upa-cli` runs the same rules without an Editor, in about a second, and
reports machine-readably. Use `upa-cli` to check your own edits; treat a Unity compile as the
final authority.

## Critical rules

- **Fix, don't silence.** Read the rule's page before changing anything:
  `docs/rules/<ID>.md` in https://github.com/NeshGames/unity-performance-analyzers (the
  diagnostic's help link points there). It says what the rewrite is and when it is safe.
- **Never edit `Assets/Default.ruleset` to make one call site go away.** That turns the rule off
  for the whole project.
- **A suppression carries its reason on the same line**, or it is not acceptable:
  `#pragma warning disable UPA0006 // one-shot lazy init; runs once per session` … `#pragma warning restore UPA0006`.
  Use it for the cases the rule page lists under "When to suppress warnings" — typically a cold
  branch inside a hot method, which the rules cannot tell apart because they do no flow analysis.
- **Keep the Unity profile non-fatal.** Use `unity.ruleset` as `Assets/Default.ruleset`.
  Analyzer Error diagnostics fail Unity's compile; an Editor started on a project that does not
  compile can enter Safe Mode, where `unity command` cannot connect. Use `ci.ruleset` only with
  `upa-cli` for the stricter gate.
- **Match versions.** A `upa-cli` from a different release knows a different rule set. Use the
  release whose tag matches the package version in `Packages/manifest.json`
  (`…unity-performance-analyzers.git?path=/package#vX.Y.Z`).

## 1. Get upa-cli

```bash
upa-cli --version
```

If that fails, download the archive for your platform from the release matching the package
version — `upa-cli-<version>-linux-x64.tar.gz`, `-osx-arm64.tar.gz` or `-win-x64.zip` at
https://github.com/NeshGames/unity-performance-analyzers/releases — and extract it. It is
self-contained; no .NET install is needed. Ask the user before downloading.

## 2. Generate the project's arguments once

Run from the Unity project root, after the project has compiled in Unity at least once:

```bash
upa-cli --init-args upa-args.rsp --assembly-name Assembly-CSharp
```

This reads what Unity actually compiled that assembly with — defines, references, the full
source set — and marks the run `--whole-assembly`, so a compile error is fatal instead of quietly
silencing rules. Repeat per asmdef (`--assembly-name <asmdef name>`) you are working in. Regenerate after
changing packages, defines or the Editor version; a stale file fails on the moved reference rather
than analyzing less.

Without it, a handful of files compiles against stubs: unresolved types silence rules, so a
partial run under-reports. That is fine for a quick look, never for a verdict.

## 3. Check

```bash
upa-cli @upa-args.rsp --ruleset Assets/Default.ruleset --format json --fail-on warning
```

Read the JSON in this order:

1. `summary.analyzerFailureCount` > 0 → nothing was analyzed. Report it; do not treat the run as clean.
2. `summary.compileErrorCount` > 0 → findings may be missing (with the response file this also
   exits `2`). Fix the arguments or the code first; the errors are listed in `compileErrors[]`.
3. `diagnostics[]` → each has `id`, `severity`, `message`, `file`, `line`, `column`, `helpUri`.

Exit codes: `0` nothing at or above `--fail-on`, `1` findings at or above it, `2` a usage error,
an analyzer that failed, or a whole-assembly run that did not compile — never read `2` as
"no findings".

To hear only about the files you changed — every input is still compiled, so symbols resolve
exactly as in a full run:

```bash
# relative to the project root, and including files not yet tracked
{ git diff --name-only --relative HEAD; git ls-files --others --exclude-standard; } > changed.txt
upa-cli @upa-args.rsp --ruleset Assets/Default.ruleset --format json --only-from changed.txt
```

`upa-cli --list-rules` lists every rule with its default severity and conditions.

## 4. Act on each finding

- Open the rule page from `helpUri`. Apply the rewrite it describes, respecting its safety
  conditions (for example, UPA0029's `AddRange` rewrite is safe for array sources only).
- Re-run step 3 until the file is clean, then let Unity recompile.
- If the finding is on code that genuinely runs once (lazy init, a rare state change), suppress it
  locally with the reason on the same line, as above.
- For an existing project with many findings, freeze them rather than fixing everything at once:
  `upa-cli @upa-args.rsp --write-baseline upa-baseline.json`, commit the file, and
  pass `--baseline upa-baseline.json` from then on. Only new violations report.

## 5. Reading Unity's own output

UPA diagnostics appear in `Editor.log` as `File.cs(12,5): warning UPA0006: …`, or `error UPA0006`
under an Error-level ruleset. When searching the log for what broke a compile — including while
recovering from Safe Mode — match analyzer ids too, not just compiler ones:

```bash
grep -iE '(error|warning) (CS|UPA|UNT)[0-9]{4}|Scripts have compiler errors' <Editor.log path> | tail -40
```

A pattern that only matches `error CS[0-9]{4}` reports "no compile errors" for a compile that an
analyzer failed.

## 6. CI

After `unity doctor --ci` and before `unity test` in a workflow generated by `unity ci init`:

```yaml
- name: Unity performance analyzers
  run: upa-cli @upa-args.rsp --ruleset Assets/Default.ruleset --format github --fail-on warning
```

`--format github` annotates the pull request at the offending file and line. Commit
`upa-args.rsp`; its `--unity-dll-dir` line must point at the runner's Unity installation. To gate
harder than the project's own Unity ruleset without making Unity's compile fail, pass the
agent CI profile only here, e.g. `--ruleset ci/upa-ci.ruleset --fail-on error`.
