using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace UnityPerformanceAnalyzers
{
    /// <summary>
    /// UPA2012: Reports two shapes under one rule ID — (A) async void method declarations,
    /// whose exceptions escape every caller, and (B) expression-statement invocations that
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

        private static readonly ImmutableArray<DiagnosticDescriptor> s_supportedDiagnostics =
            ImmutableArray.Create(s_asyncVoidRule, s_fireAndForgetRule);

        private static readonly LocalizableResourceString s_adviceAsyncVoidDefault =
            new LocalizableResourceString(Strings.UPA2012AdviceAsyncVoidDefault, Strings.ResourceManager, typeof(Strings));

        private static readonly LocalizableResourceString s_adviceAsyncVoidUniTask =
            new LocalizableResourceString(Strings.UPA2012AdviceAsyncVoidUniTask, Strings.ResourceManager, typeof(Strings));

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

            var eventArgsType = ctx.Type("System.EventArgs");
            ctx.RegisterSyntaxNodeAction(
                nodeCtx => AnalyzeAsyncVoid(nodeCtx, eventArgsType, adviceAsyncVoid),
                SyntaxKind.MethodDeclaration);

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
            var declaration = (MethodDeclarationSyntax)context.Node;
            var method = context.SemanticModel.GetDeclaredSymbol(declaration, context.CancellationToken);
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
                declaration.Identifier.GetLocation(),
                method.Name,
                advice));
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

            // Only bare expression statements discard the result. Awaits, assignments,
            // returns, arguments, `_ =` discards, and .Forget() receivers all have a
            // different parent operation and fall out here.
            if (!(invocation.Parent is IExpressionStatementOperation))
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
                invocation.Syntax.GetLocation(),
                invocation.TargetMethod.Name,
                advice));
        }
    }
}
