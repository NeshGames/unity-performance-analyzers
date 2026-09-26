using System;
using System.Linq;
using System.Collections.Immutable;
using System.Threading;
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
    [UpaClaim(UpaClaimKind.PerFrameCost)]
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

                var receiver = new Receiver(
                    receiverName,
                    IsMemberReceiver(memberAccess.Expression, context.SemanticModel, context.CancellationToken),
                    context.SemanticModel.GetEnclosingSymbol(forStatement.SpanStart, context.CancellationToken)?.ContainingType);
                if (LoopMayReachReceiver(forStatement, receiver, context.SemanticModel, context.CancellationToken))
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

        // A field or property can be reached by any code running on the instance (or, when
        // static, on the type); a local or parameter only by code that was handed it or
        // captured it. An unresolved receiver is treated as the wider of the two.
        private static bool IsMemberReceiver(ExpressionSyntax receiver, SemanticModel model, CancellationToken cancellationToken)
        {
            switch (model.GetSymbolInfo(receiver, cancellationToken).Symbol)
            {
                case ILocalSymbol _:
                case IParameterSymbol _:
                    return false;
                default:
                    return true;
            }
        }

        /// <summary>The collection whose Count is read, and what could reach it besides the loop.</summary>
        private readonly struct Receiver
        {
            public Receiver(string name, bool isMember, INamedTypeSymbol? enclosingType)
            {
                Name = name;
                IsMember = isMember;
                EnclosingType = enclosingType;
            }

            public string Name { get; }

            public bool IsMember { get; }

            public INamedTypeSymbol? EnclosingType { get; }
        }

        /// <summary>
        /// Whether anything in the loop - body, initializer or incrementor - could reach the
        /// collection other than by reading it. Hoisting Count is only correct while the
        /// collection does not change, so this errs towards silence: a report that is wrong
        /// here does not merely add noise, it tells someone to make a change that breaks their
        /// program.
        /// </summary>
        /// <remarks>
        /// The initializer runs before the hoisted read would, so
        /// <c>for (int i = Reset(items); i &lt; items.Count; i++)</c> iterates a different
        /// number of times once Count is lifted out. The incrementor runs between iterations
        /// and can do the same. Neither was scanned until a pre-push review pointed at the
        /// initializer, and the rule is where that belongs: if the advice cannot be followed
        /// safely, the advice is what is wrong.
        /// </remarks>
        private static bool LoopMayReachReceiver(
            ForStatementSyntax loop,
            Receiver receiver,
            SemanticModel model,
            CancellationToken cancellationToken)
        {
            if (loop.Declaration is { } declaration && MayReachReceiver(declaration, receiver, model, cancellationToken))
            {
                return true;
            }

            foreach (var initializer in loop.Initializers)
            {
                if (MayReachReceiver(initializer, receiver, model, cancellationToken))
                {
                    return true;
                }
            }

            foreach (var incrementor in loop.Incrementors)
            {
                if (MayReachReceiver(incrementor, receiver, model, cancellationToken))
                {
                    return true;
                }
            }

            return MayReachReceiver(loop.Statement, receiver, model, cancellationToken);
        }

        /// <summary>
        /// Whether this fragment of the loop could reach the collection other than by reading
        /// it.
        /// </summary>
        /// <remarks>
        /// Three ways an alias appears, and all three are the receiver used as a bare
        /// identifier: passed as an argument, assigned to something else, or used to
        /// initialise a declaration. Reading through it -- <c>list.Count</c>,
        /// <c>list[i]</c> -- cannot produce one, so those still report.
        /// <para>
        /// Then the routes that need no alias. Assigning the receiver itself replaces the
        /// collection the hoisted count was read from. A field is reachable from any method of
        /// its own type, so a call through <c>this</c> - implicit or written - may mutate it
        /// however innocent its arguments look: <c>Kill(_enemies[i])</c> removing from
        /// <c>_enemies</c> is the ordinary shape of that. A local function may have captured
        /// the receiver wherever it was declared, and a delegate invoked by name may be a
        /// lambda that did. None of these can be told apart from the harmless case without
        /// reading the callee, so all of them silence the rule.
        /// </para>
        /// <para>
        /// What remains and cannot be removed: another object may already hold the collection
        /// and mutate it from inside the loop. Nothing visible at this call site says so. The
        /// rule page states this.
        /// </para>
        /// </remarks>
        private static bool MayReachReceiver(
            SyntaxNode scope,
            Receiver receiver,
            SemanticModel model,
            CancellationToken cancellationToken)
        {
            var receiverName = receiver.Name;
            foreach (var node in scope.DescendantNodesAndSelf())
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

                        if (receiver.IsMember &&
                            invocation.ArgumentList is { } thisArguments &&
                            PassesThis(thisArguments))
                        {
                            return true;
                        }

                        if (CallMayReachReceiver(invocation, receiver, model, cancellationToken))
                        {
                            return true;
                        }

                        break;

                    // Either side: on the right the collection escapes into another name, on
                    // the left it is replaced - `items = GetMore();` - and a count hoisted
                    // before the loop belongs to a list the loop is no longer reading.
                    case AssignmentExpressionSyntax assignment
                        when MentionsBareIdentifier(assignment.Right, receiverName) ||
                            MentionsBareIdentifier(assignment.Left, receiverName):
                        return true;

                    case VariableDeclaratorSyntax declarator
                        when declarator.Initializer is { } initializer &&
                            MentionsBareIdentifier(initializer.Value, receiverName):
                        return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Whether this call runs code that may reach the receiver without being handed it.
        /// </summary>
        private static bool CallMayReachReceiver(
            InvocationExpressionSyntax invocation,
            Receiver receiver,
            SemanticModel model,
            CancellationToken cancellationToken)
        {
            var target = invocation.Expression;
            var throughThis = target is MemberAccessExpressionSyntax qualified &&
                (qualified.Expression is ThisExpressionSyntax || qualified.Expression is BaseExpressionSyntax);
            var bySimpleName = target is SimpleNameSyntax;
            if (!throughThis && !bySimpleName)
            {
                return false;
            }

            var method = model.GetSymbolInfo(invocation, cancellationToken).Symbol as IMethodSymbol;
            if (method is null)
            {
                // Unbound (an error, or a dynamic call): nothing says what it runs.
                return true;
            }

            // Wherever it was declared, a local function sees the locals it captured and the
            // instance it runs on; a delegate invoked by name may be a lambda that did the same.
            if (method.MethodKind == MethodKind.LocalFunction ||
                method.MethodKind == MethodKind.DelegateInvoke)
            {
                return true;
            }

            if (!receiver.IsMember)
            {
                return false;
            }

            // `this.Anything()` - including an extension that takes the instance - runs with
            // the instance in hand.
            if (throughThis)
            {
                return true;
            }

            // A simple name that binds to the enclosing type's own methods, its bases' or an
            // outer type's is an implicit-this or same-type static call. A `using static`
            // import binds elsewhere and cannot see the field.
            return IsOwnOrInheritedMember(method.ContainingType, receiver.EnclosingType);
        }

        private static bool IsOwnOrInheritedMember(INamedTypeSymbol? methodType, INamedTypeSymbol? enclosingType)
        {
            if (methodType is null || enclosingType is null)
            {
                return true;
            }

            var declaring = methodType.OriginalDefinition;
            for (var outer = enclosingType; outer is not null; outer = outer.ContainingType)
            {
                for (var type = outer; type is not null; type = type.BaseType)
                {
                    if (SymbolEqualityComparer.Default.Equals(type.OriginalDefinition, declaring))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        // `this` handed out as an argument gives the callee every field of the instance.
        private static bool PassesThis(ArgumentListSyntax arguments)
        {
            foreach (var node in arguments.DescendantNodes())
            {
                if (node is ThisExpressionSyntax &&
                    !(node.Parent is MemberAccessExpressionSyntax member && member.Expression == node))
                {
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
