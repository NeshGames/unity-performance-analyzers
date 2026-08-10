using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Xunit;

namespace UnityPerformanceAnalyzers.Tests
{
    // UPA0022 is deprecated: Enum.HasFlag neither boxes nor costs more than the bitwise
    // form on any supported runtime, and the code fix it used to offer produced the slower
    // spelling. The number stays registered because CLAUDE.md 2.4 does not recycle ids, so
    // what these tests pin is that it says nothing unless a project asks it to.
    public class UPA0022HasFlagAnalyzerTests
    {
        // Enabled the way a project that still wants it would.
        private static Task VerifyEnabledAsync(string source) =>
            RuleVerifier.VerifyAsync<UPA0022HasFlagAnalyzer>(source, new RuleHarness
            {
                EnabledRules = { "UPA0022" },
            });

        // UPA0022 test case 1 is not written here, and the reason is worth stating rather
        // than leaving as an absence. Microsoft.CodeAnalysis.Testing force-enables every
        // diagnostic of the analyzers a test adds — deliberately, so a test cannot pass by
        // the analyzer never running — so "off by default" is not expressible through this
        // verifier. What decides it for Roslyn in Unity, for upa-cli, and for the presets is
        // the descriptor, and that is pinned below.
        [Fact]
        public Task HasFlag_InUpdate_WhenEnabled_Triggers()
        {
            return VerifyEnabledAsync(@"
using System;
using UnityEngine;

[Flags]
enum State { None = 0, Dead = 1 }

class C : MonoBehaviour
{
    State state;

    void Update()
    {
        if ({|UPA0022:state.HasFlag(State.Dead)|})
        {
        }
    }
}");
        }

        // UPA0022 test case 3
        [Fact]
        public Task HasFlag_InStart_WhenEnabled_DoesNotTrigger()
        {
            return VerifyEnabledAsync(@"
using System;
using UnityEngine;

[Flags]
enum State { None = 0, Dead = 1 }

class C : MonoBehaviour
{
    State state;

    void Start()
    {
        if (state.HasFlag(State.Dead))
        {
        }
    }
}");
        }

        // UPA0022 test case 4
        [Fact]
        public Task CustomHasFlagMethod_WhenEnabled_DoesNotTrigger()
        {
            return VerifyEnabledAsync(@"
using UnityEngine;

class Flags
{
    public bool HasFlag(int flag) => false;
}

class C : MonoBehaviour
{
    Flags flags = new Flags();

    void Update()
    {
        if (flags.HasFlag(1))
        {
        }
    }
}");
        }

        // UPA0022 test case 5
        [Fact]
        public void Descriptor_IsNotEnabledByDefault()
        {
            var descriptor = new UPA0022HasFlagAnalyzer()
                .SupportedDiagnostics
                .Single(d => d.Id == UPA0022HasFlagAnalyzer.DiagnosticId);

            Assert.False(descriptor.IsEnabledByDefault);
        }

        // UPA0022 test case 6 — the fix rewrote to (x & y) == y, which measures slower than
        // the call it replaced. A deprecated rule offering a pessimisation is worse than the
        // rule itself, so the provider is gone and must stay gone.
        [Fact]
        public void NoCodeFixProviderRemains()
        {
            var providers = typeof(CodeFixes.UPA0019BoxedYieldCodeFixProvider).Assembly
                .GetTypes()
                .Where(t => t.Name.Contains("UPA0022"))
                .ToArray();

            Assert.Empty(providers);
        }
    }
}
