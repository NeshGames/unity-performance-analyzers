using TMPro;
using UnityEngine;

// UPA0012, which until now had no sandbox coverage at all: the rule resolves TMP_Text through
// GetTypeByMetadataName, so without TextMeshPro in the project it never registers and a real
// Unity compilation could say nothing about it either way. The measurement project pulls TMP
// in as of A4, so this is the first time the rule is exercised against the real assembly
// rather than the test stubs.
//
// The negative lines matter as much as the positive one. SetText is what the rule asks for,
// and reading the property is not an assignment.
public class TextMeshProProbe : MonoBehaviour
{
    [SerializeField]
    private TMP_Text label;

    private int score;

    private void Update()
    {
        label.text = score.ToString();                            // UPA0012

        label.SetText("{0}", score);                              // no diagnostic — the fix

        var current = label.text;                                 // no diagnostic — a read
        _ = current;
    }

    private void Start()
    {
        label.text = "ready";                                     // no diagnostic — not a hot path
    }
}
