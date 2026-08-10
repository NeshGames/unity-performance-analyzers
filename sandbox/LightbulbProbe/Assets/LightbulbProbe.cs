using System;
using System.Collections;
using DG.Tweening;
using UnityEngine;

// Every rule that ships a code fix, in the exact shapes the fix tests prove work. Open this
// file in Rider or Visual Studio, put the caret on each underlined span, and press Alt+Enter
// (Rider) or Ctrl+. (Visual Studio): the rewrite named above it should be offered.
//
// See ../README.md for what each way this can fail looks like.
public class LightbulbProbe : MonoBehaviour
{
  [Flags]
  private enum Layers
  {
    None = 0,
    Ground = 1,
    Water = 2,
  }

  [SerializeField]
  private Layers active = Layers.Ground;

  [SerializeField]
  private Vector3 v;

  [SerializeField]
  private Vector3 a;

  [SerializeField]
  private Vector3 b;

  [SerializeField]
  private Vector3 spin;

  [SerializeField]
  private int[] seed = new int[0];

  private readonly System.Collections.Generic.List<int> items = new System.Collections.Generic.List<int>();

  private int total;

  private string label;

  // UPA0026 — GetType on a value-type receiver in a per-frame method.
  // Expected fix: typeof(Layers).
  //
  // The HasFlag line below it is deliberately here and must stay silent: UPA0022 is
  // deprecated and UPA0006 does not report the argument box either, because the runtime
  // removes it with the call. A lightbulb - or a warning at all - on that line is a
  // failure of this probe.
  private void Update()
  {
    var kind = active.GetType();
    _ = kind;

    if (active.HasFlag(Layers.Water))
    {
      enabled = true;
    }
  }

  // UPA0021 — magnitude compared against a literal.
  // Expected fix: square both sides, v.sqrMagnitude < 25f.
  private void CheckMagnitude()
  {
    if (v.magnitude < 5f)
    {
      enabled = true;
    }
  }

  // UPA0021 — the other shape, with its own rewrite.
  // Expected fix: (a - b).sqrMagnitude <= 25f.
  private void CheckDistance()
  {
    if (Vector3.Distance(a, b) <= 5f)
    {
      enabled = true;
    }
  }

  // UPA0019 — boxed value yielded from a coroutine.
  // Expected fix: yield null instead.
  private IEnumerator WaitOneFrame()
  {
    yield return 0;
  }

  // UPA0009 — Count re-read on every iteration of a per-frame loop. The rule is hot-path
  // only, so this has to sit in Update to report; the recommended preset the probe installs
  // turns it on.
  // Expected fix: int itemsCount = items.Count; declared before the loop.
  private void LateUpdate()
  {
    for (int i = 0; i < items.Count; i++)
    {
      total += items[i];
    }
  }

  // UPA0029 — a copy loop whose source is an array, the one source that cannot be the
  // list being appended to.
  // Expected fix: items.AddRange(seed).
  private void Seed()
  {
    foreach (var value in seed)
    {
      items.Add(value);
    }
  }

  // UPA2031 — an infinite tween nothing holds, so nothing can ever kill it.
  // Expected fix: append .SetLink(gameObject).
  private void Spin()
  {
    transform.DORotate(spin, 2f).SetLoops(-1);
  }

  // UPA2000 — string building on a per-frame path, with an operand that is not a string:
  // the shape the measurement says ZString helps with. The rule is hot-path scoped, so this
  // has to be a per-frame method to report at all.
  // Expected fix: ZString.Concat("score: ", total).
  private void FixedUpdate()
  {
    label = "score: " + total;
  }

  // UPA2012 — a UniTask nobody awaits, so its exceptions go nowhere.
  // Expected fix: append .Forget().
  private void FireAndForget()
  {
    LoadAsync();
  }

  // UPA0003 — the string is hashed to an id on every call.
  // Expected fix: a private static readonly int Alpha on this type, and the call
  // switched to the integer overload. Fix All should share one field between the two
  // calls rather than adding one each.
  private void Tint(Material mat)
  {
    mat.SetFloat("_Alpha", 0.5f);
    mat.SetFloat("_Alpha", 1f);
  }

  private Cysharp.Threading.Tasks.UniTask LoadAsync()
  {
    return default;
  }

  private void Start()
  {
    StartCoroutine(WaitOneFrame());
  }
}
