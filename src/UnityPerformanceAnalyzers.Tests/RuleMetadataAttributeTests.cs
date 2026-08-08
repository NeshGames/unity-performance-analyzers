using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.CodeAnalysis.Diagnostics;
using Xunit;

namespace UnityPerformanceAnalyzers.Tests
{
    /// <summary>
    /// Guards the declarative rule metadata that the catalog is generated from. The
    /// attributes travel with the analyzers, so drift can only happen when an annotation
    /// itself is wrong — these tests pin the expected sets.
    /// </summary>
    public class RuleMetadataAttributeTests
    {
        private static IEnumerable<Type> AnalyzerTypes =>
            typeof(UPA0001ComponentLookupAnalyzer).Assembly.GetTypes()
                .Where(t => !t.IsAbstract && typeof(DiagnosticAnalyzer).IsAssignableFrom(t));

        [Fact]
        public void HotPathRules_MatchTheExpectedSet()
        {
            var annotated = AnalyzerTypes
                .Where(t => t.GetCustomAttribute<HotPathRuleAttribute>() is object)
                .Select(t => t.Name)
                .OrderBy(n => n, StringComparer.Ordinal)
                .ToArray();

            // UPA0003 deliberately absent: hot-path narrowing there is an opt-in option,
            // not the rule's default scope.
            var expected = new[]
            {
                "UPA0001ComponentLookupAnalyzer", "UPA0002NameTagAccessAnalyzer",
                "UPA0004InstantiatingAccessorAnalyzer", "UPA0006HotPathAllocationAnalyzer",
                "UPA0007CapturingLambdaAnalyzer", "UPA0009ListCountInForLoopAnalyzer",
                "UPA0012TmpTextAssignmentAnalyzer", "UPA0013LinqUsageAnalyzer",
                "UPA0014SceneSearchAnalyzer", "UPA0015CameraMainAnalyzer",
                "UPA0017GetComponentsArrayAnalyzer", "UPA0018AllocatingArrayApiAnalyzer",
                "UPA0022HasFlagAnalyzer", "UPA0024ResourcesLoadAnalyzer",
                "UPA0026BoxedReceiverCallAnalyzer", "UPA0027ParamsArrayAllocationAnalyzer",
                "UPA0030KnownAllocatingBclApiAnalyzer", "UPA2000StringConcatenationAnalyzer",
                "UPA2030TweenCreationAnalyzer",
            };

            Assert.Equal(expected, annotated);
        }

        [Fact]
        public void ConditionalRules_MatchTheExpectedConditions()
        {
            var annotated = AnalyzerTypes
                .Select(t => (t.Name, t.GetCustomAttribute<ConditionalRuleAttribute>()?.Condition))
                .Where(x => x.Condition is object)
                .OrderBy(x => x.Name, StringComparer.Ordinal)
                .ToArray();

            var expected = new (string, string?)[]
            {
                ("UPA2010AsyncTaskReturnAnalyzer", "UniTask"),
                ("UPA2011CoroutineAnalyzer", "UniTask"),
                ("UPA2021ActionEventAnalyzer", "R3"),
                ("UPA2030TweenCreationAnalyzer", "DOTween"),
                ("UPA2031DiscardedInfiniteTweenAnalyzer", "DOTween"),
                ("UPA2032StringTweenIdAnalyzer", "DOTween"),
                ("UPA3000WebGlUnsupportedApiAnalyzer", "WebGL"),
                ("UPA3004BlockingWaitAnalyzer", "WebGL"),
            };

            Assert.Equal(expected, annotated);
        }

        [Fact]
        public void ConditionValues_AreFromTheKnownVocabulary()
        {
            var known = new HashSet<string>(StringComparer.Ordinal) { "UniTask", "ZString", "R3", "DOTween", "WebGL" };
            foreach (var type in AnalyzerTypes)
            {
                var condition = type.GetCustomAttribute<ConditionalRuleAttribute>()?.Condition;
                if (condition is object)
                {
                    Assert.Contains(condition, known);
                }
            }
        }
    }
}
