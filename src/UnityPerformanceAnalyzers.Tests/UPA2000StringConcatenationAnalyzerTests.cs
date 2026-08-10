using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Xunit;

namespace UnityPerformanceAnalyzers.Tests
{
    public class UPA2000StringConcatenationAnalyzerTests
    {
        private const string AdviceDefault =
            "Cache the string, or build it with a reusable StringBuilder outside the hot path.";

        private const string AdviceZString =
            "Use ZString to format without intermediate string allocations.";

        private static string Message(string advice)
            => $"String building on a per-frame path allocates a new string every frame. {advice}";

        private static CSharpAnalyzerTest<UPA2000StringConcatenationAnalyzer, DefaultVerifier> CreateTest(
            string source,
            bool referenceZString = false)
        {
            // UPA2000 is disabled by default; enable it the same way a preset would.
            var harness = new RuleHarness { EnabledRules = { "UPA2000" } };
            if (referenceZString)
            {
                harness.PackageAssemblies.Add(UpaProfile.ZStringAssemblyName);
            }

            return RuleVerifier.CreateTest<UPA2000StringConcatenationAnalyzer>(source, harness);
        }

        private const string ZStringStub = @"
namespace Cysharp.Text
{
    public static class ZString
    {
        public static string Concat<T1, T2>(T1 arg1, T2 arg2) => string.Empty;

        public static string Concat<T1, T2, T3>(T1 arg1, T2 arg2, T3 arg3) => string.Empty;
    }
}
";

        // Same text on both sides asserts the diagnostic is reported and no fix is offered.
        private static Task VerifyFixAsync(string source, string fixedSource, bool referenceZString = true)
        {
            var harness = new RuleHarness { EnabledRules = { "UPA2000" } };
            if (referenceZString)
            {
                harness.PackageAssemblies.Add(UpaProfile.ZStringAssemblyName);
                harness.Sources.Add(ZStringStub);
            }

            return RuleVerifier.VerifyCodeFixAsync<
                UPA2000StringConcatenationAnalyzer,
                CodeFixes.UPA2000ZStringCodeFixProvider>(source, fixedSource, harness);
        }

        // UPA2000 test case 1 — concatenation without ZString suggests StringBuilder
        [Fact]
        public Task Concatenation_WithoutZString_TriggersWithStringBuilderAdvice()
        {
            var test = CreateTest(@"
using UnityEngine;

class C : MonoBehaviour
{
    int n;
    string s = """";

    void Update()
    {
        s = {|#0:""score: "" + n|};
    }
}");
            test.ExpectedDiagnostics.Add(
                new DiagnosticResult(UPA2000StringConcatenationAnalyzer.DiagnosticId, DiagnosticSeverity.Warning)
                    .WithLocation(0)
                    .WithMessage(Message(AdviceDefault)));
            return test.RunAsync();
        }

        // UPA2000 test case 2 — same code with a ZString-named assembly suggests ZString
        [Fact]
        public Task Concatenation_WithZString_TriggersWithZStringAdvice()
        {
            var test = CreateTest(@"
using UnityEngine;

class C : MonoBehaviour
{
    int n;
    string s = """";

    void Update()
    {
        s = {|#0:""score: "" + n|};
    }
}", referenceZString: true);
            test.ExpectedDiagnostics.Add(
                new DiagnosticResult(UPA2000StringConcatenationAnalyzer.DiagnosticId, DiagnosticSeverity.Warning)
                    .WithLocation(0)
                    .WithMessage(Message(AdviceZString)));
            return test.RunAsync();
        }

        // UPA2000 test case 3
        [Fact]
        public Task Interpolation_InUpdate_Triggers()
        {
            return CreateTest(@"
using UnityEngine;

class C : MonoBehaviour
{
    int hp;
    string s = """";

    void Update()
    {
        s = {|UPA2000:$""hp {hp}""|};
    }
}").RunAsync();
        }

        // UPA2000 test case 4 — compile-time constant folding allocates nothing
        [Fact]
        public Task ConstantFolding_DoesNotTrigger()
        {
            return CreateTest(@"
using UnityEngine;

class C : MonoBehaviour
{
    string s = """";

    void Update()
    {
        s = ""a"" + ""b"";
    }
}").RunAsync();
        }

        // UPA2000 test case 5
        [Fact]
        public Task Concatenation_InStart_DoesNotTrigger()
        {
            return CreateTest(@"
using UnityEngine;

class C : MonoBehaviour
{
    int n;
    string s = """";

    void Start()
    {
        s = ""score: "" + n;
    }
}").RunAsync();
        }

        // UPA2000 test case 6
        [Fact]
        public Task CompoundAppend_InUpdate_Triggers()
        {
            return CreateTest(@"
using UnityEngine;

class C : MonoBehaviour
{
    string s = """";

    void Update()
    {
        {|UPA2000:s += ""x""|};
    }
}").RunAsync();
        }

        // Chained concatenation is one allocation site to the reader — a single report
        // on the outermost expression, not one per + operator.
        [Fact]
        public Task ChainedConcatenation_ReportsOnce()
        {
            return CreateTest(@"
using UnityEngine;

class C : MonoBehaviour
{
    int a;
    int b;
    string s = """";

    void Update()
    {
        s = {|UPA2000:""a: "" + a + b|};
    }
}").RunAsync();
        }

        // isEnabledByDefault: false — asserted on the descriptor because the
        // testing framework force-enables disabled-by-default rules when running analyzers.
        [Fact]
        public void Descriptor_IsDisabledByDefault()
        {
            var descriptor = Assert.Single(
                new UPA2000StringConcatenationAnalyzer().SupportedDiagnostics);
            Assert.False(descriptor.IsEnabledByDefault);
        }

        // UPA2000 test case 7 - the shape the measurement says ZString helps with
        [Fact]
        public Task ConcatenationWithNonStringOperand_CodeFix_UsesZString()
        {
            return VerifyFixAsync(@"
using Cysharp.Text;
using UnityEngine;

class C : MonoBehaviour
{
    int score;
    string label;

    void Update()
    {
        label = {|UPA2000:""score: "" + score|};
    }
}", @"
using Cysharp.Text;
using UnityEngine;

class C : MonoBehaviour
{
    int score;
    string label;

    void Update()
    {
        label = ZString.Concat(""score: "", score);
    }
}");
        }

        // UPA2000 test case 8 - the call site need never have named ZString
        [Fact]
        public Task ConcatenationWithNonStringOperand_CodeFix_AddsMissingUsing()
        {
            return VerifyFixAsync(@"
using UnityEngine;

class C : MonoBehaviour
{
    int score;
    string label;

    void Update()
    {
        label = {|UPA2000:""score: "" + score|};
    }
}", @"
using UnityEngine;
using Cysharp.Text;

class C : MonoBehaviour
{
    int score;
    string label;

    void Update()
    {
        label = ZString.Concat(""score: "", score);
    }
}");
        }

        // UPA2000 test case 9 - two strings measured the same either way, so no bulb
        [Fact]
        public Task ConcatenationOfStrings_Triggers_WithoutFix()
        {
            const string Source = @"
using Cysharp.Text;
using UnityEngine;

class C : MonoBehaviour
{
    string first;
    string second;
    string label;

    void Update()
    {
        label = {|UPA2000:first + second|};
    }
}";
            return VerifyFixAsync(Source, Source);
        }

        // UPA2000 test case 10 - without the type the rewrite would not compile
        [Fact]
        public Task Concatenation_WithoutZString_Triggers_WithoutFix()
        {
            const string Source = @"
using UnityEngine;

class C : MonoBehaviour
{
    int score;
    string label;

    void Update()
    {
        label = {|UPA2000:""score: "" + score|};
    }
}";
            return VerifyFixAsync(Source, Source, referenceZString: false);
        }

        // UPA2000 test case 11 - a compound assignment wants a builder, not a call
        [Fact]
        public Task CompoundAssignment_Triggers_WithoutFix()
        {
            const string Source = @"
using Cysharp.Text;
using UnityEngine;

class C : MonoBehaviour
{
    int score;
    string label = string.Empty;

    void Update()
    {
        {|UPA2000:label += score|};
    }
}";
            return VerifyFixAsync(Source, Source);
        }

        // UPA2000 test case 12 - interpolation holes can carry format specifiers
        [Fact]
        public Task Interpolation_Triggers_WithoutFix()
        {
            const string Source = @"
using Cysharp.Text;
using UnityEngine;

class C : MonoBehaviour
{
    int score;
    string label;

    void Update()
    {
        label = {|UPA2000:$""score: {score}""|};
    }
}";
            return VerifyFixAsync(Source, Source);
        }

        // UPA2000 test case 13 - a chain becomes one call, not nested ones
        [Fact]
        public Task ConcatenationChain_CodeFix_ProducesOneCall()
        {
            return VerifyFixAsync(@"
using Cysharp.Text;
using UnityEngine;

class C : MonoBehaviour
{
    int score;
    string suffix;
    string label;

    void Update()
    {
        label = {|UPA2000:""score: "" + score + suffix|};
    }
}", @"
using Cysharp.Text;
using UnityEngine;

class C : MonoBehaviour
{
    int score;
    string suffix;
    string label;

    void Update()
    {
        label = ZString.Concat(""score: "", score, suffix);
    }
}");
        }

        // UPA2000 test case 14 - a local named ZString shadows the type, and the added using
        // does not help: lexical scope resolves first
        [Fact]
        public Task ShadowedZString_Triggers_WithoutFix()
        {
            const string Source = @"
using Cysharp.Text;
using UnityEngine;

class C : MonoBehaviour
{
    int score;
    string label;

    void Update()
    {
        var ZString = ""shadow"";
        label = {|UPA2000:""score: "" + score|};
        _ = ZString;
    }
}";
            return VerifyFixAsync(Source, Source);
        }

        // UPA2000 test case 15 - a using in another namespace does not reach this one, so the
        // rewrite still needs its own import
        [Fact]
        public Task ImportInAnotherNamespace_CodeFix_StillAddsUsing()
        {
            return VerifyFixAsync(@"
using UnityEngine;

namespace Unrelated
{
    using Cysharp.Text;

    class Ignored
    {
    }
}

namespace Consumer
{
    class C : MonoBehaviour
    {
        int score;
        string label;

        void Update()
        {
            label = {|UPA2000:""score: "" + score|};
        }
    }
}", @"
using UnityEngine;
using Cysharp.Text;

namespace Unrelated
{
    using Cysharp.Text;

    class Ignored
    {
    }
}

namespace Consumer
{
    class C : MonoBehaviour
    {
        int score;
        string label;

        void Update()
        {
            label = ZString.Concat(""score: "", score);
        }
    }
}");
        }
    }
}
