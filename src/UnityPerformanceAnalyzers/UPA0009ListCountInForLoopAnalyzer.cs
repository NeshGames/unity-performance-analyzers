using System;
using System.Linq;
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
    [HotPathRule]
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class UPA0009ListCountInForLoopAnalyzer : UpaAnalyzer
    {
        /// <summary>The diagnostic ID reported by this analyzer.</summary>
        public const string DiagnosticId = "UPA0009";

        private static readonly DiagnosticDescriptor Rule = UpaDescriptor.Create(
            DiagnosticId,
            DiagnosticCategories.Performance,
            DiagnosticSeverity.Warning,
            isEnabledByDefault: false);

        private static readonly ImmutableArray<DiagnosticDescriptor> s_supportedDiagnostics =
            ImmutableArray.Create(Rule);

        /// <inheritdoc/>
        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => s_supportedDiagnostics;

        /// <inheritdoc/>
        private protected override void InitializeCore(UpaCompilationContext ctx)
        {
            var listType = ctx.Type("System.Collections.Generic.List`1");
            if (listType is null)
            {
                return;
            }

            var hotPathDetector = ctx.HotPath;

            ctx.RegisterSyntaxNodeAction(
                nodeCtx => AnalyzeForStatement(nodeCtx, listType, hotPathDetector),
                SyntaxKind.ForStatement);
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

                if (BodyMayReachReceiver(forStatement.Statement, receiverName))
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

        /// <summary>
        /// Whether anything in the loop body could reach the collection other than by reading
        /// it. Hoisting Count is only correct while the collection does not change, so this
        /// errs towards silence: a report that is wrong here does not merely add noise, it
        /// tells someone to make a change that breaks their program.
        /// </summary>
        /// <remarks>
        /// Three ways an alias appears, and all three are the receiver used as a bare
        /// identifier: passed as an argument, assigned to something else, or used to
        /// initialise a declaration. Reading through it -- <c>list.Count</c>,
        /// <c>list[i]</c> -- cannot produce one, so those still report.
        /// <para>
        /// What remains and cannot be removed: another object may already hold the collection
        /// and mutate it from inside the loop. Nothing visible at this call site says so. The
        /// rule page states this.
        /// </para>
        /// </remarks>
        private static bool BodyMayReachReceiver(StatementSyntax body, string receiverName)
        {
            foreach (var node in body.DescendantNodes())
            {
                switch (node)
                {
                    case InvocationExpressionSyntax invocation:
                        // Any call *on* the collection, whatever it is named. The previous
                        // version compared against a list of mutating method names, and a
                        // closed list of names is what this defect was: a reduced extension
                        // call -- items.TrimToLimit() -- puts the collection in the receiver
                        // position under a name nobody enumerated, and it can mutate freely.
                        if (invocation.Expression is MemberAccessExpressionSyntax memberAccess &&
                            TryGetSimpleReceiverName(memberAccess.Expression) is string called &&
                            string.Equals(called, receiverName, StringComparison.Ordinal))
                        {
                            return true;
                        }

                        if (invocation.ArgumentList is { } arguments &&
                            MentionsBareIdentifier(arguments, receiverName))
                        {
                            return true;
                        }

                        break;

                    case AssignmentExpressionSyntax assignment
                        when MentionsBareIdentifier(assignment.Right, receiverName):
                        return true;

                    case VariableDeclaratorSyntax declarator
                        when declarator.Initializer is { } initializer &&
                            MentionsBareIdentifier(initializer.Value, receiverName):
                        return true;
                }
            }

            return false;
        }

        // The identifier standing for the collection itself, rather than for something read
        // out of it. list.Count and list[i] use the same identifier and alias nothing.
        private static bool MentionsBareIdentifier(SyntaxNode scope, string receiverName)
        {
            foreach (var identifier in scope.DescendantNodesAndSelf().OfType<IdentifierNameSyntax>())
            {
                if (!string.Equals(identifier.Identifier.ValueText, receiverName, StringComparison.Ordinal))
                {
                    continue;
                }

                switch (identifier.Parent)
                {
                    case MemberAccessExpressionSyntax member when member.Expression == identifier:
                    case ElementAccessExpressionSyntax element when element.Expression == identifier:
                        continue;
                    default:
                        return true;
                }
            }

            return false;
        }
    }
}
