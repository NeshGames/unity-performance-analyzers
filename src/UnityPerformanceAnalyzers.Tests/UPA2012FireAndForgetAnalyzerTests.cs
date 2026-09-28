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
    struct UniTask<T> { }

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
            var harness = new RuleHarness
            {
                UnityStubs = false,
                // Both UPA2012 descriptors share the ID and severity; markup only needs ID + span.
                MarkupOptions = MarkupOptions.UseFirstDescriptor,
            };
            if (referenceUniTask)
            {
                harness.PackageAssemblies.Add(UpaProfile.UniTaskAssemblyName);
            }

            return RuleVerifier.CreateTest<UPA2012FireAndForgetAnalyzer>(source, harness);
        }

        private static Task VerifyTriggersAsync(string source) =>
            RuleVerifier.VerifyAsync<UPA2012FireAndForgetAnalyzer>(source, new RuleHarness
            {
                UnityStubs = false,
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

        // UniTask.Analyzer does not own discarded results. Keep this differential guard so a
        // future coexistence cleanup cannot silently create a hole for UniTask<T>.
        [Fact]
        public Task WithUniTaskPackage_DiscardedUniTaskAndGenericStillTrigger()
        {
            return VerifyTriggersAsync(@"
using Cysharp.Threading.Tasks;

class C
{
    UniTask FireAsync() => default;
    UniTask<int> LoadAsync() => default;

    void M()
    {
        {|UPA2012:FireAsync()|};
        {|UPA2012:LoadAsync()|};
    }
}");
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

        // UPA2012 test case 10 - UniTask defines Forget on its own types; Task has none
        [Fact]
        public Task UnawaitedTask_Triggers()
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
            return VerifyTriggersAsync(Source);
        }

        // UPA2012 test case 11 - form A is fixed by changing a signature, which reaches every
        // caller
        [Fact]
        public Task AsyncVoid_Triggers()
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
            return VerifyTriggersAsync(Source);
        }

        // UPA2012 test case 12 - another Forget on the same receiver is in scope, so appending
        // one decides nothing about which it binds to
        [Fact]
        public Task CompetingForgetExtension_Triggers()
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
            return VerifyTriggersAsync(Source);
        }

        // Form A, one scope down: a local function's exceptions escape its callers the same way.
        [Fact]
        public Task AsyncVoidLocalFunction_Triggers()
        {
            return CreateTest(@"
using System;
using System.Threading.Tasks;

class C
{
    void M()
    {
        async void {|UPA2012:Local|}()
        {
            await Task.Yield();
        }

        async void Handler(object sender, EventArgs e)
        {
            await Task.Yield();
        }

        Local();
    }
}").RunAsync();
        }

        private const string UnityEventStub = @"
namespace UnityEngine.Events
{
    public delegate void UnityAction();

    public class UnityEvent
    {
        public void AddListener(UnityAction call) { }
    }
}
";

        // The canonical Unity UI line. The delegate returns void, so the lambda is async void,
        // and the message names the delegate because the lambda has no name of its own.
        [Fact]
        public Task AsyncLambdaToUnityAction_Triggers_WithLambdaAdvice()
        {
            var test = CreateTest(@"
using System.Threading.Tasks;
using UnityEngine.Events;
" + UnityEventStub + @"
class C
{
    UnityEvent onClick = new UnityEvent();

    Task LoadAsync() => Task.CompletedTask;

    void M()
    {
        onClick.AddListener({|#0:async () =>|} await LoadAsync());
    }
}");
            test.ExpectedDiagnostics.Add(
                new DiagnosticResult(UPA2012FireAndForgetAnalyzer.DiagnosticId, DiagnosticSeverity.Warning)
                    .WithLocation(0)
                    .WithMessage("This async lambda is async void once converted to 'UnityAction': exceptions escape every caller and completion cannot be observed. Move the body into a Task-returning method that handles its own exceptions, and call it as () => _ = TheMethod()."));
            return test.RunAsync();
        }

        [Fact]
        public Task AsyncLambdaToAction_WithUniTask_TriggersWithUniTaskAdvice()
        {
            var test = CreateTest(@"
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

class C
{
    void M(List<int> ids)
    {
        ids.ForEach({|#0:async id =>|} await Task.Yield());
    }
}", referenceUniTask: true);
            test.ExpectedDiagnostics.Add(
                new DiagnosticResult(UPA2012FireAndForgetAnalyzer.DiagnosticId, DiagnosticSeverity.Warning)
                    .WithLocation(0)
                    .WithMessage("This async lambda is async void once converted to 'Action<int>': exceptions escape every caller and completion cannot be observed. Wrap it in UniTask.UnityAction or UniTask.Action, or have it call an async UniTaskVoid method and Forget the result."));
            return test.RunAsync();
        }

        [Fact]
        public Task AsyncAnonymousMethodToAction_Triggers()
        {
            return CreateTest(@"
using System;
using System.Threading.Tasks;

class C
{
    void M()
    {
        Action fire = {|UPA2012:async delegate|} { await Task.Yield(); };
    }
}").RunAsync();
        }

        // Where the delegate returns a task, so does the lambda, and whoever invokes it can
        // observe it. The event-handler convention is exempt for lambdas as it is for methods,
        // and the fix the message offers without UniTask - a non-async lambda discarding a
        // task that handles its own exceptions - must not report either.
        [Fact]
        public Task AsyncLambdaReturningTaskOrEventHandler_DoesNotTrigger()
        {
            return CreateTest(@"
using System;
using System.Threading.Tasks;

class C
{
    event EventHandler Clicked;

    void M()
    {
        Func<Task> load = async () => await Task.Yield();
        var run = Task.Run(async () => await Task.Yield());
        Clicked += async (sender, e) => await Task.Yield();
        Action fire = () => _ = LoadAndReportAsync();
    }

    async Task LoadAndReportAsync()
    {
        try { await Task.Yield(); }
        catch (Exception) { }
    }
}").RunAsync();
        }

        // `?.` only decides whether there is a task; when there is one it is discarded all the
        // same. The span is the whole conditional access, which is the statement being flagged.
        [Fact]
        public Task ConditionalAccessInvocation_Triggers()
        {
            return CreateTest(@"
using System.Threading.Tasks;

class Loader
{
    public Loader Next;
    public Task LoadAsync() => Task.CompletedTask;
}

class C
{
    Loader loader;

    void M()
    {
        {|UPA2012:loader?.LoadAsync()|};
        {|UPA2012:loader?.Next?.LoadAsync()|};
        {|UPA2012:loader?.Next.LoadAsync()|};
    }
}").RunAsync();
        }

        [Fact]
        public Task ConditionalAccessUniTask_ForgetOrReceiver_DoesNotTrigger_BareTriggers()
        {
            return CreateTest(@"
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
" + UniTaskStub + @"
class Loader
{
    public UniTask LoadAsync() => default;
    public Task Pending() => Task.CompletedTask;
}

class C
{
    Loader loader;

    void M()
    {
        loader?.LoadAsync().Forget();
        loader.Pending()?.ToString();
        {|UPA2012:loader?.LoadAsync()|};
    }
}", referenceUniTask: true).RunAsync();
        }
    }
}
