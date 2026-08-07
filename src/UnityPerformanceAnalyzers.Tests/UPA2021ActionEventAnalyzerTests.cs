using System.Threading.Tasks;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Xunit;

namespace UnityPerformanceAnalyzers.Tests
{
    public class UPA2021ActionEventAnalyzerTests
    {
        private static Task VerifyAsync(string source, bool referenceR3 = true)
        {
            var test = new CSharpAnalyzerTest<UPA2021ActionEventAnalyzer, DefaultVerifier>
            {
                TestCode = source,
                ReferenceAssemblies = ReferenceAssemblies.NetStandard.NetStandard20,
            };
            if (referenceR3)
            {
                test.TestState.AdditionalReferences.Add(
                    TestMetadataReferences.EmptyAssembly(UpaProfile.R3AssemblyName));
            }

            // UPA2021 is disabled by default; enable it the same way a preset would.
            test.TestState.AnalyzerConfigFiles.Add(("/.editorconfig", @"
root = true

[*.cs]
dotnet_diagnostic.UPA2021.severity = warning
"));
            return test.RunAsync();
        }

        // UPA2021 test case 1
        [Fact]
        public Task PublicActionEvent_WithR3_Triggers()
        {
            return VerifyAsync(@"
using System;

class Score
{
    public event Action<int> {|UPA2021:ScoreChanged|};

    void Raise(int value) => ScoreChanged?.Invoke(value);
}");
        }

        // UPA2021 test case 2 — EventHandler is the established .NET event pattern
        [Fact]
        public Task PublicEventHandlerEvent_DoesNotTrigger()
        {
            return VerifyAsync(@"
using System;

class Score
{
    public event EventHandler Changed;

    void Raise() => Changed?.Invoke(this, EventArgs.Empty);
}");
        }

        // UPA2021 test case 3 — non-public events are implementation detail
        [Fact]
        public Task PrivateActionEvent_DoesNotTrigger()
        {
            return VerifyAsync(@"
using System;

class Score
{
    private event Action Tick;

    void Raise() => Tick?.Invoke();
}");
        }

        // UPA2021 test case 4 — a plain Action field is a callback, not an observable
        [Fact]
        public Task PublicActionField_DoesNotTrigger()
        {
            return VerifyAsync(@"
using System;

class Score
{
    public Action<int> Callback;

    void Raise(int value) => Callback?.Invoke(value);
}");
        }

        // UPA2021 test case 5 — without R3 the rule is not registered at all
        [Fact]
        public Task PublicActionEvent_WithoutR3_DoesNotTrigger()
        {
            return VerifyAsync(@"
using System;

class Score
{
    public event Action<int> ScoreChanged;

    void Raise(int value) => ScoreChanged?.Invoke(value);
}", referenceR3: false);
        }

        // Custom delegate types may carry an established API contract
        [Fact]
        public Task CustomDelegateEvent_DoesNotTrigger()
        {
            return VerifyAsync(@"
class Score
{
    public delegate void ScoreHandler(int value);

    public event ScoreHandler ScoreChanged;

    void Raise(int value) => ScoreChanged?.Invoke(value);
}");
        }

        // isEnabledByDefault: false — asserted on the descriptor because the
        // testing framework force-enables disabled-by-default rules when running analyzers.
        [Fact]
        public void Descriptor_IsDisabledByDefault()
        {
            var descriptor = Assert.Single(new UPA2021ActionEventAnalyzer().SupportedDiagnostics);
            Assert.False(descriptor.IsEnabledByDefault);
        }
    }
}
