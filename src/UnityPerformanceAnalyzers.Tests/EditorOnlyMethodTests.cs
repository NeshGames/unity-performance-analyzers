using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

using Xunit;

namespace UnityPerformanceAnalyzers.Tests
{
    /// <summary>
    /// Unity calls OnDrawGizmos, OnDrawGizmosSelected, OnValidate and the [MenuItem],
    /// [InitializeOnLoadMethod] and [DidReloadScripts] callbacks only in the editor, so
    /// per-frame cost inside them costs nothing in a player — while a defect inside them is
    /// still a defect.
    /// </summary>
    /// <remarks>
    /// This is not the hot-path exclusion. OnDrawGizmos was never in HOT_MESSAGES, which covered
    /// the hot-path-scoped rules; the rules that are not hot-path scoped never consult that
    /// detector, which is how two magnitude findings ended up in OnDrawGizmosSelected on real
    /// game code.
    /// </remarks>
    public class EditorOnlyMethodTests
    {
        private static IEnumerable<Type> AnalyzerTypes =>
            typeof(UPA0001ComponentLookupAnalyzer).Assembly.GetTypes()
                .Where(t => !t.IsAbstract && typeof(DiagnosticAnalyzer).IsAssignableFrom(t));

        /// <summary>a cost rule goes quiet in a gizmo method.</summary>
        [Fact]
        public Task PerFrameCostRule_InOnDrawGizmos_DoesNotTrigger()
        {
            return RuleVerifier.VerifyAsync<UPA0003StringPropertyAccessAnalyzer>(@"
using UnityEngine;

class C : MonoBehaviour
{
    public Material mat;

    void OnDrawGizmos()
    {
        mat.SetFloat(""_A"", 1f);
    }
}");
        }

        /// <summary>OnDrawGizmosSelected, the other gizmo message.</summary>
        [Fact]
        public Task PerFrameCostRule_InOnDrawGizmosSelected_DoesNotTrigger()
        {
            return RuleVerifier.VerifyAsync<UPA0003StringPropertyAccessAnalyzer>(@"
using UnityEngine;

class C : MonoBehaviour
{
    public Material mat;

    void OnDrawGizmosSelected()
    {
        mat.SetFloat(""_A"", 1f);
    }
}");
        }

        /// <summary>
        /// OnValidate is in the set because a build never calls it, not because it
        /// is rare — it fires on every inspector edit and is not rare at all.
        /// </summary>
        [Fact]
        public Task PerFrameCostRule_InOnValidate_DoesNotTrigger()
        {
            return RuleVerifier.VerifyAsync<UPA0003StringPropertyAccessAnalyzer>(@"
using UnityEngine;

class C : MonoBehaviour
{
    public Material mat;

    void OnValidate()
    {
        mat.SetFloat(""_A"", 1f);
    }
}");
        }

        /// <summary>the control. Without it, a rule switched off entirely passes.</summary>
        [Fact]
        public Task PerFrameCostRule_InOrdinaryMethod_StillTriggers()
        {
            return RuleVerifier.VerifyAsync<UPA0003StringPropertyAccessAnalyzer>(@"
using UnityEngine;

class C : MonoBehaviour
{
    public Material mat;

    void Apply()
    {
        {|UPA0003:mat.SetFloat(""_A"", 1f)|};
    }
}");
        }

        /// <summary>
        /// the asymmetry that gives this section its shape. Being in editor code
        /// does not make a non-exhaustive switch correct.
        /// </summary>
        [Fact]
        public Task CorrectnessRule_InOnDrawGizmos_StillTriggers()
        {
            return RuleVerifier.VerifyAsync<UPA1001NonExhaustiveEnumSwitchAnalyzer>(@"
using UnityEngine;

enum Mode { A = 1, B = 2, C = 4 }

class C : MonoBehaviour
{
    public Mode mode;

    void OnDrawGizmos()
    {
        switch ({|UPA1001:mode|})
        {
            case Mode.A: break;
        }
    }
}", new RuleHarness { UnityStubs = true });
        }

        /// <summary>a method Unity never calls is an ordinary method.</summary>
        [Fact]
        public Task MethodNamedLikeAGizmo_OnAPlainClass_StillTriggers()
        {
            return RuleVerifier.VerifyAsync<UPA0003StringPropertyAccessAnalyzer>(@"
using UnityEngine;

class NotABehaviour
{
    public Material mat = null!;

    public void OnDrawGizmos()
    {
        {|UPA0003:mat.SetFloat(""_A"", 1f)|};
    }
}");
        }

        /// <summary>a lambda inherits the method it is written in.</summary>
        [Fact]
        public Task LambdaInsideAGizmoMethod_DoesNotTrigger()
        {
            return RuleVerifier.VerifyAsync<UPA0003StringPropertyAccessAnalyzer>(@"
using System;
using UnityEngine;

class C : MonoBehaviour
{
    public Material mat;

    void OnDrawGizmos()
    {
        Action draw = () => mat.SetFloat(""_A"", 1f);
        draw();
    }
}");
        }

        /// <summary>
        /// Reset is Unity's editor-only message and also the conventional name of a pooling
        /// method game code calls every frame. Nothing at the report site tells them apart,
        /// and a silence in the second case cannot be noticed - so the name is not exempt.
        /// </summary>
        [Fact]
        public Task PerFrameCostRule_InReset_StillTriggers()
        {
            return RuleVerifier.VerifyAsync<UPA0003StringPropertyAccessAnalyzer>(@"
using UnityEngine;

class Bullet : MonoBehaviour
{
    public Material mat;

    public void Reset()
    {
        {|UPA0003:mat.SetFloat(""_Alpha"", 1f)|};
    }
}");
        }

        /// <summary>
        /// The editor callback attributes, spelled the way Unity spells them. MenuItem and
        /// DidReloadScripts carry no Attribute suffix, and a check written against the suffixed
        /// spelling never matched either. UPA0010 because it is not hot-path scoped and has no
        /// exemption of its own for these, so nothing but this filter can silence it here.
        /// </summary>
        [Theory]
        [InlineData("[MenuItem(\"Tools/Measure\")]")]
        [InlineData("[MenuItem(\"Tools/Measure\", false, 10)]")]
        [InlineData("[InitializeOnLoadMethod]")]
        [InlineData("[DidReloadScripts]")]
        [InlineData("[DidReloadScripts(1)]")]
        public Task PerFrameCostRule_InEditorCallback_DoesNotTrigger(string attribute)
        {
            return RuleVerifier.VerifyAsync<UPA0010UnboundedRaycastAnalyzer>(@"
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEngine;

static class Tools
{
    " + attribute + @"
    static void Measure()
    {
        Physics.Raycast(new Vector3(), new Vector3());
    }
}");
        }

        /// <summary>
        /// The control for the case above: the same method with no attribute reports, so the
        /// silence there comes from the attribute and not from the rule missing the shape.
        /// </summary>
        [Fact]
        public Task PerFrameCostRule_InStaticMethodWithoutEditorAttribute_StillTriggers()
        {
            return RuleVerifier.VerifyAsync<UPA0010UnboundedRaycastAnalyzer>(@"
using UnityEngine;

static class Tools
{
    static void Measure()
    {
        {|UPA0010:Physics.Raycast(new Vector3(), new Vector3())|};
    }
}");
        }

        /// <summary>
        /// A project's own attribute with Unity's name is an ordinary attribute. Matching by
        /// name would let it silence rules in runtime code, and the silence would leave nothing
        /// behind to notice.
        /// </summary>
        [Theory]
        [InlineData("MenuItem")]
        [InlineData("MenuItemAttribute")]
        [InlineData("DidReloadScripts")]
        public Task PerFrameCostRule_UnderAProjectsOwnLookalikeAttribute_StillTriggers(string className)
        {
            return RuleVerifier.VerifyAsync<UPA0010UnboundedRaycastAnalyzer>(@"
using UnityEngine;

namespace Game.Tooling
{
    [System.AttributeUsage(System.AttributeTargets.Method)]
    sealed class " + className + @" : System.Attribute
    {
        public " + className + @"(string path = """") { }
    }

    static class Tools
    {
        static Vector3 a;
        static Vector3 b;

        [" + className + @"(""Tools/Measure"")]
        static void Measure()
        {
            if ({|UPA0021:Vector3.Distance(a, b) > 2f|})
            {
            }
        }
    }
}");
        }

        /// <summary>
        /// A partial method's attributes can sit on the declaration while the body sits on the
        /// implementation, which the syntax of the body alone does not show.
        /// </summary>
        [Fact]
        public Task PerFrameCostRule_InPartialMethodAttributedOnItsOtherPart_DoesNotTrigger()
        {
            return RuleVerifier.VerifyAsync<UPA0010UnboundedRaycastAnalyzer>(@"
using UnityEditor;
using UnityEngine;

static partial class Tools
{
    [MenuItem(""Tools/Measure"")]
    static partial void Measure();

    static partial void Measure()
    {
        Physics.Raycast(new Vector3(), new Vector3());
    }
}");
        }

        /// <summary>
        /// Syntax-node rules go through the same filter as operation rules; this is the one
        /// test that would notice if only one of the two registrations applied it.
        /// </summary>
        [Fact]
        public Task SyntaxNodeRule_InOnValidate_DoesNotTrigger()
        {
            return RuleVerifier.VerifyAsync<UPA0008StackallocInLoopAnalyzer>(@"
using UnityEngine;

class C : MonoBehaviour
{
    unsafe void OnValidate()
    {
        for (int i = 0; i < 10; i++)
        {
            int* p = stackalloc int[16];
        }
    }

    unsafe void Apply()
    {
        for (int i = 0; i < 10; i++)
        {
            int* p = {|UPA0008:stackalloc int[16]|};
        }
    }
}", new RuleHarness { AllowUnsafe = true });
        }

        // ---------------------------------------------------------------------------------
        // Contract. Whether a rule is excluded here must be declared, not
        // inferred: an implementer who reads a rule's "claim" wrong produces output that looks
        // exactly like a correct implementation.
        // ---------------------------------------------------------------------------------

        /// <summary>Every analyzer declares its claim; a missing one is a build-time omission nobody sees.</summary>
        [Fact]
        public void EveryAnalyzer_DeclaresItsClaimKind()
        {
            var missing = AnalyzerTypes
                .Where(t => t.GetCustomAttribute<UpaClaimAttribute>() is null)
                .Select(t => t.Name)
                .OrderBy(n => n, StringComparer.Ordinal)
                .ToArray();

            Assert.Empty(missing);
        }

        /// <summary>
        /// Only one direction is constrained: a Correctness-category rule reports
        /// a defect by definition. The other categories carry both kinds — UPA0019 is filed
        /// under Performance and reports a defect (Unity reads a boxed yield as null), and
        /// UPA0028 is about how a type is declared. A rule that decided this from its category
        /// would silence both in gizmo methods.
        /// </summary>
        [Fact]
        public void CorrectnessCategoryRules_DeclareACorrectnessClaim()
        {
            var wrong = new List<string>();

            foreach (var type in AnalyzerTypes)
            {
                var claim = type.GetCustomAttribute<UpaClaimAttribute>();
                if (claim is null)
                {
                    continue;
                }

                var analyzer = (DiagnosticAnalyzer)Activator.CreateInstance(type)!;
                foreach (var descriptor in analyzer.SupportedDiagnostics)
                {
                    if (descriptor.Category == "Correctness" && claim.Kind != UpaClaimKind.Correctness)
                    {
                        wrong.Add($"{type.Name} is category Correctness but claims {claim.Kind}");
                    }
                }
            }

            Assert.Empty(wrong);
        }

        /// <summary>
        /// The claim decides real behaviour, so the two rules whose category disagrees with it
        /// are named here. If someone "tidies" either into matching its category, this fails and
        /// says which invariant they broke.
        /// </summary>
        [Theory]
        [InlineData(typeof(UPA0019BoxedYieldAnalyzer))]
        [InlineData(typeof(UPA0028ValueTypeCollectionKeyAnalyzer))]
        public void PerformanceCategoryRulesThatReportDefects_ClaimCorrectness(Type analyzerType)
        {
            var claim = analyzerType.GetCustomAttribute<UpaClaimAttribute>();

            Assert.NotNull(claim);
            Assert.Equal(UpaClaimKind.Correctness, claim!.Kind);
        }
    }
}
