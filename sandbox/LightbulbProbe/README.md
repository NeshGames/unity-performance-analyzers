# Lightbulb probe

The one thing about the code fixes that no automated check in this repository can answer:
**does the lightbulb actually appear in an IDE?**

The analyzers are compiled by both supported editors in `sandbox/verify.sh`, and each fix has
unit tests that apply it and compare the result. Both of those drive Roslyn directly. Neither
says whether Rider or Visual Studio loaded `UnityPerformanceAnalyzers.CodeFixes.dll` and
offered the rewrite — that path runs through Unity generating a `.csproj`, listing both
assemblies as analyzers, and the IDE honouring it. Every link in that chain can break without
the build going red.

## Setup

```bash
sandbox/lightbulb.sh
```

That builds both assemblies, installs them into the package, writes the manifest, and copies
the `recommended` preset in as `Assets/Default.ruleset` — the step the documentation gives a
consumer. All four rules are on by default and would report without it; the probe takes the
step anyway, because a setup nobody uses is not the setup worth checking.

Then open `sandbox/LightbulbProbe` in Unity Hub, let the import finish, and choose
**Assets → Open C# Project**.

## What to check

Open `Assets/LightbulbProbe.cs`. Four spans should be underlined. On each, press
**Alt+Enter** (Rider) or **Ctrl+.** (Visual Studio):

| Line | Rule | The fix should offer |
|---|---|---|
| `active.HasFlag(Layers.Water)` | UPA0022 | a bitwise check — `(active & Layers.Water) == Layers.Water` |
| `v.magnitude < 5f` | UPA0021 | squaring both sides — `v.sqrMagnitude < 25f` |
| `Vector3.Distance(a, b) <= 5f` | UPA0021 | `(a - b).sqrMagnitude <= 25f` |
| `yield return 0` | UPA0019 | `yield return null` |

Applying a fix should leave the file compiling and the warning gone.

## Reading the result

**All four offer their fix.** The chain works, and the one claim this project could not
make about itself can be made.

**Warnings appear, no lightbulb on any of them.** The analyzer assembly reached the compiler
and the code-fix assembly did not. Look at the generated `.csproj`: it should carry an
`<Analyzer Include="..." />` item for *both* DLLs. If only the first is there, the second
one's `.meta` has lost its `RoslynAnalyzer` label — Unity regenerates `.meta` files and the
label does not survive, which is the failure this project exists to catch.

**No warnings at all.** The analyzers did not load. Check the Unity console for `CS8032`: an
analyzer built against a newer Roslyn than its host is not an error, it is a single warning
followed by a compile with no analyzers at all. `sandbox/verify.sh` checks for exactly this
in both editors and would normally have caught it first.

**Warnings appear but one span has no lightbulb.** Note which. Not every warning has a fix —
the line with `HasFlag` also reports UPA0006 for the boxing, and UPA0006 has no fix by
design, so a lightbulb missing *there* is correct. The four rows in the table above are the
ones that must offer something.

**A fix produces code that does not compile, or changes behaviour.** Say what you did and
what came out. That is worth more than the other outcomes.

Until someone has run this, the package documentation stays cautious about what the code
fixes do in an IDE — a check no machine here can run is not a check that has passed.
