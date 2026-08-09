using UnityEngine;

// The negative half of the load smoke test. Nothing in this file may be reported: the
// component lookup happens once, outside the hot path, and the hot path only reads the
// field. Any diagnostic pointing at this file fails the smoke test.
//
// This half is what separates "the analyzer ran" from "the analyzer ran and was right".
// A probe that only asserts positives passes just as happily when every rule fires on
// everything.
public class NoTrigger : MonoBehaviour
{
    Transform cachedTransform;

    void Start()
    {
        cachedTransform = GetComponent<Transform>();
    }

    void Update()
    {
        _ = cachedTransform;
    }
}
