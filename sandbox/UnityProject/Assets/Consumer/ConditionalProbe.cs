using System;
using System.Collections;
using System.Threading.Tasks;
using UnityEngine;

// Conditional-rule probe. Uses only BCL Task types so the same code compiles with and
// without UniTask installed; what changes between the two scenarios is which rules
// register (UPA2010) and which advice sentence UPA2012 picks.
public class ConditionalProbe : MonoBehaviour
{
    public event Action<int> ScoreChanged;           // UPA2021 only when R3 is referenced (it is not)

    async Task LoadAsync()                           // UPA2010 only when UniTask is referenced
    {
        await Task.Yield();
    }

    async void FireAndForget()                       // UPA2012 form A (advice switches on UniTask)
    {
        await Task.Yield();
    }

    IEnumerator FadeRoutine()                        // UPA2011 (unconditional)
    {
        yield return null;
    }

    void Kick()
    {
        LoadAsync();                                 // UPA2012 form B
        FireAndForget();
        ScoreChanged?.Invoke(0);
        StartCoroutine(FadeRoutine());
    }

    void Update()
    {
        var label = "score: " + Time.frameCount;     // UPA2000 (unconditional; advice switches on ZString)
        _ = label;
        Kick();
    }
}
