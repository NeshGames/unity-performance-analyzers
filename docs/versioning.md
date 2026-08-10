# Versioning and rule governance

What a version number promises, what a rule ID promises, and what may change under you.

Analyzers are unusual: an upgrade can fail a build that passed yesterday without a single
line of your code changing. This page exists so that never happens by surprise.

[繁體中文](versioning.zh-TW.md)

---

## Install from a tag

```
https://github.com/NeshGames/unity-performance-analyzers.git?path=/package#v0.8.0
```

UPM resolves the package straight out of the tag, so the tag *is* the version you get.
Pointing at a branch instead gives you whatever that branch happens to hold, including a
`package.json` version that is only written during a release — a branch install is not a
version, and nothing here applies to it.

The CLI has the same rule. Check out the tag matching the package your project uses: a CLI
built from a different revision may know rules that package does not, and the command line
and the Editor would then disagree about the same code.

---

## What the version number means

`0.MINOR.PATCH`, and the leading zero is doing real work.

| | Before 1.0 | From 1.0 |
|---|---|---|
| Patch (`0.8.0` → `0.8.1`) | Bug fixes, false-positive fixes, documentation | Same |
| Minor (`0.8.0` → `0.9.0`) | Anything in the table below, including changes that can fail a build | New rules, wider rules, new options |
| Major | — | Anything that breaks a compatibility surface |

Until 1.0, treat every minor as potentially build-affecting and read the changelog. That is
the cost of a pre-1.0 package, stated plainly rather than implied by the number.

1.0 is what turns the compatibility surfaces below from convention into contract.

---

## What a rule ID promises

**A `UPA####` number is spent permanently the moment it appears on a tag.** It is never
reused for a different rule, and it is never deleted.

That is not tidiness. Your ruleset entries, `.editorconfig` lines, `#pragma warning
disable` comments and baseline entries all name rules by number, and they all live in your
repository rather than in this one. A recycled ID would silently repoint every one of them
at a rule you never read about.

Retiring a rule therefore means:

- its severity default becomes off,
- it is marked deprecated in its title, in both languages,
- its documentation page stays, and says what replaced it or why it went,
- the number is never given to anything else.

Two rules are retired today: [UPA0022](rules/UPA0022.md) and [UPA1000](rules/UPA1000.md).
Both still resolve. A suppression you wrote for either keeps meaning what it meant.

---

## Severity policy

**No rule's own default is above Warning.** Of 46 rules, 43 default to Warning and 3 to
Info. Nothing in this package decides on its own that your build should fail.

Errors come from a preset *you* chose — `minimal`, `recommended`, `strict` or
`cysharp-stack`. That is the only channel through which a rule reaches error level, and it
is a file in your project that you can read and edit.

Ecosystem rules (`UPA2000`+) and platform rules (`UPA3000`+) ship off by default. They turn
on when the assembly being compiled references the package in question, or when you define
`UPA_TARGET_WEBGL` — per assembly, automatically, with nothing to configure.

---

## What may change, and what it costs you

| Change | Version | What you may have to do |
|---|---|---|
| New rule, new ID | Minor | Nothing, unless a preset grades it as an error. New rules enter presets one version after they ship |
| A rule reports **less** — a narrowed rule, a fixed false positive | Patch or minor | Nothing. Baseline entries for the dropped reports become stale and are reported as such |
| A rule reports **more** — a widened rule | **Minor, never patch** | This is the change most likely to fail a build. The changelog names the rule and what it now catches |
| A rule is retired | Minor | Nothing. It stops reporting; your suppressions stay valid |
| A rule's own default severity changes | Minor | Nothing, unless you relied on the default rather than a preset |
| Preset contents change | Minor | Re-copy the preset if you took it from the sample. Your edited copy is untouched |
| CLI arguments, exit codes, JSON schema | Major from 1.0 | See the compatibility surfaces below |
| Baseline file format | Major from 1.0 | Regenerate with `--write-baseline` |
| A rule page moves | Never | Help links are part of the diagnostic |

The row that matters is the third one. A rule that starts reporting more is indistinguishable
from your code getting worse, and if a preset grades it as an error it fails the build.
It is always a minor, it is always in the changelog by name, and a baseline is the way to
adopt it without stopping to fix everything first:

```bash
upa-cli "Assets/Scripts/**/*.cs" --whole-assembly --write-baseline upa-baseline.json
```

---

## Compatibility surfaces

These are the things other people's files, scripts and pipelines name. They do not change
outside a major version once 1.0 lands, and before then they change only with a changelog
entry that says so.

- **Rule IDs and help URLs** — named by rulesets, `.editorconfig`, pragmas and baselines
- **Package name** `com.neshgames.unity-performance-analyzers` and assembly name
  `UnityPerformanceAnalyzers`
- **CLI argument names and exit codes** — `0` clean, `1` diagnostics at or above the
  threshold, `2` usage or execution error
- **`--format json` document shape**, versioned by its own `schemaVersion` field
- **Baseline file format**, likewise versioned in the file
- **Preset file names** — `minimal`, `recommended`, `strict`, `cysharp-stack`,
  `webgl-addon`, `editor-relaxed`

### Roslyn 3.8, and why it stays

The analyzer is compiled against Roslyn 3.8.0 and will keep being compiled against the
oldest compiler any supported editor ships. This is a compatibility decision, not a
maintenance backlog.

An analyzer built against a newer Roslyn than the host does not fail the build. It emits
`CS8032` and then *does nothing* — no diagnostics, no error, no indication that a whole
package of rules stopped running. Silence looks exactly like a clean project. Raising the
floor therefore waits until no supported editor is below it.

**Supported editors: Unity 2022.3 LTS and Unity 6.** Both are smoke-tested on every
release; a release does not go out unless both report the analyzer loaded and firing.

---

## When a rule is removed because it was wrong

Every performance claim in this package has to survive measurement on IL2CPP — the backend
a shipped game actually runs. Reasoning from IL semantics, from .NET behaviour, or from
what an optimization "should" do is not evidence, and neither is a Mono number: Mono is
recorded for contrast and IL2CPP decides.

The consequence is a governance rule, not an aspiration:

> **A rule whose premise has expired is worse than no rule.** It asks for a change that
> buys nothing, and it spends your attention every time it fires.

So rules are re-measured, and the ones measurement refutes are retired or narrowed —
including rules that have already shipped. Version 0.8.0 was that, and only that:
`UPA0022` retired, `UPA1000` retired, `UPA0006`'s enum-argument report withdrawn, `UPA0026`
narrowed to the one call it could still justify. Four rules got smaller and none got bigger.

If you find a rule whose advice does not hold on IL2CPP, that is the most useful bug report
this project can receive.

---

## Reporting a false positive

A rule that fires on correct code is a defect, and fixing it is a patch. What makes one
quick to fix:

- the rule ID and the source line it fired on,
- the smallest snippet that still triggers it,
- the Unity version and whether the assembly is Editor or player code,
- what you expected instead — no report, or a report somewhere else.

The CLI reproduces it outside Unity in one line, which is usually faster than screenshots:

```bash
upa-cli Assets/Scripts/Thing.cs --all-warn --format json
```

Until a fix ships, `#pragma warning disable UPA####` or a ruleset entry silences it, and
neither becomes wrong when the fix lands — an ID never changes meaning.
