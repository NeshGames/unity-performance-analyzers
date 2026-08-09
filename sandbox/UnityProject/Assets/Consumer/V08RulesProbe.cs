using UnityEngine;

// UPA0031, in a real Unity compilation rather than a test harness. The positive half uses the
// receiverless inherited form, which is the only form most projects ever write and the one a
// syntax-shaped implementation would miss.
public class V08RulesProbe : MonoBehaviour
{
    public GameObject prefab;
    public GameObject spawned;

    void Update()
    {
        var copy = Instantiate(prefab);      // UPA0031 - create
        Destroy(copy, 2f);                   // UPA0031 - destroy
    }

    // The negative half. Building and tearing down outside a per-frame path is ordinary.
    void Start()
    {
        spawned = Instantiate(prefab);
    }

    void OnDestroy()
    {
        Destroy(spawned);
    }
}
