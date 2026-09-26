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
            var harness = new RuleHarness();
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

        private static Task VerifyTriggersAsync(string source, bool referenceZString = true)
        {
            var harness = new RuleHarness();
            if (referenceZString)
            {
                harness.PackageAssemblies.Add(UpaProfile.ZStringAssemblyName);
                harness.Sources.Add(ZStringStub);
            }

            return RuleVerifier.VerifyAsync<UPA2000StringConcatenationAnalyzer>(source, harness);
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

        // UPA2000 test case 9 - two strings measured the same either way, so no bulb
        [Fact]
        public Task ConcatenationOfStrings_Triggers()
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
            return VerifyTriggersAsync(Source);
        }

        // UPA2000 test case 10 - without the type the rewrite would not compile
        [Fact]
        public Task Concatenation_WithoutZString_Triggers()
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
            return VerifyTriggersAsync(Source, referenceZString: false);
        }

        // UPA2000 test case 11 - a compound assignment wants a builder, not a call
        [Fact]
        public Task CompoundAssignment_Triggers()
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
            return VerifyTriggersAsync(Source);
        }

        // UPA2000 test case 12 - interpolation holes can carry format specifiers
        [Fact]
        public Task Interpolation_Triggers()
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
            return VerifyTriggersAsync(Source);
        }

        // UPA2000 test case 14 - a local named ZString shadows the type, and the added using
        // does not help: lexical scope resolves first
        [Fact]
        public Task ShadowedZString_Triggers()
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
            return VerifyTriggersAsync(Source);
        }
    }
}
