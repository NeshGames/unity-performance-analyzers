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
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class UPA2010AsyncTaskReturnAnalyzer : DiagnosticAnalyzer
    {
        /// <summary>The diagnostic ID reported by this analyzer.</summary>
        public const string DiagnosticId = "UPA2010";

        private static readonly DiagnosticDescriptor Rule = new DiagnosticDescriptor(
            DiagnosticId,
            new LocalizableResourceString(Strings.UPA2010Title, Strings.ResourceManager, typeof(Strings)),
            new LocalizableResourceString(Strings.UPA2010MessageFormat, Strings.ResourceManager, typeof(Strings)),
            DiagnosticCategories.Ecosystem,
            DiagnosticSeverity.Warning,
            isEnabledByDefault: false,
            description: new LocalizableResourceString(Strings.UPA2010Description, Strings.ResourceManager, typeof(Strings)),
            helpLinkUri: "https://github.com/NeshGames/unity-performance-analyzers/blob/main/docs/rules/UPA2010.md");

        private static readonly ImmutableArray<DiagnosticDescriptor> s_supportedDiagnostics =
            ImmutableArray.Create(Rule);

        /// <inheritdoc/>
        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => s_supportedDiagnostics;

        /// <inheritdoc/>
        public override void Initialize(AnalysisContext context)
        {
            context.EnableConcurrentExecution();
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.RegisterCompilationStartAction(ctx =>
            {
                var profile = UpaProfile.Resolve(ctx.Compilation, ctx.Options);
                if (!profile.HasUniTask)
                {
                    return;
                }

                var taskType = ctx.Compilation.GetTypeByMetadataName("System.Threading.Tasks.Task");
                var taskOfTType = ctx.Compilation.GetTypeByMetadataName("System.Threading.Tasks.Task`1");
                if (taskType is null && taskOfTType is null)
                {
                    return;
                }

                ctx.RegisterSyntaxNodeAction(
                    nodeCtx => AnalyzeDeclaration(nodeCtx, taskType, taskOfTType),
                    SyntaxKind.MethodDeclaration,
                    SyntaxKind.LocalFunctionStatement);
            });
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
