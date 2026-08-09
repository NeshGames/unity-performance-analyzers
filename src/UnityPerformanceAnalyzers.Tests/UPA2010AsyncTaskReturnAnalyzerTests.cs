using System.Threading.Tasks;
using Xunit;

namespace UnityPerformanceAnalyzers.Tests
{
    public class UPA2010AsyncTaskReturnAnalyzerTests
    {
        private static Task VerifyAsync(string source, bool referenceUniTask = true)
        {
            var harness = new RuleHarness { UnityStubs = false, EnabledRules = { "UPA2010" } };
            if (referenceUniTask)
            {
                harness.PackageAssemblies.Add(UpaProfile.UniTaskAssemblyName);
            }

            return RuleVerifier.VerifyAsync<UPA2010AsyncTaskReturnAnalyzer>(source, harness);
        }

        // UPA2010 test case 1
        [Fact]
        public Task AsyncTaskMethod_WithUniTask_Triggers()
        {
            return VerifyAsync(@"
using System.Threading.Tasks;

class C
{
    async Task {|UPA2010:LoadAsync|}()
    {
        await Task.Yield();
    }

    async Task<int> {|UPA2010:CountAsync|}()
    {
        await Task.Yield();
        return 0;
    }
}");
        }

        // UPA2010 test case 2 — without UniTask the rule is not registered at all
        [Fact]
        public Task AsyncTaskMethod_WithoutUniTask_DoesNotTrigger()
        {
            return VerifyAsync(@"
using System.Threading.Tasks;

class C
{
    async Task LoadAsync()
    {
        await Task.Yield();
    }
}", referenceUniTask: false);
        }

        // UPA2010 test case 3 — overrides and interface implementations are bound signatures
        [Fact]
        public Task OverrideAndInterfaceImplementation_DoNotTrigger()
        {
            return VerifyAsync(@"
using System.Threading.Tasks;

interface ILoader
{
    Task LoadAsync();
}

abstract class Base
{
    public abstract Task SaveAsync();
}

class C : Base, ILoader
{
    public async Task LoadAsync()
    {
        await Task.Yield();
    }

    public override async Task SaveAsync()
    {
        await Task.Yield();
    }
}

class Explicit : ILoader
{
    async Task ILoader.LoadAsync()
    {
        await Task.Yield();
    }
}");
        }

        // UPA2010 test case 4 — async UniTask is the recommended shape
        [Fact]
        public Task AsyncUniTaskMethod_DoesNotTrigger()
        {
            return VerifyAsync(@"
using System;
using System.Runtime.CompilerServices;
using Cysharp.Threading.Tasks;

namespace System.Runtime.CompilerServices
{
    sealed class AsyncMethodBuilderAttribute : Attribute
    {
        public AsyncMethodBuilderAttribute(Type builderType) { }
    }
}

namespace Cysharp.Threading.Tasks
{
    [AsyncMethodBuilder(typeof(AsyncUniTaskMethodBuilder))]
    struct UniTask { }

    struct AsyncUniTaskMethodBuilder
    {
        public static AsyncUniTaskMethodBuilder Create() => default;
        public UniTask Task => default;
        public void SetResult() { }
        public void SetException(Exception exception) { }
        public void SetStateMachine(IAsyncStateMachine stateMachine) { }
        public void Start<TStateMachine>(ref TStateMachine stateMachine)
            where TStateMachine : IAsyncStateMachine => stateMachine.MoveNext();
        public void AwaitOnCompleted<TAwaiter, TStateMachine>(ref TAwaiter awaiter, ref TStateMachine stateMachine)
            where TAwaiter : INotifyCompletion
            where TStateMachine : IAsyncStateMachine { }
        public void AwaitUnsafeOnCompleted<TAwaiter, TStateMachine>(ref TAwaiter awaiter, ref TStateMachine stateMachine)
            where TAwaiter : ICriticalNotifyCompletion
            where TStateMachine : IAsyncStateMachine { }
    }
}

class C
{
    async UniTask LoadAsync()
    {
    }
}");
        }

        // UPA2010 test case 5 — non-async forwarding wrappers allocate no state machine
        [Fact]
        public Task NonAsyncTaskMethod_DoesNotTrigger()
        {
            return VerifyAsync(@"
using System.Threading.Tasks;

class C
{
    Task Forward() => Task.CompletedTask;
}");
        }

        // UPA2010 test case 6
        [Fact]
        public Task AsyncTaskLocalFunction_Triggers()
        {
            return VerifyAsync(@"
using System.Threading.Tasks;

class C
{
    void M()
    {
        async Task {|UPA2010:InnerAsync|}()
        {
            await Task.Yield();
        }

        _ = InnerAsync();
    }
}");
        }

        // Async lambdas are excluded — the delegate type is fixed by the consuming API
        [Fact]
        public Task AsyncLambda_DoesNotTrigger()
        {
            return VerifyAsync(@"
using System;
using System.Threading.Tasks;

class C
{
    void M()
    {
        Func<Task> f = async () => await Task.Yield();
        _ = f;
    }
}");
        }

        // isEnabledByDefault: false — asserted on the descriptor because the
        // testing framework force-enables disabled-by-default rules when running analyzers.
        [Fact]
        public void Descriptor_IsDisabledByDefault()
        {
            var descriptor = Assert.Single(new UPA2010AsyncTaskReturnAnalyzer().SupportedDiagnostics);
            Assert.False(descriptor.IsEnabledByDefault);
        }
    }
}
