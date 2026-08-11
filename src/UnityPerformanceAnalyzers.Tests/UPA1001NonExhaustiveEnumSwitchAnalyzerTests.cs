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

        // ---------------------------------------------------------------------------------
        // Flags detection by syntax rather than by value.
        //
        // The corpus found VehicleLight: no [Flags], but Front = FrontLeft | FrontRight and
        // All = Front | Rear, so the rule demanded cases for bit masks. Judging by value looks
        // like the fix and is not one - Priority { Low = 1, Medium = 2, High = 3 } has 3 == 1|2,
        // so a value test silences the rule on an ordinary enum and leaves no trace that it did.
        // ---------------------------------------------------------------------------------

        /// <summary>the corpus shape.</summary>
        [Fact]
        public Task BitwiseOrInitializer_WithoutFlagsAttribute_DoesNotTrigger()
        {
            return VerifyAsync(@"
enum VehicleLight
{
    FrontLeft = 1, FrontRight = 2, RearLeft = 4, RearRight = 8,
    Front = FrontLeft | FrontRight,
    Rear = RearLeft | RearRight,
    All = Front | Rear
}

class C
{
    void M(VehicleLight light)
    {
        switch (light)
        {
            case VehicleLight.FrontLeft: break;
            case VehicleLight.FrontRight: break;
            case VehicleLight.RearLeft: break;
            case VehicleLight.RearRight: break;
        }
    }
}");
        }

        /// <summary>
        /// the reason the criterion is syntactic. 3 == 1 | 2, so a value-based
        /// flags test would judge this enum flags-like and report nothing here, forever.
        /// </summary>
        [Fact]
        public Task SequentialEnum_WhoseThirdValueIsTheOrOfTheFirstTwo_Triggers()
        {
            return VerifyAsync(@"
enum Priority { Low = 1, Medium = 2, High = 3 }

class C
{
    void M(Priority p)
    {
        switch ({|UPA1001:p|})
        {
            case Priority.Low: break;
            case Priority.Medium: break;
        }
    }
}");
        }

        /// <summary>literals only, zero present.</summary>
        [Fact]
        public Task LiteralValues_WithZeroMember_Triggers()
        {
            return VerifyAsync(@"
enum Mode { None = 0, A = 1, B = 2, C = 4 }

class C
{
    void M(Mode m)
    {
        switch ({|UPA1001:m|})
        {
            case Mode.None: break;
            case Mode.A: break;
            case Mode.B: break;
        }
    }
}");
        }

        /// <summary>powers of two are not, on their own, flags.</summary>
        [Fact]
        public Task PowersOfTwoWrittenAsLiterals_Triggers()
        {
            return VerifyAsync(@"
enum Size { Small = 1, Medium = 2, Large = 4 }

class C
{
    void M(Size s)
    {
        switch ({|UPA1001:s|})
        {
            case Size.Small: break;
            case Size.Medium: break;
        }
    }
}");
        }

        /// <summary>a same-value alias is not a composite member.</summary>
        [Fact]
        public Task SameValueAlias_IsNotAComposite_Triggers()
        {
            return VerifyAsync(@"
enum E { A = 1, Dup = 1, B = 2 }

class C
{
    void M(E e)
    {
        switch ({|UPA1001:e|})
        {
            case E.A: break;
        }
    }
}");
        }

        /// <summary>a negative literal is not a signal either.</summary>
        [Fact]
        public Task NegativeLiteral_Triggers()
        {
            return VerifyAsync(@"
enum E { Sign = int.MinValue, A = 1, B = 2 }

class C
{
    void M(E e)
    {
        switch ({|UPA1001:e|})
        {
            case E.Sign: break;
            case E.A: break;
        }
    }
}");
        }

        /// <summary>
        /// the shape both value formulations miss. 1|2|4|8 never equals ~0, so a
        /// value test finds no composite member and reports; the `~` says what the author meant.
        /// </summary>
        [Fact]
        public Task BitwiseNotInitializer_DoesNotTrigger()
        {
            return VerifyAsync(@"
enum E { A = 1, B = 2, C = 4, D = 8, All = ~0 }

class C
{
    void M(E e)
    {
        switch (e)
        {
            case E.A: break;
            case E.B: break;
        }
    }
}");
        }

        /// <summary>a shift is the other common way to write a flag.</summary>
        [Fact]
        public Task ShiftInitializer_DoesNotTrigger()
        {
            return VerifyAsync(@"
enum E { A = 1, B = 1 << 1, C = 1 << 2 }

class C
{
    void M(E e)
    {
        switch (e)
        {
            case E.A: break;
        }
    }
}");
        }

        /// <summary>the attribute still stands on its own.</summary>
        [Fact]
        public Task FlagsAttribute_WithLiteralValues_DoesNotTrigger()
        {
            return VerifyAsync(@"
[System.Flags]
enum E { A = 1, B = 2, C = 4 }

class C
{
    void M(E e)
    {
        switch (e)
        {
            case E.A: break;
        }
    }
}");
        }

        /// <summary>
        /// the documented degradation. A metadata-only enum has values but no
        /// syntax, so an unattributed bitwise-combination enum from a referenced assembly is
        /// analysed as an ordinary one. Pinned so nobody "fixes" it back into a value test.
        /// </summary>
        [Fact]
        public Task MetadataOnlyEnum_WithoutFlagsAttribute_IsAnalysedNormally()
        {
            return VerifyAsync(@"
class C
{
    void M(System.DayOfWeek d)
    {
        switch ({|UPA1001:d|})
        {
            case System.DayOfWeek.Monday: break;
        }
    }
}");
        }
    }
}
