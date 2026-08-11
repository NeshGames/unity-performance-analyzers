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
    /// Unity strips OnDrawGizmos, OnDrawGizmosSelected, OnValidate and Reset
    /// from a player build, so per-frame cost inside them costs nothing — while a defect inside
    /// them is still a defect.
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
