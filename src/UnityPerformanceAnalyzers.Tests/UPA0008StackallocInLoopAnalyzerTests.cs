using System.Threading.Tasks;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Xunit;

namespace UnityPerformanceAnalyzers.Tests
{
    public class UPA0008StackallocInLoopAnalyzerTests
    {
        private static Task VerifyAsync(string source)
        {
            var test = new CSharpAnalyzerTest<UPA0008StackallocInLoopAnalyzer, DefaultVerifier>
            {
                TestCode = source,
                ReferenceAssemblies = ReferenceAssemblies.NetStandard.NetStandard20,
            };
            test.SolutionTransforms.Add((solution, projectId) =>
            {
                var options = (CSharpCompilationOptions)solution.GetProject(projectId)!.CompilationOptions!;
                return solution.WithProjectCompilationOptions(projectId, options.WithAllowUnsafe(true));
            });
            return test.RunAsync();
        }

        // UPA0008 test case 1
        [Fact]
        public Task Stackalloc_InForLoop_Triggers()
        {
            return VerifyAsync(@"
class C
{
    unsafe void M()
    {
        for (int i = 0; i < 10; i++)
        {
            int* p = {|UPA0008:stackalloc int[16]|};
        }
    }
}");
        }

        // UPA0008 test case 2
        [Fact]
        public Task Stackalloc_AtMethodTopLevel_DoesNotTrigger()
        {
            return VerifyAsync(@"
class C
{
    unsafe void M()
    {
        int* p = stackalloc int[16];
    }
}");
        }

        // UPA0008 test case 3
        [Fact]
        public Task Stackalloc_InForeach_Triggers()
        {
            return VerifyAsync(@"
class C
{
    unsafe void M(int[] items)
    {
        foreach (var item in items)
        {
            int* p = {|UPA0008:stackalloc int[16]|};
        }
    }
}");
        }

        // UPA0008 test case 4
        [Fact]
        public Task Stackalloc_InWhile_Triggers()
        {
            return VerifyAsync(@"
class C
{
    unsafe void M(bool condition)
    {
        while (condition)
        {
            int* p = {|UPA0008:stackalloc int[16]|};
            condition = false;
        }
    }
}");
        }

        // UPA0008 test case 5 — nested function boundary resets the lifetime
        [Fact]
        public Task Stackalloc_InLocalFunctionInsideLoop_DoesNotTrigger()
        {
            return VerifyAsync(@"
class C
{
    unsafe void M()
    {
        for (int i = 0; i < 10; i++)
        {
            Fill();

            void Fill()
            {
                int* p = stackalloc int[16];
            }
        }
    }
}");
        }
    }
}
