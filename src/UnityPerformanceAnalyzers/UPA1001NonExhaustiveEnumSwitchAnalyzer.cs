using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace UnityPerformanceAnalyzers
{
    /// <summary>
    /// UPA1001: Reports switch statements and switch expressions over an enum that do not
    /// handle every declared member. A default branch (or discard arm) counts as exhaustive
    /// unless upa_enum_switch_allow_default is set to false. Flags enums and switches whose
    /// coverage cannot be judged statically (when guards, relational/range patterns,
    /// non-constant labels) are conservatively skipped; same-value aliases count as covered
    /// (docs/rules/UPA1001.md).
    /// </summary>
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class UPA1001NonExhaustiveEnumSwitchAnalyzer : UpaAnalyzer
    {
        /// <summary>The diagnostic ID reported by this analyzer.</summary>
        public const string DiagnosticId = "UPA1001";

        internal const string AllowDefaultOptionKey = "upa_enum_switch_allow_default";

        private const int MaxListedMembers = 5;

        private static readonly DiagnosticDescriptor Rule = UpaDescriptor.Create(
            DiagnosticId,
            DiagnosticCategories.Correctness,
            DiagnosticSeverity.Warning,
            isEnabledByDefault: true);

        private static readonly ImmutableArray<DiagnosticDescriptor> s_supportedDiagnostics =
            ImmutableArray.Create(Rule);

        /// <inheritdoc/>
        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => s_supportedDiagnostics;

        /// <inheritdoc/>
        private protected override void InitializeCore(CompilationStartAnalysisContext ctx)
        {
            var flagsAttributeType = ctx.Compilation.GetTypeByMetadataName("System.FlagsAttribute");
            var allowDefault = ReadAllowDefaultOption(ctx.Compilation, ctx.Options);

            ctx.RegisterOperationAction(
                opCtx => AnalyzeSwitch(opCtx, flagsAttributeType, allowDefault),
                OperationKind.Switch,
                OperationKind.SwitchExpression);
        }

        // Same resolution pattern as HotPathDetector: once per compilation, layered through
        // UpaOptions (options file > .editorconfig > default), defaults on any read failure.
        private static bool ReadAllowDefaultOption(Compilation compilation, AnalyzerOptions analyzerOptions)
        {
            return UpaOptions.Resolve(analyzerOptions).GetBool(
                AllowDefaultOptionKey,
                compilation.SyntaxTrees.FirstOrDefault(),
                analyzerOptions.AnalyzerConfigOptionsProvider,
                fallback: true);
        }

        private static void AnalyzeSwitch(
            OperationAnalysisContext context,
            INamedTypeSymbol? flagsAttributeType,
            bool allowDefault)
        {
            IOperation governing;
            var hasDefault = false;
            var covered = new HashSet<object>();

            switch (context.Operation)
            {
                case ISwitchOperation switchStatement:
                    governing = switchStatement.Value;
                    if (!TryCollectStatementCoverage(switchStatement, covered, ref hasDefault))
                    {
                        return;
                    }

                    break;

                case ISwitchExpressionOperation switchExpression:
                    governing = switchExpression.Value;
                    if (!TryCollectExpressionCoverage(switchExpression, covered, ref hasDefault))
                    {
                        return;
                    }

                    break;

                default:
                    return;
            }

            if (hasDefault && allowDefault)
            {
                return;
            }

            var enumType = governing.Type as INamedTypeSymbol;
            if (enumType is null || enumType.TypeKind != TypeKind.Enum)
            {
                return;
            }

            // Bitwise combinations make exhaustiveness meaningless for flags enums.
            if (flagsAttributeType is object && enumType.GetAttributes().Any(a =>
                    SymbolEqualityComparer.Default.Equals(a.AttributeClass, flagsAttributeType)))
            {
                return;
            }

            // Same-value aliases: any covered value covers all members sharing it, and the
            // missing list names one member per distinct uncovered value.
            var missing = new List<string>();
            var seenValues = new HashSet<object>();
            foreach (var member in enumType.GetMembers().OfType<IFieldSymbol>())
            {
                if (!member.HasConstantValue || member.ConstantValue is null)
                {
                    continue;
                }

                if (covered.Contains(member.ConstantValue) || !seenValues.Add(member.ConstantValue))
                {
                    continue;
                }

                missing.Add(member.Name);
            }

            if (missing.Count == 0)
            {
                return;
            }

            var listed = string.Join(", ", missing.Take(MaxListedMembers));
            if (missing.Count > MaxListedMembers)
            {
                listed += ", …";
            }

            context.ReportDiagnostic(UpaDiagnostics.Create(
                Rule,
                governing.Syntax.GetLocation(),
                enumType.Name,
                listed));
        }

        // Returns false when coverage cannot be judged statically — the switch is skipped.
        private static bool TryCollectStatementCoverage(
            ISwitchOperation switchStatement,
            HashSet<object> covered,
            ref bool hasDefault)
        {
            foreach (var switchCase in switchStatement.Cases)
            {
                foreach (var clause in switchCase.Clauses)
                {
                    switch (clause)
                    {
                        case IDefaultCaseClauseOperation _:
                            hasDefault = true;
                            break;

                        case ISingleValueCaseClauseOperation singleValue
                            when singleValue.Value.ConstantValue.HasValue &&
                                 singleValue.Value.ConstantValue.Value is object value:
                            covered.Add(value);
                            break;

                        case IPatternCaseClauseOperation patternClause when patternClause.Guard is null:
                            if (!TryCollectPattern(patternClause.Pattern, covered, ref hasDefault))
                            {
                                return false;
                            }

                            break;

                        default:
                            return false;
                    }
                }
            }

            return true;
        }

        private static bool TryCollectExpressionCoverage(
            ISwitchExpressionOperation switchExpression,
            HashSet<object> covered,
            ref bool hasDefault)
        {
            foreach (var arm in switchExpression.Arms)
            {
                if (arm.Guard is object)
                {
                    return false;
                }

                if (!TryCollectPattern(arm.Pattern, covered, ref hasDefault))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool TryCollectPattern(IPatternOperation pattern, HashSet<object> covered, ref bool hasDefault)
        {
            switch (pattern)
            {
                case IConstantPatternOperation constantPattern
                    when constantPattern.Value.ConstantValue.HasValue &&
                         constantPattern.Value.ConstantValue.Value is object value:
                    covered.Add(value);
                    return true;

                case IDiscardPatternOperation _:
                    hasDefault = true;
                    return true;

                default:
                    return false;
            }
        }
    }
}
