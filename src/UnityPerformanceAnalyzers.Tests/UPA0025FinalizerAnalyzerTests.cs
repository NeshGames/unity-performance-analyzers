using System.Threading.Tasks;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Xunit;

namespace UnityPerformanceAnalyzers.Tests
{
    public class UPA0025FinalizerAnalyzerTests
    {
        private static Task VerifyAsync(string source, string? assemblyName = null)
        {
            var test = new CSharpAnalyzerTest<UPA0025FinalizerAnalyzer, DefaultVerifier>
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

            return test.RunAsync();
        }

        // UPA0025 test case 1
        [Fact]
        public Task Finalizer_InRuntimeType_Triggers()
        {
            return VerifyAsync(@"
class MyClass
{
    ~{|UPA0025:MyClass|}()
    {
    }
}");
        }

        // UPA0025 test case 2 — editor assemblies detected by name; reference-based
        // detection is useless because Unity injects UnityEditor refs into every
        // in-editor compilation
        [Fact]
        public Task Finalizer_InAssemblyCSharpEditor_DoesNotTrigger()
        {
            return VerifyAsync(@"
class MyClass
{
    ~MyClass()
    {
    }
}", assemblyName: "Assembly-CSharp-Editor");
        }

        [Fact]
        public Task Finalizer_InDotEditorSuffixedAssembly_DoesNotTrigger()
        {
            return VerifyAsync(@"
class MyClass
{
    ~MyClass()
    {
    }
}", assemblyName: "MyGame.Tools.Editor");
        }

        // UPA0025 test case 3
        [Fact]
        public Task DisposableWithoutFinalizer_DoesNotTrigger()
        {
            return VerifyAsync(@"
using System;

class MyClass : IDisposable
{
    public void Dispose()
    {
    }
}");
        }
    }
}
