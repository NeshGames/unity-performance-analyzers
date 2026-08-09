using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.CodeAnalysis.Diagnostics;
using Xunit;

namespace UnityPerformanceAnalyzers.Tests
{
    /// <summary>
    /// The two things that cannot be checked by running a rule: that it goes through the
    /// shared skeleton, and that it keeps nothing between compilations.
    /// </summary>
    public class UpaAnalyzerContractTests
    {
        private static IEnumerable<Type> ConcreteAnalyzers()
            => typeof(UpaAnalyzer).Assembly
                .GetTypes()
                .Where(type => typeof(DiagnosticAnalyzer).IsAssignableFrom(type) && !type.IsAbstract);

        // Deriving from DiagnosticAnalyzer directly is allowed with a reason, and nothing here
        // has one. A rule that skips the base class also skips EnableConcurrentExecution and
        // the generated-code setting, and nothing about its output would show it.
        [Fact]
        public void EveryAnalyzer_DerivesFromTheBaseClass()
        {
            var strays = ConcreteAnalyzers()
                .Where(type => !typeof(UpaAnalyzer).IsAssignableFrom(type))
                .Select(type => type.Name)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray();

            Assert.Empty(strays);
        }

        // Roslyn creates one analyzer instance and serves every compilation from it, including
        // concurrently in the IDE. An instance field holding anything derived from a
        // compilation is therefore shared between compilations: the second one is analyzed
        // against the first one's types, references and options, and the result looks exactly
        // like a correct one. Per-compilation state belongs on UpaCompilationContext, which is
        // created inside the callback and passed by argument.
        [Fact]
        public void NoAnalyzer_KeepsInstanceState()
        {
            var fields = new List<string>();
            foreach (var type in ConcreteAnalyzers())
            {
                for (var current = type; current is object && current != typeof(DiagnosticAnalyzer); current = current.BaseType)
                {
                    fields.AddRange(current
                        .GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
                        .Select(field => $"{current.Name}.{field.Name}"));
                }
            }

            Assert.Empty(fields.OrderBy(name => name, StringComparer.Ordinal).ToArray());
        }
    }
}
