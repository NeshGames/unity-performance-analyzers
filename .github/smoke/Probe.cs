using System.Collections.Generic;
using UnityEngine;

// The positive half of the load smoke test. Every rule marked "expect" below must be
// reported when this file is compiled with the analyzer loaded, and every rule marked
// "expect-none" must stay silent. The assertion script reads those markers out of this
// file, so a probe that stops triggering a rule cannot quietly stop asserting it.
//
// Keep the violations one per line: the assertions match on the rule ID, and a line
// carrying two of them makes a failure harder to read.
//
// expect-none UPA0005   Debug.Log is None under the recommended preset. Reporting it
//                       would mean the ruleset never reached the compiler.
public class Probe : MonoBehaviour
{
    void Update()
    {
        var component = GetComponent<Transform>();   // expect UPA0001
        var objectName = gameObject.name;            // expect UPA0002
        var buffer = new List<int>();                // expect UPA0006
        Debug.Log("probe");

        _ = component;
        _ = objectName;
        _ = buffer;
    }
}
