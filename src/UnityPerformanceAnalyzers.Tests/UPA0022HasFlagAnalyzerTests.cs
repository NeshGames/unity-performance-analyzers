using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Xunit;

namespace UnityPerformanceAnalyzers.Tests
{
    // UPA0022 is deprecated: Enum.HasFlag neither boxes nor costs more than the bitwise
    // form on any supported runtime, and the code fix it used to offer produced the slower
    // spelling. The number stays registered because a rule id is never reused, so
    // what these tests pin is that it says nothing unless a project asks it to.
    public class UPA0022HasFlagAnalyzerTests
    {
        private static Task VerifyEnabledAsync(string source) =>
            RuleVerifier.VerifyAsync<UPA0022HasFlagAnalyzer>(source);

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
    }
}
