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

            // First, because it is the cheap question and it does not depend on the receiver:
            // everything below binds, and most for loops in a project are not on a hot path.
            if (!hotPathDetector.IsInHotPath(forStatement, context.SemanticModel, context.CancellationToken))
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

                var receiverSymbol = context.SemanticModel.GetSymbolInfo(memberAccess.Expression, context.CancellationToken).Symbol;
                var receiver = new Receiver(
                    receiverName,
                    receiverSymbol,
                    IsMemberReceiver(receiverSymbol),
                    context.SemanticModel.GetEnclosingSymbol(forStatement.SpanStart, context.CancellationToken)?.ContainingType);
                if (LoopMayReachReceiver(forStatement, receiver, context.SemanticModel, context.CancellationToken))
                {
                    continue;
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
        private static bool IsMemberReceiver(ISymbol? receiver)
        {
            switch (receiver)
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
            public Receiver(string name, ISymbol? symbol, bool isMember, INamedTypeSymbol? enclosingType)
            {
                Name = name;
                Symbol = symbol;
                IsMember = isMember;
                EnclosingType = enclosingType;
            }

            public string Name { get; }

            public ISymbol? Symbol { get; }

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
        /// collection the hoisted count was read from. A field is reachable from any code that
        /// runs on its own instance, so a call through <c>this</c> - implicit or written - may
        /// mutate it however innocent its arguments look: <c>Kill(_enemies[i])</c> removing
        /// from <c>_enemies</c> is the ordinary shape of that. A property or indexer of the
        /// instance runs an accessor, which is a call written as an assignment or a read:
        /// <c>CurrentTarget = _targets[i];</c> whose setter drops the old target from
        /// <c>_targets</c>. A constructor handed <c>this</c> has the instance. A local
        /// function may have captured the receiver wherever it was declared, and a delegate -
        /// invoked by name, through <c>.Invoke</c>, through <c>?.Invoke</c>, or as an event
        /// raise - may be a lambda that did, or a subscriber that removes itself. None of
        /// these can be told apart from the harmless case without reading the callee, so all
        /// of them silence the rule.
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
                        // nameof(...) is a constant; it names things without running or
                        // aliasing any of them.
                        if (IsNameof(invocation, model, cancellationToken))
                        {
                            break;
                        }

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

                    // A constructor is a call too: handed the collection it can keep or change
                    // it, handed `this` it has every field of the instance.
                    case BaseObjectCreationExpressionSyntax creation:
                        if (creation.ArgumentList is { } creationArguments &&
                            MentionsBareIdentifier(creationArguments, receiverName))
                        {
                            return true;
                        }

                        if (receiver.IsMember && PassesThis(creation))
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

                    case IdentifierNameSyntax identifier
                        when receiver.IsMember &&
                            IsOnOwnInstance(identifier) &&
                            AccessorMayReachReceiver(identifier, receiver, model, cancellationToken):
                        return true;

                    case ElementAccessExpressionSyntax element
                        when receiver.IsMember &&
                            (element.Expression is ThisExpressionSyntax || element.Expression is BaseExpressionSyntax) &&
                            AccessorMayReachReceiver(element, receiver, model, cancellationToken):
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

            var info = model.GetSymbolInfo(invocation, cancellationToken);
            if (info.Symbol is IMethodSymbol method)
            {
                return MethodMayReachReceiver(method, throughThis, bySimpleName, receiver);
            }

            if (info.CandidateSymbols.IsEmpty)
            {
                // Unbound with nothing to go on (an error, or a dynamic call): nothing says
                // what it runs. That matters where the call could be on this instance.
                return throughThis || bySimpleName;
            }

            // Overload resolution failed but the name did bind to something: judge every
            // candidate as if it were the one, and stay quiet if any of them could reach.
            foreach (var candidate in info.CandidateSymbols)
            {
                if (!(candidate is IMethodSymbol candidateMethod) ||
                    MethodMayReachReceiver(candidateMethod, throughThis, bySimpleName, receiver))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool MethodMayReachReceiver(
            IMethodSymbol method,
            bool throughThis,
            bool bySimpleName,
            Receiver receiver)
        {
            // Wherever it was declared, a local function sees the locals it captured and the
            // instance it runs on. A delegate may be a lambda that did the same, or a
            // subscriber that removes itself from the list being walked; that holds however
            // the delegate is reached - by name, `.Invoke`, `?.Invoke`, off another object -
            // so this is judged on what the call binds to, not on how it is written.
            if (method.MethodKind == MethodKind.LocalFunction ||
                method.MethodKind == MethodKind.DelegateInvoke)
            {
                return true;
            }

            if (!receiver.IsMember || (!throughThis && !bySimpleName))
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

        /// <summary>
        /// Whether a property or indexer of the receiver's own instance (or, for a static
        /// receiver, its own type) runs an accessor that could reach it.
        /// </summary>
        /// <remarks>
        /// An auto-property's accessors only move a value in and out of its backing field, so
        /// those stay reported; anything else declared in source has an accessor body that
        /// can do what a method can. Accessors compiled into another assembly cannot be read.
        /// Reading one is treated as harmless - it is how <c>transform.position</c> appears in
        /// almost every loop a MonoBehaviour writes, and an engine getter does not call back
        /// into a script. Writing one is not: <c>enabled = false</c> runs
        /// <c>OnDisable</c> before it returns.
        /// </remarks>
        private static bool AccessorMayReachReceiver(
            ExpressionSyntax access,
            Receiver receiver,
            SemanticModel model,
            CancellationToken cancellationToken)
        {
            if (!(model.GetSymbolInfo(access, cancellationToken).Symbol is IPropertySymbol property))
            {
                return false;
            }

            // Reading the receiver itself is what the rule is about, not a route around it.
            if (SymbolEqualityComparer.Default.Equals(property, receiver.Symbol) ||
                !IsOwnOrInheritedMember(property.ContainingType, receiver.EnclosingType) ||
                IsInsideNameof(access, model, cancellationToken))
            {
                return false;
            }

            if (property.DeclaringSyntaxReferences.IsEmpty)
            {
                return IsWritten(access);
            }

            return !IsAutoProperty(property);
        }

        // Written as `X` or `this.X`/`base.X`, a member is looked up on the instance the loop
        // runs on (or, if static, on the enclosing type); `other.X` and `?.X` are not.
        private static bool IsOnOwnInstance(IdentifierNameSyntax identifier)
        {
            switch (identifier.Parent)
            {
                case MemberAccessExpressionSyntax member when member.Name == identifier:
                    return member.Expression is ThisExpressionSyntax || member.Expression is BaseExpressionSyntax;
                // `new Other { Health = 1 }` sets a member of the object being created.
                case AssignmentExpressionSyntax initializer
                    when initializer.Left == identifier && initializer.Parent is InitializerExpressionSyntax:
                    return false;
                case MemberBindingExpressionSyntax _:
                case QualifiedNameSyntax _:
                case AliasQualifiedNameSyntax _:
                case NameColonSyntax _:
                    return false;
                default:
                    return true;
            }
        }

        // The compiler gives an auto-property a synthesized backing field that points back
        // at it; a property with accessor bodies has none.
        private static bool IsAutoProperty(IPropertySymbol property)
        {
            var definition = property.OriginalDefinition;
            if (definition.IsIndexer || definition.IsAbstract)
            {
                return false;
            }

            foreach (var member in definition.ContainingType.GetMembers())
            {
                if (member is IFieldSymbol field &&
                    SymbolEqualityComparer.Default.Equals(field.AssociatedSymbol, definition))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsWritten(ExpressionSyntax access)
        {
            var expression = access;
            if (expression.Parent is MemberAccessExpressionSyntax member && member.Name == expression)
            {
                expression = member;
            }

            switch (expression.Parent)
            {
                case AssignmentExpressionSyntax assignment:
                    return assignment.Left == expression;
                case PrefixUnaryExpressionSyntax prefix:
                    return prefix.IsKind(SyntaxKind.PreIncrementExpression) ||
                        prefix.IsKind(SyntaxKind.PreDecrementExpression);
                case PostfixUnaryExpressionSyntax postfix:
                    return postfix.IsKind(SyntaxKind.PostIncrementExpression) ||
                        postfix.IsKind(SyntaxKind.PostDecrementExpression);
                case ArgumentSyntax argument:
                    return !argument.RefKindKeyword.IsKind(SyntaxKind.None);
                default:
                    return false;
            }
        }

        private static bool IsNameof(InvocationExpressionSyntax invocation, SemanticModel model, CancellationToken cancellationToken) =>
            invocation.Expression is IdentifierNameSyntax name &&
            name.Identifier.ValueText == "nameof" &&
            model.GetConstantValue(invocation, cancellationToken).HasValue;

        private static bool IsInsideNameof(SyntaxNode node, SemanticModel model, CancellationToken cancellationToken)
        {
            foreach (var invocation in node.Ancestors().OfType<InvocationExpressionSyntax>())
            {
                if (IsNameof(invocation, model, cancellationToken))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsOwnOrInheritedMember(INamedTypeSymbol? memberType, INamedTypeSymbol? enclosingType)
        {
            if (memberType is null || enclosingType is null)
            {
                return true;
            }

            var declaring = memberType.OriginalDefinition;
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

        // `this` handed out as an argument (or into an object initializer) gives the callee
        // every field of the instance.
        private static bool PassesThis(SyntaxNode arguments)
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
