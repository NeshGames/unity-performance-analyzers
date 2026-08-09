# `DestroyImmediate` at runtime — why there is no rule for it

> English | [繁體中文](destroy-immediate-at-runtime.zh-TW.md)

**This page is not a rule.** It documents a genuine problem this project evaluated and
decided not to turn into a rule, and why. No `UPA` number is assigned to it.

## The problem is real

Unity's own documentation is direct about it:

> You should never call `DestroyImmediate` on any object in a game. Use `Object.Destroy`
> instead.

`Destroy` marks the object and tears it down after the current update loop finishes.
`DestroyImmediate` does it there and then, in the middle of whatever was running. Two
consequences follow:

- Anything iterating a collection of objects while destroying from it — a common shape in
  cleanup code — mutates the collection under its own feet.
- In the editor it destroys the *asset*, not a scene instance, when handed one. That is the
  behaviour it exists for, and it is destructive in a way `Destroy` is not.

Nothing in [Microsoft.Unity.Analyzers](https://github.com/microsoft/Microsoft.Unity.Analyzers)
covers it. `UNT0030` sounds close — "Calling `Destroy` or `DestroyImmediate` on a
`Transform`" — but it is about the argument, not about which method was called. Across all
forty-three `UNT` rules there is no other candidate.

## Why it is not a rule here

Editor code uses `DestroyImmediate` correctly and constantly. Narrowing by assembly name
handles the obvious half of that — `UPA0023` and `UPA0025` already decide "is this editor
code?" from the assembly name, for reasons set out on those pages. What it does not handle
is the other half:

```csharp
public class Spawner : MonoBehaviour     // Assembly-CSharp — a player assembly
{
#if UNITY_EDITOR
    void Reset()
    {
        DestroyImmediate(stalePreview);   // correct, and common
    }
#endif
}
```

That is a player assembly, so the assembly-name check does not exclude it. And when the
analyzer runs — in the editor, which is where anyone sees its output — `UNITY_EDITOR` **is
defined**, so the guarded code is live, parsed, and reported.

Detecting it is possible: Roslyn keeps directive trivia in the tree, so a rule could ask
whether a node sits inside an active `#if UNITY_EDITOR` region. No rule in this project
decides anything that way today, and the machinery would be new — new code, and a new way
for a rule to be wrong.

Set against that: calling `DestroyImmediate` at runtime is not a common mistake. It is a
method people reach for deliberately, usually after reading what it does. A rule that fires
on correct editor code the first time a project builds is a rule that gets switched off, and
a rule that is switched off is worth nothing. This project's 1.0 bar is noise, not rule
count.

## What to do instead

- In runtime code, call `Object.Destroy`. If the object must be gone before the next line
  runs, that is usually a sign the code should be restructured rather than a reason to
  destroy synchronously.
- In editor code, `DestroyImmediate` is the correct call. Remember that on a prefab or an
  asset it destroys the asset, and that the change goes through the undo system only if you
  route it there.
- If you want this checked mechanically, a project-local analyzer or a review checklist can
  do it with the narrow assumptions your own codebase allows — which is exactly the freedom a
  shipped rule does not have.

## Related

- [UPA0031](UPA0031.md) reports `Instantiate` and `Destroy` on a per-frame path. It
  deliberately excludes `DestroyImmediate`: both of its messages point at an object pool, and
  pooling is not the answer here.
