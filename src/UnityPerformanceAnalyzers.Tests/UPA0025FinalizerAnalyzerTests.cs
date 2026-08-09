using System.Threading.Tasks;
using Xunit;

namespace UnityPerformanceAnalyzers.Tests
{
    public class UPA0025FinalizerAnalyzerTests
    {
        private static Task VerifyAsync(string source, string? assemblyName = null) =>
            RuleVerifier.VerifyAsync<UPA0025FinalizerAnalyzer>(source, new RuleHarness
            {
                AssemblyName = assemblyName,
            });

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
