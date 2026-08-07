using System;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace UnityPerformanceAnalyzers
{
    /// <summary>
    /// UPA0009: Reports <c>List&lt;T&gt;.Count</c> reads in a hot-path <c>for</c>-loop condition
    /// when the loop body does not mutate the same list. Receiver matching is deliberately
    /// shallow (identifier or <c>this.identifier</c>, normalized); deeper member chains never
    /// trigger — mutation of those cannot be judged reliably, and this analyzer prefers missed
    /// reports over false positives. Arrays are excluded: <c>i &lt; array.Length</c> is the form
    /// the JIT uses to eliminate bounds checks.
    /// </summary>
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class UPA0009ListCountInForLoopAnalyzer : DiagnosticAnalyzer
    {
        /// <summary>The diagnostic ID reported by this analyzer.</summary>
        public const string DiagnosticId = "UPA0009";

        private static readonly DiagnosticDescriptor Rule = new DiagnosticDescriptor(
            DiagnosticId,
            new LocalizableResourceString(Strings.UPA0009Title, Strings.ResourceManager, typeof(Strings)),
            new LocalizableResourceString(Strings.UPA0009MessageFormat, Strings.ResourceManager, typeof(Strings)),
            DiagnosticCategories.Performance,
            DiagnosticSeverity.Warning,
            isEnabledByDefault: false,
            description: new LocalizableResourceString(Strings.UPA0009Description, Strings.ResourceManager, typeof(Strings)),
            helpLinkUri: "https://github.com/NeshGames/unity-performance-analyzers/blob/main/docs/rules/UPA0009.md");

        private static readonly ImmutableArray<DiagnosticDescriptor> s_supportedDiagnostics =
            ImmutableArray.Create(Rule);

        private static readonly ImmutableHashSet<string> s_mutatingMethodNames = ImmutableHashSet.Create(
            StringComparer.Ordinal,
            "Add",
            "AddRange",
            "Insert",
            "InsertRange",
            "Remove",
            "RemoveAt",
            "RemoveAll",
            "RemoveRange",
            "Clear");

        /// <inheritdoc/>
        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => s_supportedDiagnostics;

        /// <inheritdoc/>
        public override void Initialize(AnalysisContext context)
        {
            context.EnableConcurrentExecution();
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.RegisterCompilationStartAction(ctx =>
            {
                var listType = ctx.Compilation.GetTypeByMetadataName("System.Collections.Generic.List`1");
                if (listType is null)
                {
                    return;
                }

                var hotPathDetector = HotPathDetector.Create(ctx.Compilation, ctx.Options);

                ctx.RegisterSyntaxNodeAction(
                    nodeCtx => AnalyzeForStatement(nodeCtx, listType, hotPathDetector),
                    SyntaxKind.ForStatement);
            });
        }

        private static void AnalyzeForStatement(
            SyntaxNodeAnalysisContext context,
            INamedTypeSymbol listType,
            HotPathDetector hotPathDetector)
        {
            var forStatement = (ForStatementSyntax)context.Node;
            if (forStatement.Condition is null)
            {
                return;
            }

            foreach (var node in forStatement.Condition.DescendantNodesAndSelf())
            {
                if (!(node is MemberAccessExpressionSyntax memberAccess) ||
                    memberAccess.Name.Identifier.ValueText != "Count")
                {
                    continue;
                }

                var receiverName = TryGetSimpleReceiverName(memberAccess.Expression);
                if (receiverName is null)
                {
                    continue;
                }

                var property = context.SemanticModel.GetSymbolInfo(memberAccess, context.CancellationToken).Symbol
                    as IPropertySymbol;
                if (property is null ||
                    !SymbolEqualityComparer.Default.Equals(property.ContainingType.OriginalDefinition, listType))
                {
                    continue;
                }

                if (BodyMutatesReceiver(forStatement.Statement, receiverName))
                {
                    continue;
                }

                if (!hotPathDetector.IsInHotPath(forStatement, context.SemanticModel, context.CancellationToken))
                {
                    return;
                }

                context.ReportDiagnostic(UpaDiagnostics.Create(
                    Rule,
                    memberAccess.GetLocation(),
                    receiverName));
            }
        }

        // Only a bare identifier or this.identifier counts as a matchable receiver; deeper
        // chains return null and are never reported.
        private static string? TryGetSimpleReceiverName(ExpressionSyntax expression)
        {
            switch (expression)
            {
                case IdentifierNameSyntax identifier:
                    return identifier.Identifier.ValueText;
                case MemberAccessExpressionSyntax thisAccess
                    when thisAccess.Expression is ThisExpressionSyntax &&
                        thisAccess.Name is IdentifierNameSyntax member:
                    return member.Identifier.ValueText;
                default:
                    return null;
            }
        }

        private static bool BodyMutatesReceiver(StatementSyntax body, string receiverName)
        {
            foreach (var node in body.DescendantNodes())
            {
                if (!(node is InvocationExpressionSyntax invocation) ||
                    !(invocation.Expression is MemberAccessExpressionSyntax memberAccess) ||
                    !s_mutatingMethodNames.Contains(memberAccess.Name.Identifier.ValueText))
                {
                    continue;
                }

                if (TryGetSimpleReceiverName(memberAccess.Expression) is string mutatedReceiver &&
                    string.Equals(mutatedReceiver, receiverName, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
