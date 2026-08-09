using System.Threading.Tasks;
using UnityPerformanceAnalyzers.CodeFixes;
using Xunit;

namespace UnityPerformanceAnalyzers.Tests
{
    public class UPA0022HasFlagAnalyzerTests
    {
        private static Task VerifyAsync(string source) =>
            RuleVerifier.VerifyAsync<UPA0022HasFlagAnalyzer>(source);

        private static Task VerifyFixAsync(string source, string fixedSource)
        {
            return RuleVerifier.VerifyCodeFixAsync<UPA0022HasFlagAnalyzer, UPA0022HasFlagCodeFixProvider>(
                source,
                fixedSource);
        }

        // UPA0022 test case 1
        [Fact]
        public Task HasFlag_InUpdate_Triggers_AndFixRewritesToBitwiseCheck()
        {
            return VerifyFixAsync(@"
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
}", @"
using System;
using UnityEngine;

[Flags]
enum State { None = 0, Dead = 1 }

class C : MonoBehaviour
{
    State state;

    void Update()
    {
        if ((state & State.Dead) == State.Dead)
        {
        }
    }
}");
        }

        // UPA0022 test case 2
        [Fact]
        public Task HasFlag_InStart_DoesNotTrigger()
        {
            return VerifyAsync(@"
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

        // The rewrite duplicates the flag expression, so a side-effecting argument still
        // reports but gets no fix — the source must stay unchanged.
        [Fact]
        public Task HasFlagWithMethodCallArgument_Triggers_WithoutFix()
        {
            var source = @"
using System;
using UnityEngine;

[Flags]
enum State { None = 0, Dead = 1 }

class C : MonoBehaviour
{
    State state;

    State NextFlag() => State.Dead;

    void Update()
    {
        if ({|UPA0022:state.HasFlag(NextFlag())|})
        {
        }
    }
}";
            return VerifyFixAsync(source, source);
        }

        // A field argument is a plain storage read — single-evaluation-safe, fix offered.
        [Fact]
        public Task HasFlagWithFieldArgument_CodeFix_Rewrites()
        {
            return VerifyFixAsync(@"
using System;
using UnityEngine;

[Flags]
enum State { None = 0, Dead = 1 }

class C : MonoBehaviour
{
    State state;
    State mask;

    void Update()
    {
        if ({|UPA0022:state.HasFlag(mask)|})
        {
        }
    }
}", @"
using System;
using UnityEngine;

[Flags]
enum State { None = 0, Dead = 1 }

class C : MonoBehaviour
{
    State state;
    State mask;

    void Update()
    {
        if ((state & mask) == mask)
        {
        }
    }
}");
        }

        // UPA0022 test case 3
        [Fact]
        public Task CustomHasFlagMethod_InUpdate_DoesNotTrigger()
        {
            return VerifyAsync(@"
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
