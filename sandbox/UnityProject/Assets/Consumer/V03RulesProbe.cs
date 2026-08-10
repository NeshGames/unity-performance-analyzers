using System;
using System.Collections;
using UnityEngine;

// Deliberate violations of the twelve v0.3 Unity performance rules; each line notes
// the diagnostic the analyzer must report when this assembly compiles.
[Flags]
public enum ProbeState
{
    None = 0,
    Dead = 1,
}

public class V03RulesProbe : MonoBehaviour
{
    Vector3 _velocity;
    ProbeState _state;
    bool _ready;

    void Start()
    {
        gameObject.SendMessage("OnProbe");                        // UPA0016 (global, any method)
    }

    void Update()
    {
        var player = GameObject.Find("Player");                   // UPA0014
        var cam = Camera.main;                                    // UPA0015
        var bodies = GetComponents<Transform>();                  // UPA0017
        var touchCount = Input.touches.Length;                    // UPA0018
        if (_velocity.magnitude < 5f)                             // UPA0021
        {
        }

        // Negative now, and deliberately kept: UPA0022 is deprecated (HasFlag allocates
        // nothing on any supported runtime) and UPA0006 does not report the argument box
        // either, because the runtime removes it along with the call.
        if (_state.HasFlag(ProbeState.Dead))                      // no diagnostic
        {
        }

        // Also negative since UPA0026 narrowed to GetType: the receiver box measured 0.00
        // B/op for ToString on IL2CPP.
        var stateLabel = _state.ToString();                       // no diagnostic
        _ = stateLabel;

        var stateType = _state.GetType();                         // UPA0026 (enum receiver)
        _ = stateType;

        var icon = Resources.Load<TextAsset>("missing");          // UPA0024
        var hud = $"touches {touchCount}";                        // UPA0006 (interpolation hole boxing)
        _ = (player, cam, bodies, touchCount, icon, hud);
    }

    IEnumerator FadeOut()
    {
        yield return 0;                                           // UPA0019
        yield return new WaitUntil(() => _ready);                 // UPA0020
    }

    void OnGUI()                                                  // UPA0023
    {
    }

    ~V03RulesProbe()                                              // UPA0025
    {
    }
}
