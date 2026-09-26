using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;
using Microsoft.CodeAnalysis.Text;

namespace UnityPerformanceAnalyzers
{
    /// <summary>
    /// UPA2012: Reports two shapes under one rule ID — (A) async void methods, local functions
    /// and async lambdas converted to a void-returning delegate, whose exceptions escape every
    /// caller, and (B) expression-statement invocations (including through <c>?.</c>) that
    /// return Task/Task&lt;T&gt;/UniTask/UniTask&lt;T&gt; with the result discarded, silently
    /// losing exceptions. Registered unconditionally — UniTask presence only
    /// switches the advice sentence. Event-handler-signature async void, awaited calls,
    /// stored/passed results, .Forget(), and `_ =` discards are excluded (docs/rules/UPA2012.md).
    /// </summary>
    [UpaClaim(UpaClaimKind.Correctness)]
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class UPA2012FireAndForgetAnalyzer : UpaAnalyzer
    {
        /// <summary>The diagnostic ID reported by this analyzer.</summary>
        public const string DiagnosticId = "UPA2012";

        private const string HelpLinkUri =
            "https://github.com/NeshGames/unity-performance-analyzers/blob/main/docs/rules/UPA2012.md";

        private static readonly DiagnosticDescriptor s_asyncVoidRule = UpaDescriptor.Create(
            DiagnosticId,
            DiagnosticCategories.Ecosystem,
            DiagnosticSeverity.Warning,
            isEnabledByDefault: false,
            messageFormatKey: Strings.UPA2012MessageFormatAsyncVoid);

        private static readonly DiagnosticDescriptor s_fireAndForgetRule = UpaDescriptor.Create(
            DiagnosticId,
            DiagnosticCategories.Ecosystem,
            DiagnosticSeverity.Warning,
            isEnabledByDefault: false,
            messageFormatKey: Strings.UPA2012MessageFormatFireAndForget);

        // A lambda has no name to put in the async-void message, and the fix differs: the
        // delegate type is usually someone else's (UnityAction, Action<T>), so "return Task
        // instead" is not a change the author can make.
        private static readonly DiagnosticDescriptor s_asyncLambdaRule = UpaDescriptor.Create(
            DiagnosticId,
            DiagnosticCategories.Ecosystem,
            DiagnosticSeverity.Warning,
            isEnabledByDefault: false,
            messageFormatKey: Strings.UPA2012MessageFormatAsyncLambda);

        private static readonly ImmutableArray<DiagnosticDescriptor> s_supportedDiagnostics =
            ImmutableArray.Create(s_asyncVoidRule, s_fireAndForgetRule, s_asyncLambdaRule);

        private static readonly LocalizableResourceString s_adviceAsyncVoidDefault =
            new LocalizableResourceString(Strings.UPA2012AdviceAsyncVoidDefault, Strings.ResourceManager, typeof(Strings));

        private static readonly LocalizableResourceString s_adviceAsyncVoidUniTask =
            new LocalizableResourceString(Strings.UPA2012AdviceAsyncVoidUniTask, Strings.ResourceManager, typeof(Strings));

        private static readonly LocalizableResourceString s_adviceAsyncLambdaDefault =
            new LocalizableResourceString(Strings.UPA2012AdviceAsyncLambdaDefault, Strings.ResourceManager, typeof(Strings));

        private static readonly LocalizableResourceString s_adviceAsyncLambdaUniTask =
            new LocalizableResourceString(Strings.UPA2012AdviceAsyncLambdaUniTask, Strings.ResourceManager, typeof(Strings));

        private static readonly LocalizableResourceString s_adviceFireAndForgetDefault =
            new LocalizableResourceString(Strings.UPA2012AdviceFireAndForgetDefault, Strings.ResourceManager, typeof(Strings));

        private static readonly LocalizableResourceString s_adviceFireAndForgetUniTask =
            new LocalizableResourceString(Strings.UPA2012AdviceFireAndForgetUniTask, Strings.ResourceManager, typeof(Strings));

        /// <inheritdoc/>
        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => s_supportedDiagnostics;

        /// <inheritdoc/>
        private protected override void InitializeCore(UpaCompilationContext ctx)
        {
            var profile = ctx.Profile;
            var adviceAsyncVoid = profile.HasUniTask ? s_adviceAsyncVoidUniTask : s_adviceAsyncVoidDefault;
            var adviceFireAndForget = profile.HasUniTask ? s_adviceFireAndForgetUniTask : s_adviceFireAndForgetDefault;
            var adviceAsyncLambda = profile.HasUniTask ? s_adviceAsyncLambdaUniTask : s_adviceAsyncLambdaDefault;

            var eventArgsType = ctx.Type("System.EventArgs");
            ctx.RegisterSyntaxNodeAction(
                nodeCtx => AnalyzeAsyncVoid(nodeCtx, eventArgsType, adviceAsyncVoid),
                SyntaxKind.MethodDeclaration,
                SyntaxKind.LocalFunctionStatement);

            ctx.RegisterOperationAction(
                opCtx => AnalyzeAsyncLambda(opCtx, eventArgsType, adviceAsyncLambda),
                OperationKind.AnonymousFunction);

            var taskLikeTypes = GetTaskLikeTypes(ctx.Compilation);
            if (!taskLikeTypes.IsEmpty)
            {
                ctx.RegisterOperationAction(
                    opCtx => AnalyzeInvocation(opCtx, taskLikeTypes, adviceFireAndForget),
                    OperationKind.Invocation);
            }
        }

        private static ImmutableArray<INamedTypeSymbol> GetTaskLikeTypes(Compilation compilation)
        {
            return WellKnownTypes.Resolve(compilation, new[]
            {
                "System.Threading.Tasks.Task",
                "System.Threading.Tasks.Task`1",
                "Cysharp.Threading.Tasks.UniTask",
                "Cysharp.Threading.Tasks.UniTask`1",
            });
        }

        private static void AnalyzeAsyncVoid(
            SyntaxNodeAnalysisContext context,
            INamedTypeSymbol? eventArgsType,
            LocalizableString advice)
        {
            // A local function is the same declaration in a smaller scope: its exceptions
            // escape its callers exactly as a method's do.
            var identifier = context.Node is LocalFunctionStatementSyntax localFunction
                ? localFunction.Identifier
                : ((MethodDeclarationSyntax)context.Node).Identifier;
            var method = context.SemanticModel.GetDeclaredSymbol(context.Node, context.CancellationToken) as IMethodSymbol;
            if (method is null || !method.IsAsync || !method.ReturnsVoid)
            {
                return;
            }

            if (IsEventHandlerSignature(method, eventArgsType))
            {
                return;
            }

            context.ReportDiagnostic(UpaDiagnostics.Create(
                s_asyncVoidRule,
                identifier.GetLocation(),
                method.Name,
                advice));
        }

        /// <summary>
        /// An async lambda (or anonymous method) is async void whenever the delegate it becomes
        /// returns void - <c>AddListener(async () =&gt; await LoadAsync())</c>,
        /// <c>list.ForEach(async x =&gt; ...)</c>. Where the delegate returns a task the lambda
        /// does too and the caller can observe it, so only the void-returning conversions
        /// report.
        /// </summary>
        /// <remarks>
        /// <c>AddListener(async () =&gt; ...)</c> is the most common way Unity UI code is
        /// written, which is an argument for silence only if the shape were harmless, and it
        /// is not: an exception thrown after the first await reaches no caller. The rule is off
        /// by default and on in the UniTask stack, where UniTask.UnityAction is the fix that
        /// keeps the one-liner.
        /// </remarks>
        private static void AnalyzeAsyncLambda(
            OperationAnalysisContext context,
            INamedTypeSymbol? eventArgsType,
            LocalizableString advice)
        {
            var function = (IAnonymousFunctionOperation)context.Operation;
            var symbol = function.Symbol;
            if (!symbol.IsAsync || !symbol.ReturnsVoid)
            {
                return;
            }

            if (IsEventHandlerSignature(symbol, eventArgsType))
            {
                return;
            }

            var delegateType = (function.Parent as IDelegateCreationOperation)?.Type;
            var delegateName = delegateType?.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat) ?? "delegate";

            context.ReportDiagnostic(UpaDiagnostics.Create(
                s_asyncLambdaRule,
                Location.Create(function.Syntax.SyntaxTree, GetHeaderSpan(function.Syntax)),
                delegateName,
                advice));
        }

        // `async () =>` / `async delegate` rather than the whole lambda: the body can run to
        // dozens of lines, and the modifier is what the diagnostic is about.
        private static TextSpan GetHeaderSpan(SyntaxNode syntax)
        {
            switch (syntax)
            {
                case LambdaExpressionSyntax lambda:
                    return TextSpan.FromBounds(lambda.SpanStart, lambda.ArrowToken.Span.End);
                case AnonymousMethodExpressionSyntax anonymousMethod:
                    return TextSpan.FromBounds(anonymousMethod.SpanStart, anonymousMethod.DelegateKeyword.Span.End);
                default:
                    return syntax.Span;
            }
        }

        // The established .NET convention (object sender, EventArgs-derived e) is exempt.
        private static bool IsEventHandlerSignature(IMethodSymbol method, INamedTypeSymbol? eventArgsType)
        {
            if (eventArgsType is null || method.Parameters.Length != 2)
            {
                return false;
            }

            if (method.Parameters[0].Type.SpecialType != SpecialType.System_Object)
            {
                return false;
            }

            return TypeHierarchy.DerivesFrom(method.Parameters[1].Type, eventArgsType);
        }

        private static void AnalyzeInvocation(
            OperationAnalysisContext context,
            ImmutableArray<INamedTypeSymbol> taskLikeTypes,
            LocalizableString advice)
        {
            var invocation = (IInvocationOperation)context.Operation;

            // `obj?.DoAsync();` discards the task just as surely as `obj.DoAsync();` - the
            // conditional access only decides whether there is one. Climb out of it, but
            // only from the WhenNotNull side: `GetTask()?.ToString()` uses the task as a
            // receiver, which is a different statement about it.
            IOperation statementRoot = invocation;
            while (statementRoot.Parent is IConditionalAccessOperation conditional &&
                conditional.WhenNotNull == statementRoot)
            {
                statementRoot = conditional;
            }

            // Only bare expression statements discard the result. Awaits, assignments,
            // returns, arguments, `_ =` discards, and .Forget() receivers all have a
            // different parent operation and fall out here.
            if (!(statementRoot.Parent is IExpressionStatementOperation))
            {
                return;
            }

            var returnDefinition = (invocation.TargetMethod.ReturnType as INamedTypeSymbol)?.OriginalDefinition;
            if (returnDefinition is null)
            {
                return;
            }

            var isTaskLike = false;
            foreach (var taskLikeType in taskLikeTypes)
            {
                if (SymbolEqualityComparer.Default.Equals(returnDefinition, taskLikeType))
                {
                    isTaskLike = true;
                    break;
                }
            }

            if (!isTaskLike)
            {
                return;
            }

            context.ReportDiagnostic(UpaDiagnostics.Create(
                s_fireAndForgetRule,
                statementRoot.Syntax.GetLocation(),
                invocation.TargetMethod.Name,
                advice));
        }
    }
}
