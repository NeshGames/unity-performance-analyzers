# Rule overlap with other Unity analysis tools

> English | [繁體中文](overlap.zh-TW.md)

Most Unity projects already run at least one other analysis tool. This page records, per rule,
what else reports the same thing, and what to do about it.

The short version is at the top because it changes what the table means.

---

## The thing to understand first

**Rider's performance inspections and this package are not the same kind of output.**

Rider's Unity plugin marks `Update`, `LateUpdate`, `FixedUpdate` and coroutines as a
*performance-critical context*, then flags expensive operations inside it with gutter line
markers and highlights. JetBrains is explicit that these are **not warnings or suggestions** —
the code isn't wrong, it is doing something known to be expensive, and there is usually no
mechanical fix. They are there to create awareness.

This package produces compiler diagnostics with a severity. They appear in the Unity Console,
they can be set to error, and `upa-cli` can fail a build on them.

So "Rider already covers UPA0001" is true about the *information* and false about the
*enforcement*. Deciding what to disable requires separating those two, which is what the
recommendation column does.

**The practical consequence: Rider overlap is not a reason to turn a UPA rule off.** Rider and
ReSharper inspections are IDE-only and never gate a build, so the UPA rules they overlap with
are kept — they are the half of the pair that can be enforced in the Unity compile and in CI.

---

## Tools compared

| Tool | Kind of analysis | Where it runs | Can it fail a build? |
|---|---|---|---|
| **Rider / ReSharper** (`resharper-unity`) | Roslyn-adjacent, with call-graph propagation | IDE | No — indicators carry no severity |
| **Microsoft.Unity.Analyzers** (`UNT####`, `USP####`) | Roslyn analyzers + diagnostic suppressors | IDE + Unity compile | Yes, if graded in a ruleset |
| **Project Auditor** (`PAC####`, `PAS####`) | Whole-project audit; code analysis runs over the player assemblies, and a Roslyn analyzer set ships in a separate rules package | Unity Editor — built in from Unity 6.4 | From an Editor run, batch mode included |
| **Package-native** (`UniTask.Analyzer`, …) | Roslyn analyzers shipped by the library | IDE + Unity compile | Yes |
| **unity-performance-analyzers** (`UPA####`) | Roslyn analyzers, package- and platform-conditional | IDE + Unity compile + `upa-cli` | Yes, including offline in CI |

Two structural notes:

- **Rider propagates, we don't.** Rider marks a method expensive if anything it calls is
  expensive, walking back to the `Update` root — including through delegates. This package does
  no flow analysis and sees only the hot method itself and its lambdas. Where a rule is marked
  *"Rider is stronger"* below, this is why.
- **Project Auditor is a different cadence.** It audits the whole project in one Editor run,
  marks findings inside hot paths such as `MonoBehaviour.Update` as Critical, and lets you Mute
  individual issues. It is a periodic audit, not a per-keystroke or per-PR check. Its overlap
  with this package is real but rarely creates duplicate *noise*, because you are not looking at
  both at the same moment. The next section says where that line is, and where it might move.

---

## Project Auditor and this package

Project Auditor is Unity's own analysis tool, and it is the one this package is most often
asked about. The short version: **install it, and expect it to answer different questions.**

**How you get it, as of 2026-08-10.** Unity 6.4 and later include Project Auditor in the
Editor — *Window ▸ Analysis ▸ Project Auditor*, no package install. Before 6.4 it is a
Package Manager install (`com.unity.project-auditor`). Either way you also need the separate
**Project Auditor Rules** package (`com.unity.project-auditor-rules`), which is where the
rules themselves now live. That split is recent and deliberate: the rules package's own
changelog records the rules **and its Roslyn analyzers** being moved out of the main package
"as we migrate the tool to be bundled with the Unity Editor as a module".

**What each one is for.**

| | Project Auditor | This package |
|---|---|---|
| Scope | The whole project: asset import settings, project settings, shaders, the build report — and code | C# source, one assembly at a time |
| Code analysis | Over the player assemblies, with an inverted call hierarchy for each finding | Syntax and semantics, per file, no call graph |
| When | You run an audit | Every compile, and every keystroke in the IDE |
| Without an Editor | No | Yes — `upa-cli` in CI, no licence needed |
| Fails a build | From an Editor run you drive yourself | Yes, through a ruleset |
| Adapts to referenced packages | No | Yes — UniTask, ZString, R3, DOTween |

Everything in the left column that is not code is something this package will never do, and
the inverted call hierarchy is worth the audit on its own. Everything in the right column is
about *when* the answer arrives: a finding that reaches you in the pull request costs less
than the same finding found in a quarterly audit.

**What is not established here.** Now that the Roslyn analyzers ship in a package of their
own, they could in principle report during Unity's own compile rather than only inside an
audit — the same channel this package uses. Unity's documentation does not say either way,
and it has not been tested here. If it turns out they do, the "different cadence" note above
gets weaker for the code rules specifically, and the duplicate-noise question becomes real
rather than theoretical. Treat that row as open.

**Do not read the old repository as current.** `Unity-Technologies/ProjectAuditor` on GitHub
now says it is out of date and unsupported and points at the built-in package instead, so its
rule list is not a description of what ships today.

---

## Confidence markers

| Marker | Meaning |
|---|---|
| ● | Verified: same construct, same trigger |
| ◐ | Partial: adjacent concern, different trigger or scope |
| ○ | No equivalent found |
| ? | Believed to overlap, **not yet verified against a live install** — see Maintenance |

---

## Performance rules (UPA0001–UPA0031)

| UPA | Reports | Rider | UNT | Project Auditor | Package-native | Recommendation |
|---|---|---|---|---|---|---|
| **UPA0001** | `GetComponent` family in per-frame methods | ● *Avoid usage of GetComponent methods in performance critical context* | ◐ UNT0026 (`GetComponent` always allocates), ◐ UNT0039 (`RequireComponent` on self-invoke) | ? PAC — API database includes `GetComponent` | ○ | **Keep.** Rider is stronger as information (propagates through calls), but it cannot gate a build — and this is the rule most worth gating |
| **UPA0002** | `name` / `tag` accessed in per-frame methods | ◐ *Use CompareTag instead of explicit string comparison* — narrower | ◐ UNT0002 *Inefficient tag comparison* — narrower | ? PAC | ○ | Keep. Both alternatives only cover the comparison shape; `name` access and bare `tag` reads are not covered by either |
| **UPA0003** | String-based shader / animator property access | ● *Avoid using string based names for setting and getting properties on Animators, Shaders and Materials* | ● UNT0041 (`Animator.StringToHash` for repeated calls) — repeat-call heuristic only | ? PAC | ○ | Overlap is narrower than it looks. **Keep.** UNT0041 sees only `Animator`; measured across three real games, one of fourteen UPA0003 findings was an Animator call and it was a false positive, while all three true positives were `Material` and `MaterialPropertyBlock` calls UNT0041 cannot see (2026-08-11) |
| **UPA0004** | Instantiating accessors (`Renderer.material`, …) in per-frame | ○ | ○ | ? PAC (material instantiation is a known descriptor) | ○ | **Keep.** Distinctive rule; the leak, not just the cost, is the point |
| **UPA0005** | Direct `Debug.Log` calls *(off by default)* | ● *Avoid usage of Debug.Log methods in performance critical context* — hot-path only | ○ | ◐ | ○ | Different scope: UPA0005 is not hot-path-limited. Off by default anyway; enable deliberately or not at all |
| **UPA0006** | Reference-type allocation / boxing in per-frame | ○ | ○ | ● PAC boxing/object allocation diagnostics | ○ | **Keep.** Project Auditor covers this well but only in a batch Editor run. This is the per-PR version |
| **UPA0007** | Capturing lambdas in per-frame | ○ | ○ | ◐ | ○ | **Keep.** ReSharper's Heap Allocations Viewer is a separate opt-in plugin, not part of Unity support |
| **UPA0008** | `stackalloc` inside a loop | ○ | ○ | ○ | ○ | **Keep.** No equivalent anywhere |
| **UPA0009** | `List<T>.Count` not hoisted *(off)* | ○ | ○ | ○ | ○ | Keep as-is (off by default) |
| **UPA0010** | Raycasts without explicit `maxDistance` / `layerMask` | ◐ *Avoid using allocating versions of Physics Raycast functions* — different concern (allocation) | ◐ UNT0028 *Use non-allocating physics APIs* — different concern | ? PAC | ○ | **Keep.** Nothing else checks the argument shape; note in the rule doc that UNT0028 covers the adjacent allocation issue |
| **UPA0011** | `SetActive` to toggle UI visibility *(off)* | ○ | ○ | ○ | ○ | Keep as-is |
| **UPA0012** | TMP `text` assignment instead of `SetText` *(off)* | ○ | ○ | ○ | ○ | Keep as-is |
| **UPA0013** | `System.Linq` in per-frame *(off)* | ○ | ○ | ◐ | ○ | Keep as-is. UnityEngineAnalyzer has no LINQ rule - `UEA0009` is InvokeFunctionMissing, and this page said otherwise until its rule list was actually read (2026-08-10) |
| **UPA0014** | Scene-search APIs in per-frame | ● *Avoid usage of Find methods in performance critical context* — same API set, plus quick-fixes | ○ | ? PAC | ○ | **Keep.** Rider is stronger as information, but its inspection never gates a build; this rule is the CI gate |
| **UPA0015** | `Camera.main` in per-frame *(Info)* | ● *Camera.main is inefficient in frequently called methods* — with a cache-to-`Awake` context action | ○ | ? PAC | ○ | **Keep.** Already Info severity, so low noise; Rider's inspection is IDE-only |
| **UPA0016** | `SendMessage` / `BroadcastMessage` | ● *Avoid using string based Method Invocation* | ○ | ? PAC | ○ | **Keep.** Rider's inspection is IDE-only; this one is worth gating at error |
| **UPA0017** | Array-returning `GetComponents` overloads | ◐ | ◐ UNT0026 | ? PAC | ○ | **Keep.** The `List<T>` overload advice is more specific than either |
| **UPA0018** | Allocating array-returning Unity APIs | ○ | ◐ UNT0042 (`Mesh` array property in loop) — one API, loop-scoped | ● PAC API database | ○ | **Keep.** UNT0042 is a single case of this; add a cross-reference in the rule doc |
| **UPA0019** | Value types yielded from coroutines | ○ | ○ | ○ | ○ | **Keep — flagship.** Nothing else catches this, and the failure (Unity treats the boxed value as `null`) is a correctness bug, not just an allocation |
| **UPA0020** | Lambdas in `WaitUntil` / `WaitWhile` *(off)* | ○ | ◐ UNT0038 *Cache `WaitForSeconds`* — sibling concern, different API | ○ | ○ | Keep as-is. Cross-reference UNT0038 in the rule doc |
| **UPA0021** | `magnitude` / `Distance` where `sqrMagnitude` suffices | ○ | ◐ UNT0024 *Prefer scalar over vector calculations* | ○ | ○ | **Keep.** UNT0024 is a different rewrite |
| **UPA0022** | `Enum.HasFlag` *(deprecated)* | — | — | — | — | Deprecated; excluded from all coexistence rulesets |
| **UPA0023** | `OnGUI` in player code *(Info, off)* | ◐ *base.OnGUI() will print "no GUI implemented"* — different issue | ○ | ○ | ○ | Keep as-is |
| **UPA0024** | `Resources.Load` in per-frame *(off)* | ○ | ○ | ? PAC | ○ | Keep as-is |
| **UPA0025** | Finalizers in runtime code | ○ | ○ | ○ | ◐ General C# analyzers (CA1821 covers *empty* finalizers only) | **Keep.** CA1821 is a narrower case |
| **UPA0026** | Boxing via an inherited `GetType()` on a value type | ○ | ○ | ● PAC boxing | ○ | **Keep.** Ours runs per-compile |
| **UPA0027** | `params` overloads called in expanded form | ○ | ○ | ● PAC has a params-array allocation diagnostic | ○ | **Keep.** Same finding, different cadence |
| **UPA0028** | Structs as collection keys without `IEquatable<T>` | ○ | ○ | ◐ | ○ | **Keep — flagship.** Backed by measurement; see `enum-dictionary-keys.md` |
| **UPA0029** | Copy loops replaceable with `AddRange` | ○ | ○ | ○ | ○ | **Keep** |
| **UPA0030** | Known-allocating `string` / `Enum` members in per-frame | ○ | ○ | ● PAC API database | ○ | **Keep** |
| **UPA0031** | `Instantiate` / `Destroy` in per-frame | ◐ *Avoid usage of AddComponent in performance critical code* (sibling API), ◐ *Avoid `Object.Instantiate` without Transform Parent* (different concern) | ○ | ? PAC | ○ | **Keep.** Neither Rider inspection is this rule |

---

## Correctness rules (UPA1000–UPA1001)

| UPA | Reports | Rider | UNT | Other | Recommendation |
|---|---|---|---|---|---|
| **UPA1000** | Leaf classes not sealed *(deprecated)* | — | — | UnityEngineAnalyzer had `UnsealedDerivedClass` | Deprecated after measurement; excluded from coexistence rulesets |
| **UPA1001** | Enum switches missing declared members | ○ | ○ | ● **IDE0010** / **IDE0072** (*Add missing cases*) ship with Roslyn | **Real overlap, and it is not with a Unity tool.** If IDE0010/IDE0072 are graded in your project, set `UPA1001 = none`. Differences: ours honours `upa_enum_switch_allow_default`, and unlike IDE0010 it reports through Unity's compiler, not only in the IDE |

---

## Ecosystem rules (UPA2000–UPA2032)

All off by default and package-conditional, so overlap only materialises once you both
reference the package *and* enable the rule.

| UPA | Reports | Package-native equivalent | Recommendation |
|---|---|---|---|
| **UPA2000** | String building in per-frame (ZString-aware) | ○ | **Keep.** No package-native equivalent |
| **UPA2010** | `async Task` methods (UniTask referenced) | ○ | **Keep.** Opinionated by design |
| **UPA2011** | Coroutine `IEnumerator` on MonoBehaviours (UniTask referenced) | ○ | **Keep.** Opinionated by design |
| **UPA2012** | `async void` / discarded task calls | ● **`UniTask.Analyzer`** ships with UniTask and detects unawaited `UniTask`-returning calls. Also ◐ **CS4014** (unawaited `Task`) and ◐ UNT0012 (unused coroutine return value) | **The clearest genuine duplicate in the whole set.** If you reference UniTask you already have its analyzer, so you get two diagnostics for one problem. **Recommend deferring discarded UniTask calls to `UniTask.Analyzer`.** The temporary `unitask-coexist.ruleset` does that until AF-04 moves the decision into UPA2012 itself |
| **UPA2021** | Public `Action` events modelling observable state (R3 referenced) | ○ | **Keep.** Architectural, not mechanical |
| **UPA2030** | Tweens created in per-frame (DOTween) | ○ | **Keep** |
| **UPA2031** | Discarded infinite tweens without `SetLink` | ○ | **Keep — flagship.** This is a lifetime bug, not a style preference, and DOTween ships no analyzer |
| **UPA2032** | String tween IDs *(Info)* | ○ | **Keep** |

---

## Platform rules (UPA3000–UPA3004)

| UPA | Reports | Anything else? |
|---|---|---|
| **UPA3000** | Threading APIs unsupported on WebGL | ○ |
| **UPA3001** | `System.Net.Sockets` unsupported on WebGL | ○ |
| **UPA3002** | Synchronous file IO unsupported on WebGL | ○ |
| **UPA3003** | `System.Diagnostics.Process` unsupported on WebGL | ○ |
| **UPA3004** | Blocking waits on async operations — deadlock on single-threaded WebGL | ○ |

**No overlap with anything, from anyone.** No Unity analysis tool conditions its rules on the
build target. Project Auditor takes a target platform for its analysis but does not carry this
rule set; Rider and Microsoft.Unity.Analyzers are platform-agnostic.

Never disable these when targeting WebGL. UPA3004 in particular catches a hang, not a slowdown,
and the failure appears only in a browser build.

---

## What the other tools catch that we don't

Recommending this package means recommending the tools around it. These are real gaps, listed so
nobody discovers them the hard way.

**Rider**
- Null comparisons against `UnityEngine.Object` subclasses (native call per comparison)
- Possible unintended bypass of the engine-object lifetime check
- Multidimensional array access inefficiency
- Multiplication order (`float * Vector3` vs `Vector3 * float`)
- Redundant Unity event functions (empty message bodies)
- Redundant `SerializeField` / `HideInInspector` / `InitializeOnLoad` / `FormerlySerializedAs` attributes
- `Object.Instantiate` without a parent followed by `SetParent`
- Shader keyword enabling
- **Call-graph propagation for everything above** — the capability difference, not a rule difference

**Microsoft.Unity.Analyzers**
- Correctness and type safety broadly: message signatures (UNT0006, UNT0033), `InitializeOnLoad` (UNT0009, UNT0015), `SerializeField` validity (UNT0013), `MenuItem` on non-static (UNT0020), `Destroy` on a `Transform` (UNT0030), conditional compilation typos (UNT0043)
- Null coalescing / propagation / pattern matching on Unity objects (UNT0007, UNT0008, UNT0023, UNT0029)
- Transform get/set position+rotation efficiency (UNT0022, UNT0032, UNT0036, UNT0037)
- Empty Unity messages (UNT0001), `SetPixels` (UNT0017), reflection in hot messages (UNT0018)
- **23 diagnostic suppressors** (`USP0001`–`USP0023`) that stop general C# analyzers producing nonsense on Unity code — serialized fields flagged as unused, messages flagged as removable, and so on. **This package does not replicate any of it, and you should not run Unity code without it.**

**Project Auditor**
- Everything outside code: asset import settings, project settings, shaders, build report
- Code analysis over the player assemblies, which sees the compiled result rather than the syntax tree
- Inverted call hierarchy for each finding

Install all of them. This package is designed to sit alongside, not instead.

---

## Coexistence rulesets

Shipped in the **Ruleset Presets** sample as `.ruleset` files.

**The direction is the opposite of what you would expect, and it matters.** Each coexistence
ruleset **includes** the base preset rather than being included by it, because a rule entry in
the including file beats the same entry in an included file — and every base preset grades
every rule. A file written to be included by a preset silences nothing, while looking
completely correct. That was measured with `upa-cli` before these shipped: a base grading
UPA0001 as Warning and including an overlay that set it to `None` still reported UPA0001;
inverting the two silenced it.

So you copy the coexistence file **and** its base preset into `Assets/`, and rename the
coexistence file to `Default.ruleset`. To defer from a different base, change one `Include`
line.

### `vs-coexist.ruleset` — removed

Sets nothing to `None`. It used to defer UPA0003 to UNT0041; measurement on three real Unity
games showed that trade buys one false positive and gives up every true one, because UNT0041
only sees `Animator` and this rule's real finds are on `Material` and `MaterialPropertyBlock`.
Severities cannot be scoped to the Animator overloads, so there is no partial deferral to make.
The file is kept so paths published in earlier releases keep resolving.

Small on purpose. Microsoft.Unity.Analyzers is mostly correctness and suppressors; the actual
performance overlap is one rule.

### `unitask-coexist.ruleset` — includes `ci`

Sets to `None`: **UPA2012** (defers to `UniTask.Analyzer`).

This one includes `ci`, because that is the only preset
that turns UPA2012 on — over any other base the file would silence something already silent.

---

## Maintenance

This page is a claim about other people's software, so it needs the same treatment as any other
claim in this repository.

**Open verification items**

- [ ] Every `?` in the tables: confirm against a live Project Auditor install (Unity 6.4+, with
      `com.unity.project-auditor-rules`) and record the actual `PAC####` id, not a description
- [ ] Whether the rules package's Roslyn analyzers report during Unity's own compile, or only
      inside an audit. Undocumented either way, and it decides whether the cadence argument
      above still holds for the code rules
- [x] That Project Auditor is built into Unity 6.4+, and that `com.unity.project-auditor-rules`
      is a real package the tool now needs: checked against Unity's package documentation and
      the rules package changelog on 2026-08-10. Both were written on this page before anyone
      had looked
- [ ] Confirm `UniTask.Analyzer`'s diagnostic id and exact trigger conditions for UPA2012
- [ ] Confirm whether IDE0010 / IDE0072 fire under Unity's compiler or IDE-only, which decides
      how strongly to recommend disabling UPA1001
- [ ] Re-check Rider's inspection list per major release — JetBrains adds inspections regularly

**Ownership**

- Review each release; Rider and Microsoft.Unity.Analyzers both ship several times a year
- When adding a UPA rule, add its row here. Asserted by the tests: a rule with no row in
  either language fails the build
- Both languages are checked for the same per-rule coverage

**Sources**

- Microsoft.Unity.Analyzers rule and suppressor index: `microsoft/Microsoft.Unity.Analyzers`, `doc/index.md`
- Rider inspections: `JetBrains/resharper-unity` wiki, *Performance critical context and costly methods* and linked pages
- Project Auditor: the `com.unity.project-auditor` manual (which states the Unity 6.4 inclusion),
  and the `com.unity.project-auditor-rules` changelog (which states what moved into it and why)
