using System.Threading.Tasks;
using Xunit;

namespace UnityPerformanceAnalyzers.Tests
{
    public class UPA0012TmpTextAssignmentAnalyzerTests
    {
        private static Task VerifyAsync(string source) =>
            RuleVerifier.VerifyAsync<UPA0012TmpTextAssignmentAnalyzer>(source, new RuleHarness
            {
                EnabledRules = { "UPA0012" },
            });

        // UPA0012 test case 1
        [Fact]
        public Task TextAssignment_InUpdate_Triggers()
        {
            return VerifyAsync(@"
using TMPro;
using UnityEngine;

class C : MonoBehaviour
{
    TextMeshProUGUI label = null!;
    int score;

    void Update()
    {
        {|UPA0012:label.text|} = score.ToString();
    }
}");
        }

        // UPA0012 test case 2 — SetText is the recommended replacement
        [Fact]
        public Task SetText_InUpdate_DoesNotTrigger()
        {
            return VerifyAsync(@"
using TMPro;
using UnityEngine;

class C : MonoBehaviour
{
    TextMeshProUGUI label = null!;
    float score;

    void Update()
    {
        label.SetText(""{0}"", score);
    }
}");
        }

        // UPA0012 test case 3
        [Fact]
        public Task TextAssignment_InStart_DoesNotTrigger()
        {
            return VerifyAsync(@"
using TMPro;
using UnityEngine;

class C : MonoBehaviour
{
    TextMeshProUGUI label = null!;

    void Start()
    {
        label.text = ""Ready"";
    }
}");
        }

        // UPA0012 test case 4 — reads do not dirty the text
        [Fact]
        public Task TextRead_InUpdate_DoesNotTrigger()
        {
            return VerifyAsync(@"
using TMPro;
using UnityEngine;

class C : MonoBehaviour
{
    TextMeshProUGUI label = null!;

    void Update()
    {
        var t = label.text;
    }
}");
        }
    }
}
