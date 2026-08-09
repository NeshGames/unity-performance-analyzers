using System.Threading.Tasks;
using Xunit;

namespace UnityPerformanceAnalyzers.Tests
{
    public class UPA0023OnGuiDeclarationAnalyzerTests
    {
        private static Task VerifyAsync(string source, string? assemblyName = null) =>
            RuleVerifier.VerifyAsync<UPA0023OnGuiDeclarationAnalyzer>(source, new RuleHarness
            {
                AssemblyName = assemblyName,
                EnabledRules = { "UPA0023" },
            });

        // UPA0023 test case 1
        [Fact]
        public Task OnGui_InPlayerMonoBehaviour_Triggers()
        {
            return VerifyAsync(@"
using UnityEngine;

class C : MonoBehaviour
{
    void {|UPA0023:OnGUI|}()
    {
    }
}");
        }

        // UPA0023 test case 2 — editor assemblies detected by name; reference-based
        // detection is useless because Unity injects UnityEditor refs into every
        // in-editor compilation
        [Fact]
        public Task OnGui_InAssemblyCSharpEditor_DoesNotTrigger()
        {
            return VerifyAsync(@"
using UnityEngine;

class C : MonoBehaviour
{
    void OnGUI()
    {
    }
}", assemblyName: "Assembly-CSharp-Editor");
        }

        [Fact]
        public Task OnGui_InDotEditorSuffixedAssembly_DoesNotTrigger()
        {
            return VerifyAsync(@"
using UnityEngine;

class C : MonoBehaviour
{
    void OnGUI()
    {
    }
}", assemblyName: "MyGame.Tools.Editor");
        }

        // UPA0023 test case 3
        [Fact]
        public Task OnGui_OnNonMonoBehaviour_DoesNotTrigger()
        {
            return VerifyAsync(@"
class C
{
    void OnGUI()
    {
    }
}");
        }

        [Fact]
        public Task OnGuiWithParameters_DoesNotTrigger()
        {
            return VerifyAsync(@"
using UnityEngine;

class C : MonoBehaviour
{
    void OnGUI(int id)
    {
    }
}");
        }
    }
}
