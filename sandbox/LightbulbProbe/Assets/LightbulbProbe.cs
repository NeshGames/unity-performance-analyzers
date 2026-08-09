using System;
using System.Collections;
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

    // UPA0022 — Enum.HasFlag in a per-frame method.
    // Expected fix: rewrite to a bitwise check, (active & Layers.Water) == Layers.Water.
    //
    // This line also reports UPA0006 for the boxing, which has no fix by design. A warning
    // with no lightbulb is not a failure here; see the README.
    private void Update()
    {
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

    private void Start()
    {
        StartCoroutine(WaitOneFrame());
    }
}
