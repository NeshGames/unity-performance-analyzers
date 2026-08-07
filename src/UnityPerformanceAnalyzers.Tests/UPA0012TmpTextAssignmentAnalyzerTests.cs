using System.Threading.Tasks;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Xunit;

namespace UnityPerformanceAnalyzers.Tests
{
    public class UPA0012TmpTextAssignmentAnalyzerTests
    {
        private static Task VerifyAsync(string source)
        {
            var test = new CSharpAnalyzerTest<UPA0012TmpTextAssignmentAnalyzer, DefaultVerifier>
            {
                TestCode = source,
                ReferenceAssemblies = ReferenceAssemblies.NetStandard.NetStandard20,
            };
            test.TestState.AdditionalReferences.Add(typeof(UnityEngine.MonoBehaviour).Assembly);
            // UPA0012 is disabled by default; enable it the same way a preset would.
            test.TestState.AnalyzerConfigFiles.Add(("/.editorconfig", @"
root = true

[*.cs]
dotnet_diagnostic.UPA0012.severity = warning
"));
            return test.RunAsync();
        }

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
