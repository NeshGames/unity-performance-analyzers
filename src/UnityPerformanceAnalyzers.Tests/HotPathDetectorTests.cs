using System.Collections.Immutable;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Xunit;

namespace UnityPerformanceAnalyzers.Tests
{
    /// <summary>
    /// Reports UPATEST01 on every invocation the detector classifies as hot, so the hot-path
    /// test cases can assert hot-path classification directly with inline markup.
    /// </summary>
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class HotPathProbeAnalyzer : DiagnosticAnalyzer
    {
        public const string DiagnosticId = "UPATEST01";

        private static readonly DiagnosticDescriptor Rule = new DiagnosticDescriptor(
            DiagnosticId,
            "Hot path probe",
            "Invocation is on a hot path",
            "Test",
            DiagnosticSeverity.Warning,
            isEnabledByDefault: true);

        private static readonly ImmutableArray<DiagnosticDescriptor> s_supportedDiagnostics =
            ImmutableArray.Create(Rule);

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => s_supportedDiagnostics;

        public override void Initialize(AnalysisContext context)
        {
            context.EnableConcurrentExecution();
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.RegisterCompilationStartAction(ctx =>
            {
                var detector = HotPathDetector.Create(ctx.Compilation, ctx.Options);
                ctx.RegisterSyntaxNodeAction(
                    nodeCtx =>
                    {
                        if (detector.IsInHotPath(nodeCtx.Node, nodeCtx.SemanticModel, nodeCtx.CancellationToken))
                        {
                            nodeCtx.ReportDiagnostic(Diagnostic.Create(Rule, nodeCtx.Node.GetLocation()));
                        }
                    },
                    SyntaxKind.InvocationExpression);
            });
        }
    }

    public class HotPathDetectorTests
    {
        private const string Prelude = @"
static class Marker
{
    public static void Mark() { }
}
";

        private static Task VerifyAsync(string source, string? extraConfig = null) =>
            RuleVerifier.VerifyAsync<HotPathProbeAnalyzer>(source + Prelude, new RuleHarness
            {
                EditorConfig = extraConfig,
            });

        // Hot-path test case 1
        [Fact]
        public Task UpdateBody_OnMonoBehaviour_IsHot()
        {
            return VerifyAsync(@"
using UnityEngine;

class C : MonoBehaviour
{
    void Update()
    {
        {|UPATEST01:Marker.Mark()|};
    }
}");
        }

        // Hot-path test case 2
        [Fact]
        public Task StartBody_OnMonoBehaviour_IsNotHot()
        {
            return VerifyAsync(@"
using UnityEngine;

class C : MonoBehaviour
{
    void Start()
    {
        Marker.Mark();
    }
}");
        }

        // Hot-path test case 3
        [Fact]
        public Task UpdateBody_OnNonMonoBehaviour_IsNotHot()
        {
            return VerifyAsync(@"
class C
{
    void Update()
    {
        Marker.Mark();
    }
}");
        }

        // Hot-path test case 4
        [Fact]
        public Task LambdaInsideUpdate_IsHot()
        {
            return VerifyAsync(@"
using System;
using UnityEngine;

class C : MonoBehaviour
{
    void Update()
    {
        Action a = () => {|UPATEST01:Marker.Mark()|};
    }
}");
        }

        // Hot-path test case 5
        [Fact]
        public Task LocalFunctionInsideUpdate_IsHot()
        {
            return VerifyAsync(@"
using UnityEngine;

class C : MonoBehaviour
{
    void Update()
    {
        {|UPATEST01:Local()|};

        void Local()
        {
            {|UPATEST01:Marker.Mark()|};
        }
    }
}");
        }

        // Hot-path test case 6 — deliberate miss: no cross-method analysis
        [Fact]
        public Task MethodCalledFromUpdate_BodyIsNotHot()
        {
            return VerifyAsync(@"
using UnityEngine;

class C : MonoBehaviour
{
    void Update()
    {
        {|UPATEST01:Helper()|};
    }

    void Helper()
    {
        Marker.Mark();
    }
}");
        }

        // Hot-path test case 7
        [Fact]
        public Task MethodWithHotPathAttribute_IsHot()
        {
            return VerifyAsync(@"
class HotPathAttribute : System.Attribute { }

class C
{
    [HotPath]
    void M()
    {
        {|UPATEST01:Marker.Mark()|};
    }
}");
        }

        // Hot-path test case 8 — short-name matching only, namespace ignored
        [Fact]
        public Task MethodWithNamespaceQualifiedHotPathAttribute_IsHot()
        {
            return VerifyAsync(@"
namespace MyNamespace
{
    class HotPathAttribute : System.Attribute { }
}

class C
{
    [MyNamespace.HotPath]
    void M()
    {
        {|UPATEST01:Marker.Mark()|};
    }
}");
        }

        // Hot-path test case 9 — case-sensitive match
        [Fact]
        public Task LowercaseUpdate_IsNotHot()
        {
            return VerifyAsync(@"
using UnityEngine;

class C : MonoBehaviour
{
    void update()
    {
        Marker.Mark();
    }
}");
        }

        // Hot-path test case 10 — inheritance chain walks to MonoBehaviour
        [Fact]
        public Task UpdateOnIndirectMonoBehaviourSubclass_IsHot()
        {
            return VerifyAsync(@"
using UnityEngine;

class A : MonoBehaviour { }

class B : A
{
    void Update()
    {
        {|UPATEST01:Marker.Mark()|};
    }
}");
        }

        // Hot-path test case 11 — lambdas excluded when configured off
        [Fact]
        public Task LambdaInsideUpdate_WithIncludeLambdasOff_IsNotHot()
        {
            return VerifyAsync(@"
using System;
using UnityEngine;

class C : MonoBehaviour
{
    void Update()
    {
        Action a = () => Marker.Mark();
    }
}",
                extraConfig: "upa_hot_path_include_lambdas = false");
        }
    }
}
