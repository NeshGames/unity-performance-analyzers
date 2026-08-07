using UnityEngine;

// Deliberate rule violation used to verify the analyzer reaches this assembly even
// though nothing references it.
public class IsolatedBehaviour : MonoBehaviour
{
    void Start()
    {
        Debug.Log("Isolated assembly compiled");
    }
}
