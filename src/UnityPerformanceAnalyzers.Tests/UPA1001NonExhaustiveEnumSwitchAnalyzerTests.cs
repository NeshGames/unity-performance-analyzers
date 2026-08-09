using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Testing;
using Xunit;

namespace UnityPerformanceAnalyzers.Tests
{
    public class UPA1001NonExhaustiveEnumSwitchAnalyzerTests
    {
        private static Task VerifyAsync(string source, bool allowDefault = true) =>
            RuleVerifier.VerifyAsync<UPA1001NonExhaustiveEnumSwitchAnalyzer>(source, new RuleHarness
            {
                UnityStubs = false,
                EditorConfig = allowDefault ? null : "upa_enum_switch_allow_default = false",
            });

        // UPA1001 test case 1
        [Fact]
        public Task MissingMember_NoDefault_Triggers()
        {
            return VerifyAsync(@"
enum State { Idle, Running, Dead }

class C
{
    void M(State state)
    {
        switch ({|UPA1001:state|})
        {
            case State.Idle:
            case State.Running:
                break;
        }
    }
}");
        }

        // UPA1001 test case 2 — a default arm counts as exhaustive by default
        [Fact]
        public Task MissingMember_WithDefault_DoesNotTrigger()
        {
            return VerifyAsync(@"
enum State { Idle, Running, Dead }

class C
{
    void M(State state)
    {
        switch (state)
        {
            case State.Idle:
                break;
            default:
                break;
        }
    }
}");
        }

        // UPA1001 test case 3 — allow_default=false keeps naming the gaps
        [Fact]
        public Task MissingMember_WithDefault_OptionOff_Triggers()
        {
            return VerifyAsync(@"
enum State { Idle, Running, Dead }

class C
{
    void M(State state)
    {
        switch ({|UPA1001:state|})
        {
            case State.Idle:
                break;
            default:
                break;
        }
    }
}", allowDefault: false);
        }

        // UPA1001 test case 4
        [Fact]
        public Task AllMembersCovered_DoesNotTrigger()
        {
            return VerifyAsync(@"
enum State { Idle, Running, Dead }

class C
{
    void M(State state)
    {
        switch (state)
        {
            case State.Idle:
            case State.Running:
            case State.Dead:
                break;
        }
    }
}");
        }

        // UPA1001 test case 5 — switch expression with a discard arm
        [Fact]
        public Task SwitchExpression_WithDiscard_DefaultOption_DoesNotTrigger()
        {
            return VerifyAsync(@"
enum State { Idle, Running, Dead }

class C
{
    int M(State state) => state switch
    {
        State.Idle => 0,
        _ => -1,
    };
}");
        }

        // UPA1001 test case 5 (second half)
        [Fact]
        public Task SwitchExpression_WithDiscard_OptionOff_Triggers()
        {
            return VerifyAsync(@"
enum State { Idle, Running, Dead }

class C
{
    int M(State state) => {|UPA1001:state|} switch
    {
        State.Idle => 0,
        _ => -1,
    };
}", allowDefault: false);
        }

        // UPA1001 test case 6
        [Fact]
        public Task FlagsEnum_DoesNotTrigger()
        {
            return VerifyAsync(@"
[System.Flags]
enum Layers { None = 0, A = 1, B = 2 }

class C
{
    void M(Layers layers)
    {
        switch (layers)
        {
            case Layers.A:
                break;
        }
    }
}");
        }

        // UPA1001 test case 7
        [Fact]
        public Task NonEnumSwitch_DoesNotTrigger()
        {
            return VerifyAsync(@"
class C
{
    void M(int value)
    {
        switch (value)
        {
            case 1:
                break;
        }
    }
}");
        }

        // UPA1001 test case 8 — when guards make coverage undecidable
        [Fact]
        public Task CaseWithWhenGuard_DoesNotTrigger()
        {
            return VerifyAsync(@"
enum State { Idle, Running, Dead }

class C
{
    int hp;

    void M(State state)
    {
        switch (state)
        {
            case State.Running when hp > 0:
                break;
        }
    }
}");
        }

        // UPA1001 test case 9 — members sharing a value count as covered together
        [Fact]
        public Task SameValueAlias_DoesNotTrigger()
        {
            return VerifyAsync(@"
enum Mode { Default = 0, Legacy = 0, Fast = 1 }

class C
{
    void M(Mode mode)
    {
        switch (mode)
        {
            case Mode.Default:
            case Mode.Fast:
                break;
        }
    }
}");
        }

        // UPA1001 test case 10 — the missing list truncates at five members
        [Fact]
        public Task ManyMissingMembers_MessageTruncates()
        {
            var test = RuleVerifier.CreateTest<UPA1001NonExhaustiveEnumSwitchAnalyzer>(
                @"
enum Rainbow { Red, Orange, Yellow, Green, Blue, Indigo, Violet }

class C
{
    void M(Rainbow color)
    {
        switch ({|#0:color|})
        {
        }
    }
}",
                new RuleHarness { UnityStubs = false });
            test.ExpectedDiagnostics.Add(
                new DiagnosticResult(UPA1001NonExhaustiveEnumSwitchAnalyzer.DiagnosticId, DiagnosticSeverity.Warning)
                    .WithLocation(0)
                    .WithMessage("Switch over 'Rainbow' does not handle: Red, Orange, Yellow, Green, Blue, …. Add the missing cases, or handle them deliberately in the default arm."));
            return test.RunAsync();
        }

        // Correctness block: enabled by default (unlike the ecosystem rules)
        [Fact]
        public void Descriptor_IsEnabledByDefault()
        {
            var descriptor = Assert.Single(new UPA1001NonExhaustiveEnumSwitchAnalyzer().SupportedDiagnostics);
            Assert.True(descriptor.IsEnabledByDefault);
        }
    }
}
