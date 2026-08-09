using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace UnityPerformanceAnalyzers
{
    /// <summary>
    /// UPA2010: Reports user-authored async method declarations (including local functions)
    /// that return Task or Task&lt;T&gt;. Registered only when the compilation references
    /// UniTask — suggesting UniTask where it cannot be adopted is worse
    /// than staying silent. Overrides, interface implementations, and async lambdas are
    /// excluded: their signatures cannot be changed unilaterally (docs/rules/UPA2010.md).
    /// </summary>
    [ConditionalRule("UniTask")]
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class UPA2010AsyncTaskReturnAnalyzer : UpaAnalyzer
    {
        /// <summary>The diagnostic ID reported by this analyzer.</summary>
        public const string DiagnosticId = "UPA2010";

        private static readonly DiagnosticDescriptor Rule = UpaDescriptor.Create(
            DiagnosticId,
            DiagnosticCategories.Ecosystem,
            DiagnosticSeverity.Warning,
            isEnabledByDefault: false);

        private static readonly ImmutableArray<DiagnosticDescriptor> s_supportedDiagnostics =
            ImmutableArray.Create(Rule);

        /// <inheritdoc/>
        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => s_supportedDiagnostics;

        /// <inheritdoc/>
        private protected override void InitializeCore(UpaCompilationContext ctx)
        {
            var profile = ctx.Profile;
            if (!profile.HasUniTask)
            {
                return;
            }

            var taskType = ctx.Type("System.Threading.Tasks.Task");
            var taskOfTType = ctx.Type("System.Threading.Tasks.Task`1");
            if (taskType is null && taskOfTType is null)
            {
                return;
            }

            ctx.RegisterSyntaxNodeAction(
                nodeCtx => AnalyzeDeclaration(nodeCtx, taskType, taskOfTType),
                SyntaxKind.MethodDeclaration,
                SyntaxKind.LocalFunctionStatement);
        }

        private static void AnalyzeDeclaration(
            SyntaxNodeAnalysisContext context,
            INamedTypeSymbol? taskType,
            INamedTypeSymbol? taskOfTType)
        {
            if (!(context.SemanticModel.GetDeclaredSymbol(context.Node, context.CancellationToken) is IMethodSymbol method))
            {
                return;
            }

            if (!method.IsAsync || !ReturnsTask(method, taskType, taskOfTType))
            {
                return;
            }

            // Signatures bound by a base class or an interface cannot be changed unilaterally.
            if (method.IsOverride || ImplementsInterfaceMember(method))
            {
                return;
            }

            var identifier = context.Node is MethodDeclarationSyntax methodDeclaration
                ? methodDeclaration.Identifier
                : ((LocalFunctionStatementSyntax)context.Node).Identifier;

            context.ReportDiagnostic(UpaDiagnostics.Create(
                Rule,
                identifier.GetLocation(),
                method.Name));
        }

        private static bool ReturnsTask(
            IMethodSymbol method,
            INamedTypeSymbol? taskType,
            INamedTypeSymbol? taskOfTType)
        {
            var definition = (method.ReturnType as INamedTypeSymbol)?.OriginalDefinition;
            if (definition is null)
            {
                return false;
            }

            return (taskType is object && SymbolEqualityComparer.Default.Equals(definition, taskType)) ||
                   (taskOfTType is object && SymbolEqualityComparer.Default.Equals(definition, taskOfTType));
        }

        private static bool ImplementsInterfaceMember(IMethodSymbol method)
        {
            if (!method.ExplicitInterfaceImplementations.IsEmpty)
            {
                return true;
            }

            var containingType = method.ContainingType;
            if (containingType is null)
            {
                return false;
            }

            foreach (var interfaceType in containingType.AllInterfaces)
            {
                foreach (var member in interfaceType.GetMembers(method.Name))
                {
                    if (member is IMethodSymbol interfaceMethod &&
                        SymbolEqualityComparer.Default.Equals(
                            containingType.FindImplementationForInterfaceMember(interfaceMethod), method))
                    {
                        return true;
                    }
                }
            }

            return false;
        }
    }
}
