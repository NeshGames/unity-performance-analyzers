using System.Threading.Tasks;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Xunit;

namespace UnityPerformanceAnalyzers.Tests
{
    public class UPA0023OnGuiDeclarationAnalyzerTests
    {
        private static Task VerifyAsync(string source, string? assemblyName = null)
        {
            var test = new CSharpAnalyzerTest<UPA0023OnGuiDeclarationAnalyzer, DefaultVerifier>
            {
                TestCode = source,
                ReferenceAssemblies = ReferenceAssemblies.NetStandard.NetStandard20,
            };
            test.TestState.AdditionalReferences.Add(typeof(UnityEngine.MonoBehaviour).Assembly);
            if (assemblyName is object)
            {
                test.SolutionTransforms.Add((solution, projectId) =>
                    solution.WithProjectAssemblyName(projectId, assemblyName));
            }

            // UPA0023 is disabled by default; enable it the same way a preset would.
            test.TestState.AnalyzerConfigFiles.Add(("/.editorconfig", @"
root = true

[*.cs]
dotnet_diagnostic.UPA0023.severity = warning
"));
            return test.RunAsync();
        }

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
