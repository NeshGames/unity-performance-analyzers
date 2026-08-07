using System.Collections.Generic;
using UnityEngine;

// Intentionally violates several UPA rules so you can verify the analyzer is loaded
// and your preset is in effect. Import the sample, let the project compile, and look
// for UPA diagnostics in the Console / build log — then delete this file.
//
// Expected under the "recommended" preset (warnings): UPA0001, UPA0002, UPA0006.
// UPA0005 and UPA2000 stay silent until "strict" / "cysharp-stack".
// If nothing at all is reported, the analyzer DLL is not reaching the compiler.
public class SmokeTest : MonoBehaviour
{
    string label = "";

    void Update()
    {
        var cached = GetComponent<Transform>();      // UPA0001: component lookup per frame
        var objectName = gameObject.name;            // UPA0002: name allocates per access
        var buffer = new List<int>();                // UPA0006: hot-path allocation
        label = "frame: " + Time.frameCount;         // UPA2000 (+ boxing via UPA0006)
        Debug.Log(label);                            // UPA0005: direct Debug.Log

        _ = cached;
        _ = objectName;
        _ = buffer;
    }
}
