using UnityEngine;

// Deliberate rule violations used to verify the analyzer reaches this assembly.
public class ConsumerBehaviour : MonoBehaviour
{
    void Start()
    {
        Debug.Log("Consumer assembly compiled");    // UPA0005 (when enabled by the ruleset)
    }

    // UPA0006 is enabled by default at Warning, so this allocation must be reported
    // even without any severity configuration.
    void Update()
    {
        var garbage = new System.Collections.Generic.List<int>();
        garbage.Add(Time.frameCount);
    }

    // With UPA_TARGET_WEBGL defined (Assets/csc.rsp) and webgl-addon stacked via
    // <Include>, this must produce UPA3000.
    void KickOffWork()
    {
        _ = System.Threading.Tasks.Task.Run(() => { });
    }
}
