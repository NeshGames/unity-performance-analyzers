using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Xunit;

namespace UnityPerformanceAnalyzers.Tests
{
    public class UPA2012FireAndForgetAnalyzerTests
    {
        private const string UniTaskStub = @"
namespace Cysharp.Threading.Tasks
{
    struct UniTask { }

    static class UniTaskExtensions
    {
        public static void Forget(this UniTask task) { }
    }
}
";

        private static CSharpAnalyzerTest<UPA2012FireAndForgetAnalyzer, DefaultVerifier> CreateTest(
            string source,
            bool referenceUniTask = false)
        {
            // UPA2012 is disabled by default; enable it the same way a preset would.
            var harness = new RuleHarness
            {
                UnityStubs = false,
                EnabledRules = { "UPA2012" },
                // Both UPA2012 descriptors share the ID and severity; markup only needs ID + span.
                MarkupOptions = MarkupOptions.UseFirstDescriptor,
            };
            if (referenceUniTask)
            {
                harness.PackageAssemblies.Add(UpaProfile.UniTaskAssemblyName);
            }

            return RuleVerifier.CreateTest<UPA2012FireAndForgetAnalyzer>(source, harness);
        }

        // Same text on both sides asserts the diagnostic is reported and no fix is offered.
        private static Task VerifyFixAsync(string source, string fixedSource) =>
            RuleVerifier.VerifyCodeFixAsync<
                UPA2012FireAndForgetAnalyzer,
                CodeFixes.UPA2012ForgetCodeFixProvider>(source, fixedSource, new RuleHarness
                {
                    UnityStubs = false,
                    EnabledRules = { "UPA2012" },
                    MarkupOptions = MarkupOptions.UseFirstDescriptor,
                    PackageAssemblies = { UpaProfile.UniTaskAssemblyName },
                    Sources = { UniTaskStub },
                });

        // UPA2012 test case 1 — form A
        [Fact]
        public Task AsyncVoidMethod_Triggers()
        {
            return CreateTest(@"
using System.Threading.Tasks;

class C
{
    async void {|UPA2012:Fire|}()
    {
        await Task.Yield();
    }
}").RunAsync();
        }

        // UPA2012 test case 2 — event-handler signature is an established convention
        [Fact]
        public Task AsyncVoidEventHandler_DoesNotTrigger()
        {
            return CreateTest(@"
using System;
using System.Threading.Tasks;

class C
{
    async void OnClick(object sender, EventArgs e)
    {
        await Task.Yield();
    }
}").RunAsync();
        }

        // UPA2012 test case 3 — form B
        [Fact]
        public Task DiscardedTaskInvocation_Triggers()
        {
            return CreateTest(@"
using System.Threading.Tasks;

class C
{
    Task FooAsync() => Task.CompletedTask;

    void M()
    {
        {|UPA2012:FooAsync()|};
    }
}").RunAsync();
        }

        // UPA2012 test case 4
        [Fact]
        public Task AwaitedInvocation_DoesNotTrigger()
        {
            return CreateTest(@"
using System.Threading.Tasks;

class C
{
    Task FooAsync() => Task.CompletedTask;

    async Task M()
    {
        await FooAsync();
    }
}").RunAsync();
        }

        // UPA2012 test case 5 — Forget is UniTask's explicit fire-and-forget;
        // a bare discarded UniTask invocation still triggers.
        [Fact]
        public Task UniTaskForget_DoesNotTrigger_BareUniTask_Triggers()
        {
            return CreateTest(@"
using Cysharp.Threading.Tasks;
" + UniTaskStub + @"
class C
{
    UniTask FooAsync() => default;

    void M()
    {
        FooAsync().Forget();
        {|UPA2012:FooAsync()|};
    }
}", referenceUniTask: true).RunAsync();
        }

        // UPA2012 test case 6 — explicit discard states the intent
        [Fact]
        public Task DiscardAssignment_DoesNotTrigger()
        {
            return CreateTest(@"
using System.Threading.Tasks;

class C
{
    Task FooAsync() => Task.CompletedTask;

    void M()
    {
        _ = FooAsync();
    }
}").RunAsync();
        }

        // UPA2012 test case 7 — without UniTask the rule still fires, with Task advice
        [Fact]
        public Task WithoutUniTask_TriggersWithTaskAdvice()
        {
            var test = CreateTest(@"
using System.Threading.Tasks;

class C
{
    Task FooAsync() => Task.CompletedTask;

    async void {|#0:Fire|}()
    {
        await Task.Yield();
    }

    void M()
    {
        {|#1:FooAsync()|};
    }
}");
            test.ExpectedDiagnostics.Add(
                new DiagnosticResult(UPA2012FireAndForgetAnalyzer.DiagnosticId, DiagnosticSeverity.Warning)
                    .WithLocation(0)
                    .WithMessage("'Fire' is async void: exceptions escape every caller and completion cannot be observed. Return Task instead, and await or store it at the call sites."));
            test.ExpectedDiagnostics.Add(
                new DiagnosticResult(UPA2012FireAndForgetAnalyzer.DiagnosticId, DiagnosticSeverity.Warning)
                    .WithLocation(1)
                    .WithMessage("The result of 'FooAsync' is discarded, so its exceptions are silently lost. Await it, or store the task and observe its completion."));
            return test.RunAsync();
        }

        // With UniTask referenced the advice switches to UniTaskVoid/Forget
        [Fact]
        public Task WithUniTask_TriggersWithUniTaskAdvice()
        {
            var test = CreateTest(@"
using System.Threading.Tasks;

class C
{
    Task FooAsync() => Task.CompletedTask;

    async void {|#0:Fire|}()
    {
        await Task.Yield();
    }

    void M()
    {
        {|#1:FooAsync()|};
    }
}", referenceUniTask: true);
            test.ExpectedDiagnostics.Add(
                new DiagnosticResult(UPA2012FireAndForgetAnalyzer.DiagnosticId, DiagnosticSeverity.Warning)
                    .WithLocation(0)
                    .WithMessage("'Fire' is async void: exceptions escape every caller and completion cannot be observed. Return UniTaskVoid and call Forget, or return UniTask."));
            test.ExpectedDiagnostics.Add(
                new DiagnosticResult(UPA2012FireAndForgetAnalyzer.DiagnosticId, DiagnosticSeverity.Warning)
                    .WithLocation(1)
                    .WithMessage("The result of 'FooAsync' is discarded, so its exceptions are silently lost. Await it, or make fire-and-forget explicit with Forget."));
            return test.RunAsync();
        }

        // isEnabledByDefault: false — asserted on the descriptors because the
        // testing framework force-enables disabled-by-default rules when running analyzers.
        [Fact]
        public void Descriptors_AreDisabledByDefault()
        {
            var descriptors = new UPA2012FireAndForgetAnalyzer().SupportedDiagnostics;
            Assert.Equal(2, descriptors.Length);
            Assert.All(descriptors, d =>
            {
                Assert.Equal(UPA2012FireAndForgetAnalyzer.DiagnosticId, d.Id);
                Assert.False(d.IsEnabledByDefault);
            });
        }

        // UPA2012 test case 8 - Forget is what UniTask offers for exactly this, and appending
        // it changes nothing else in the statement
        [Fact]
        public Task UnawaitedUniTask_CodeFix_AppendsForget()
        {
            return VerifyFixAsync(@"
using Cysharp.Threading.Tasks;

class C
{
    UniTask FooAsync() => default;

    void M()
    {
        {|UPA2012:FooAsync()|};
    }
}", @"
using Cysharp.Threading.Tasks;

class C
{
    UniTask FooAsync() => default;

    void M()
    {
        FooAsync().Forget();
    }
}");
        }

        // UPA2012 test case 9 - the call site need never have named UniTask, and Forget is an
        // extension method, so the using has to come with the rewrite
        [Fact]
        public Task UnawaitedUniTask_CodeFix_AddsMissingUsing()
        {
            return VerifyFixAsync(@"
class C
{
    Cysharp.Threading.Tasks.UniTask FooAsync() => default;

    void M()
    {
        {|UPA2012:FooAsync()|};
    }
}", @"using Cysharp.Threading.Tasks;

class C
{
    Cysharp.Threading.Tasks.UniTask FooAsync() => default;

    void M()
    {
        FooAsync().Forget();
    }
}");
        }

        // UPA2012 test case 10 - UniTask defines Forget on its own types; Task has none
        [Fact]
        public Task UnawaitedTask_Triggers_WithoutFix()
        {
            const string Source = @"
using System.Threading.Tasks;

class C
{
    Task FooAsync() => Task.CompletedTask;

    void M()
    {
        {|UPA2012:FooAsync()|};
    }
}";
            return VerifyFixAsync(Source, Source);
        }

        // UPA2012 test case 11 - form A is fixed by changing a signature, which reaches every
        // caller
        [Fact]
        public Task AsyncVoid_Triggers_WithoutFix()
        {
            const string Source = @"
using System.Threading.Tasks;

class C
{
    async void {|UPA2012:Fire|}()
    {
        await Task.Yield();
    }
}";
            return VerifyFixAsync(Source, Source);
        }

        // UPA2012 test case 12 - another Forget on the same receiver is in scope, so appending
        // one decides nothing about which it binds to
        [Fact]
        public Task CompetingForgetExtension_Triggers_WithoutFix()
        {
            const string Source = @"
using Cysharp.Threading.Tasks;

static class Rival
{
    public static void Forget(this UniTask task) { }
}

class C
{
    UniTask FooAsync() => default;

    void M()
    {
        {|UPA2012:FooAsync()|};
    }
}";
            return VerifyFixAsync(Source, Source);
        }
    }
}
