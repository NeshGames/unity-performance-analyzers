# Enum dictionary keys — an optimization that no longer applies

> English | [繁體中文](enum-dictionary-keys.zh-TW.md)

**This page is not a rule.** It documents advice this project evaluated, measured, and
decided not to turn into a rule. No `UPA` number is assigned to it.

## The advice

You will still find this in Unity performance guides, forum answers and code review
checklists:

> `Dictionary<TEnum, TValue>` boxes on every lookup, because `EqualityComparer<TEnum>.Default`
> falls back to the object comparer for enums. Supply your own `IEqualityComparer<TEnum>`, or
> use the underlying integral type as the key.

It has a real origin. Unity's 2017.3 manual page on understanding the managed heap described
exactly this, complete with a hand-written comparer as the fix. That page targeted the .NET
3.5-era Mono runtime Unity shipped at the time.

## What we measured

The claim is about runtime behaviour, so it can be checked. This project built a measurement
harness in its sandbox and ran it across every combination in its supported range:

| Unity | Scripting backend | API compatibility level | Platform |
|---|---|---|---|
| 2022.3.62f2 | Mono | .NET Standard 2.1 | Editor |
| 2022.3.62f2 | Mono | .NET Framework | Editor |
| 6000.5.3f1 | Mono | .NET Standard 2.1 | Editor |
| 2022.3.62f2 | IL2CPP | .NET Standard 2.1 | Standalone x64 player |
| 6000.5.3f1 | IL2CPP | .NET Standard 2.1 | Standalone x64 player |

Two independent signals were taken. The first is the concrete type behind
`EqualityComparer<T>.Default`, which names its own behaviour and cannot be confused by
garbage collector noise. The second is the allocation delta over 200,000 `ContainsKey` calls,
reported alongside the gen-0 collection count so a partial figure identifies itself.

All five combinations agreed:

| Key type | `EqualityComparer<T>.Default` resolves to | 200,000 lookups |
|---|---|---|
| `enum` (int-backed) | `EnumEqualityComparer<T>` | 0 bytes, 0 collections |
| `enum` (byte-backed) | `EnumEqualityComparer<T>` | 0 bytes, 0 collections |
| `enum` (long-backed) | `LongEnumEqualityComparer<T>` | 0 bytes, 0 collections |
| `int` (control) | `GenericEqualityComparer<int>` | 0 bytes, 0 collections |
| `enum` with explicit comparer | (the supplied comparer) | 0 bytes, 0 collections |

The runtime has dedicated non-boxing comparers for enums, specialized further by the size of
the underlying type. There is nothing to fix.

IL2CPP was the combination worth checking hardest: its full generic sharing could in
principle instantiate `EqualityComparer<T>.Default` differently from Mono, and the player is
where the advice would matter. It does not.

## What we found instead

The same run measured struct keys, and there the cost is real:

| Key type | `EqualityComparer<T>.Default` resolves to | 200,000 lookups |
|---|---|---|
| struct without `IEquatable<T>` | `ObjectEqualityComparer<T>` | allocates (9–20 gen-0 collections) |
| struct with `IEquatable<T>` and `GetHashCode` | `GenericEqualityComparer<T>` | 0 bytes, 0 collections |

So the underlying concern — that the default comparer can fall back to a boxing path — is
sound. It simply does not apply to the type everyone repeats it about. If you are auditing a
codebase for this class of problem, look at your struct keys, not your enum keys.

That case **is** a rule: see [UPA0028](UPA0028.md).

## Why this page exists rather than a disabled rule

A rule that never fires costs a listing entry and invites someone to turn it on. A short
explanation of why the advice is obsolete is more useful than a switch nobody should flip,
and it gives the finding somewhere to live: this measurement is the kind of thing that is
easy to assume and rarely checked.

If a future Unity release changes this, the harness that produced these numbers is in the
repository under `sandbox/UnityProject/Assets/Measurement/`, and re-running it is the way to
reopen the question.

## Related rules

- [UPA0028](UPA0028.md) — value type used as collection key without `IEquatable<T>`, the
  version of this concern that measurement supports.
- [UPA0026](UPA0026.md) — boxing from calling an inherited method on a value type receiver,
  including on enums.
