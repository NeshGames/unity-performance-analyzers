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
            var test = new CSharpAnalyzerTest<UPA2000StringConcatenationAnalyzer, DefaultVerifier>
            {
                TestCode = source,
                ReferenceAssemblies = ReferenceAssemblies.NetStandard.NetStandard20,
            };
            test.TestState.AdditionalReferences.Add(typeof(UnityEngine.MonoBehaviour).Assembly);
            if (referenceZString)
            {
                test.TestState.AdditionalReferences.Add(
                    TestMetadataReferences.EmptyAssembly(UpaProfile.ZStringAssemblyName));
            }

            // UPA2000 is disabled by default; enable it the same way a preset would.
            test.TestState.AnalyzerConfigFiles.Add(("/.editorconfig", @"
root = true

[*.cs]
dotnet_diagnostic.UPA2000.severity = warning
"));

            return test;
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
    }
}
