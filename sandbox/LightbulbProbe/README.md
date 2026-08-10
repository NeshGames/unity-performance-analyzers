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
consumer — then raises UPA2012 to warning, which that preset leaves off.

The project carries stub assemblies named `DOTween`, `UniTask` and `ZString`. All three
packages are detected by assembly name, so the stubs are enough to make the ecosystem fixes
reachable without redistributing any of the libraries. ZString's stub also has to carry
`Concat`, because that fix's output has to compile. Most of these rules are on by default and would report without it, but UPA0009 is not - the
preset is what turns it on, and installing it is the step the documentation gives a
consumer.

Then open `sandbox/LightbulbProbe` in Unity Hub, let the import finish, and choose
**Assets → Open C# Project**.

## What to check

`Assets/PlayerHealthBar.cs` sits beside the probe and is **not** part of the walkthrough. It
exists for the README's screenshots: the same rules firing on code that reads like a game
rather than on a file that names the rules it is testing.

Open `Assets/LightbulbProbe.cs`. Eleven spans should be underlined. On each, press
**Alt+Enter** (Rider) or **Ctrl+.** (Visual Studio):

| Line | Rule | The fix should offer |
|---|---|---|
| `mat.SetFloat("_Alpha", 0.5f)` | UPA0003 | a `private static readonly int Alpha` on this type, and the integer overload |
| `mat.SetFloat("_Alpha", 1f)` | UPA0003 | the same — and **Fix All in document** should add one field, not two |
| `active.GetType()` | UPA0026 | `typeof(Layers)` |
| `v.magnitude < 5f` | UPA0021 | squaring both sides — `v.sqrMagnitude < 25f` |
| `Vector3.Distance(a, b) <= 5f` | UPA0021 | `(a - b).sqrMagnitude <= 25f` |
| `yield return 0` | UPA0019 | `yield return null` |
| `i < items.Count` | UPA0009 | a local before the loop — `int itemsCount = items.Count;` |
| `foreach (var value in seed)` | UPA0029 | `items.AddRange(seed)` |
| `.SetLoops(-1)` | UPA2031 | `.SetLink(gameObject)` appended |
| `LoadAsync();` | UPA2012 | `.Forget()` appended |
| `"score: " + total` | UPA2000 | `ZString.Concat("score: ", total)` |

Applying a fix should leave the file compiling and the warning gone.

## When a span the ruleset enables shows nothing

The IDE reads `Default.ruleset` through `CodeAnalysisRuleSet` in the generated `.csproj`,
and it takes that at project load. Change the ruleset after Unity wrote the `.csproj` — which
`lightbulb.sh` does, since it copies the preset in and then raises UPA2012 — and an editor
already holding the solution keeps the severities it started with. UPA2012 showed no
squiggle for exactly this reason on 2026-08-10, and the rule was fine: the same file, the
same ruleset and the command-line runner reported it.

**Edit ▸ Preferences ▸ External Tools ▸ Regenerate project files**, then reload the IDE.
Check that before concluding a rule is broken; the two look identical from the editor.

## Reading the result

**All eleven offer their fix.** The chain works, and the one claim this project could not
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

**Warnings appear but one span has no lightbulb.** Note which. The eleven rows in the table
above are the ones that must offer something.

The `"score: " + total` line also reports UPA0006 for the boxing, which has no fix by
design — two diagnostics on one line, one bulb.

**A warning on the `HasFlag` line.** That is a failure, and a different one. The call
allocates nothing on any supported runtime: UPA0022 is deprecated and UPA0006 does not
report the argument box, because the runtime removes it along with the call. A diagnostic
there means an older analyzer assembly is loaded.

**A fix produces code that does not compile, or changes behaviour.** Say what you did and
what came out. That is worth more than the other outcomes.

Walked on 2026-08-10 in **Visual Studio** and **Rider**, both against Unity 2022.3.62f2:
all eleven spans offered their fix, the HasFlag line stayed silent, UPA2000 and UPA2012
each added the `using` their rewrite needs, and UPA0003's Fix All across the project
turned three call sites in two types into two fields. Diagnostics came back in Traditional
Chinese in both editors.

Re-run it when a fix is added or changed. A check no machine here can run is not a check
that stays passed.
