using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Xunit;

namespace UnityPerformanceAnalyzers.Tests
{
    public class UPA3004BlockingWaitAnalyzerTests
    {
        // Minimal stand-in for the Addressables handle types; the analyzer matches them by
        // metadata name, so declaring them in the test compilation is equivalent to having
        // the package referenced.
        private const string AddressablesStub = @"
namespace UnityEngine.ResourceManagement.AsyncOperations
{
    public struct AsyncOperationHandle
    {
        public object WaitForCompletion() => null;
    }

    public struct AsyncOperationHandle<TObject>
    {
        public TObject WaitForCompletion() => default;
    }
}
";

        // The UPA_TARGET_WEBGL define is simulated through parse options.
        private class WebGlTest : CSharpAnalyzerTest<UPA3004BlockingWaitAnalyzer, DefaultVerifier>
        {
            public bool DefineWebGlTarget { get; set; } = true;

            protected override ParseOptions CreateParseOptions()
            {
                var options = (CSharpParseOptions)base.CreateParseOptions();
                return DefineWebGlTarget
                    ? options.WithPreprocessorSymbols(UpaProfile.WebGlDefine)
                    : options;
            }
        }

        private static Task VerifyAsync(string source, bool defineWebGlTarget = true, bool includeAddressablesStub = false)
        {
            var test = new WebGlTest
            {
                TestCode = includeAddressablesStub ? source + AddressablesStub : source,
                ReferenceAssemblies = ReferenceAssemblies.NetStandard.NetStandard20,
                DefineWebGlTarget = defineWebGlTarget,
            };
            // UPA3004 is disabled by default; enable it as the webgl-addon preset would.
            test.TestState.AnalyzerConfigFiles.Add(("/.editorconfig", @"
root = true

[*.cs]
dotnet_diagnostic.UPA3004.severity = warning
"));
            return test.RunAsync();
        }

        // UPA3004 test case 1 — Addressables WaitForCompletion, plain and generic handles
        [Fact]
        public Task WaitForCompletion_WithDefine_Triggers()
        {
            return VerifyAsync(@"
using UnityEngine.ResourceManagement.AsyncOperations;

class C
{
    void M(AsyncOperationHandle handle, AsyncOperationHandle<string> typed)
    {
        handle.{|UPA3004:WaitForCompletion|}();
        typed.{|UPA3004:WaitForCompletion|}();
    }
}", includeAddressablesStub: true);
        }

        // UPA3004 test case 2 — Task.Wait and the static WaitAll/WaitAny
        [Fact]
        public Task TaskWaitFamily_Triggers()
        {
            return VerifyAsync(@"
using System.Threading.Tasks;

class C
{
    void M(Task task, Task[] tasks)
    {
        task.{|UPA3004:Wait|}();
        Task.{|UPA3004:WaitAll|}(tasks);
        Task.{|UPA3004:WaitAny|}(tasks);
    }
}");
        }

        // UPA3004 test case 3 — Task<T>.Result property access
        [Fact]
        public Task TaskResult_Triggers()
        {
            return VerifyAsync(@"
using System.Threading.Tasks;

class C
{
    int M(Task<int> task)
    {
        return task.{|UPA3004:Result|};
    }
}");
        }

        // UPA3004 test case 4 — the GetAwaiter().GetResult() idiom, configured included
        [Fact]
        public Task GetAwaiterGetResult_Triggers()
        {
            return VerifyAsync(@"
using System.Threading.Tasks;

class C
{
    int M(Task<int> task)
    {
        task.GetAwaiter().{|UPA3004:GetResult|}();
        return task.ConfigureAwait(false).GetAwaiter().{|UPA3004:GetResult|}();
    }
}");
        }

        // UPA3004 test case 5 — awaiting is the recommended form and stays silent
        [Fact]
        public Task Await_DoesNotTrigger()
        {
            return VerifyAsync(@"
using System.Threading.Tasks;

class C
{
    async Task<int> M(Task<int> task)
    {
        await Task.Yield();
        return await task;
    }
}");
        }

        // UPA3004 test case 6 — without the define nothing registers
        [Fact]
        public Task WithoutDefine_DoesNotTrigger()
        {
            return VerifyAsync(@"
using System.Threading.Tasks;

class C
{
    int M(Task<int> task)
    {
        task.Wait();
        return task.Result;
    }
}", defineWebGlTarget: false);
        }

        // UPA3004 test case 7 — Task group works without the Addressables types
        [Fact]
        public Task TaskGroup_WorksWithoutAddressables()
        {
            return VerifyAsync(@"
using System.Threading.Tasks;

class C
{
    void M(Task task)
    {
        task.{|UPA3004:Wait|}();
    }
}");
        }

        // UPA3004 test case 8 — same-named members on user types stay silent
        [Fact]
        public Task UserTypes_DoNotTrigger()
        {
            return VerifyAsync(@"
class Handle
{
    public int Result => 0;

    public void Wait() { }

    public void WaitForCompletion() { }
}

class C
{
    int M(Handle handle)
    {
        handle.Wait();
        handle.WaitForCompletion();
        return handle.Result;
    }
}");
        }
    }
}
