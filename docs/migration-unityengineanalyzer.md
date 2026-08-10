# Coming from UnityEngineAnalyzer

[繁體中文](migration-unityengineanalyzer.zh-TW.md)

[UnityEngineAnalyzer](https://github.com/vad710/UnityEngineAnalyzer) (`UEA####`) was the
first Roslyn analyzer to encode Unity performance conventions, and this package covers much
of the same ground. If you are moving across, this page says which of its rules have an
equivalent here, which are covered by a tool you should be running anyway, and which are
simply not covered.

**Its state, as of 2026-08-10:** MIT licensed, 285 stars, **no commits since 22 October
2019**. The repository is not archived. That is the whole of what can be said without
guessing: it is unmaintained in the ordinary sense of the word, but nobody has declared it
finished.

---

## Rule by rule

| UnityEngineAnalyzer | What it looks for | Here |
|---|---|---|
| `UEA0001` DoNotUseOnGUI | `OnGUI` in game code | [UPA0023](rules/UPA0023.md) — Info, off by default |
| `UEA0002` DoNotUseStringMethods | Allocating `string` members | [UPA0030](rules/UPA0030.md) — a closed list, per-frame only |
| `UEA0003` EmptyMonoBehaviourMethod | Empty Unity messages | **Not here.** `UNT0001` in Microsoft.Unity.Analyzers |
| `UEA0004` UseCompareTag | Tag comparison | [UPA0002](rules/UPA0002.md) is wider — it reports `name` and `tag` reads at all, not only comparisons. `UNT0002` is the direct equivalent |
| `UEA0005` DoNotUseFindMethodsInUpdate | Scene search per frame | [UPA0014](rules/UPA0014.md) |
| `UEA0006` DoNotUseCoroutines | Coroutines | [UPA2011](rules/UPA2011.md), but **only when the assembly references UniTask** — a rule telling you to stop writing coroutines is not useful without the thing to write instead |
| `UEA0007` DoNotUseForEachInUpdate | `foreach` per frame | **Not here.** Where a particular `foreach` does allocate — a boxed enumerator on an interface-typed collection — [UPA0006](rules/UPA0006.md) reports the allocation itself rather than the loop |
| `UEA0008` UnsealedDerivedClass | Leaf classes not sealed | **Was here, and was retired.** UPA1000 shipped, was measured on IL2CPP, and the gain could not be told apart from noise. See [UPA1000](rules/UPA1000.md) for the numbers |
| `UEA0009` InvokeFunctionMissing | `Invoke("Name")` naming a method that does not exist | **Not here.** [UPA0016](rules/UPA0016.md) covers the `SendMessage` family, not `Invoke` |
| `UEA0010` DoNotUseStateNameInAnimator | Animator state names as strings | [UPA0003](rules/UPA0003.md) |
| `UEA0011` DoNotUseStringPropertyNames | Shader property names as strings | [UPA0003](rules/UPA0003.md) |
| `UEA0012` CameraMainIsSlow | `Camera.main` | [UPA0015](rules/UPA0015.md) — Info, because Unity 2020.2 and later cache the lookup |
| `UEA0013` UseNonAllocMethods | Non-allocating physics overloads | **Not here.** `UNT0028` covers it; [UPA0010](rules/UPA0010.md) checks a different thing about the same calls — whether the query is bounded |
| `UEA0014` AudioSourceMuteUsesCPU | `AudioSource.mute` | **Not here** |
| `UEA0015` InstantiateTakeParent | `Instantiate` without a parent | **Not here.** Rider has an inspection for it. [UPA0031](rules/UPA0031.md) reports `Instantiate` on a per-frame path, which is a different concern |
| `UEA0016` VectorMagnitudeIsSlow | `magnitude` where the square would do | [UPA0021](rules/UPA0021.md) — with a code fix |

**Eight of sixteen have a direct equivalent.** Three are covered by tools worth running
alongside this one, three have no equivalent anywhere, one is deliberately absent, and one
existed here until measurement took it away.

---

## What is different about the overlap

**Most rules here are scoped to per-frame code.** `UEA0002` reports allocating string
methods wherever they appear; UPA0030 reports them in per-frame Unity messages and in
methods you mark as hot. That is fewer reports, and the ones you get are the ones where the
cost repeats.

**Rules that need a package only exist when the package does.** UPA2011 does not appear at
all in an assembly with no UniTask reference — not disabled, absent. Nothing to configure
and nothing to see.

**A rule whose premise stops holding gets removed.** UEA0008 and UPA1000 are the same rule.
Ours was measured against IL2CPP, sealed came out at 2.70 ns against 3.00 ns unsealed inside
a spread of 1.28 ns with the ordering reversing once, and it was retired rather than left
in. [Versioning and rule governance](versioning.md) sets out when that happens.

---

## Moving over

1. Remove the UnityEngineAnalyzer package or DLL. Running both means duplicate reports for
   the eight overlapping rules.
2. Install this package and pick a preset — without one, only the rules that are on by
   default report, at Warning.
3. Install [Microsoft.Unity.Analyzers](https://github.com/microsoft/Microsoft.Unity.Analyzers)
   if you do not already have it. It covers `UEA0003` and `UEA0013`, and its 23 diagnostic
   suppressors stop general C# analyzers producing nonsense on Unity code. This package
   does not replicate any of that.
4. If the report count is high on an existing project, freeze it rather than fixing
   everything first:

   ```bash
   upa-cli "Assets/Scripts/**/*.cs" --whole-assembly --write-baseline upa-baseline.json
   ```

5. `#pragma warning disable UEA####` comments do nothing here — the ids differ. Search for
   them and translate them using the table above, or delete them and see what reports.

There is no automatic translation of suppression comments, and there will not be: an id in
a `#pragma` is a decision someone made about a specific line, and mapping it across two
rules with different scopes would silence code neither rule was asked about.
