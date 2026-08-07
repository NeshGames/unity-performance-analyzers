using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace UnityPerformanceAnalyzers
{
    /// <summary>
    /// UPA3004: Reports blocking waits on asynchronous operations — Addressables
    /// AsyncOperationHandle.WaitForCompletion, Task.Wait/WaitAll/WaitAny, Task&lt;T&gt;.Result
    /// and the GetAwaiter().GetResult() idiom on the Task/ValueTask awaiter family.
    /// Unity WebGL players are single-threaded, so these deadlock instead of merely stalling;
    /// registered only when the compilation defines UPA_TARGET_WEBGL. Each banned group is
    /// resolved independently, so the Addressables checks vanish when the package is absent.
    /// UniTask awaiters throw rather than block when incomplete and are deliberately not
    /// listed; completed-task Result access still reports (no flow analysis) and is meant
    /// to be suppressed locally (docs/rules/UPA3004.md).
    /// </summary>
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class UPA3004BlockingWaitAnalyzer : DiagnosticAnalyzer
    {
        /// <summary>The diagnostic ID reported by this analyzer.</summary>
        public const string DiagnosticId = "UPA3004";

        private static readonly DiagnosticDescriptor Rule = new DiagnosticDescriptor(
            DiagnosticId,
            new LocalizableResourceString(Strings.UPA3004Title, Strings.ResourceManager, typeof(Strings)),
            new LocalizableResourceString(Strings.UPA3004MessageFormat, Strings.ResourceManager, typeof(Strings)),
            DiagnosticCategories.Platform,
            DiagnosticSeverity.Warning,
            isEnabledByDefault: false,
            description: new LocalizableResourceString(Strings.UPA3004Description, Strings.ResourceManager, typeof(Strings)),
            helpLinkUri: "https://github.com/NeshGames/unity-performance-analyzers/blob/main/docs/rules/UPA3004.md");

        private static readonly ImmutableArray<DiagnosticDescriptor> s_supportedDiagnostics =
            ImmutableArray.Create(Rule);

        private static readonly ImmutableArray<string> s_awaiterTypeNames = ImmutableArray.Create(
            "System.Runtime.CompilerServices.TaskAwaiter",
            "System.Runtime.CompilerServices.TaskAwaiter`1",
            "System.Runtime.CompilerServices.ValueTaskAwaiter",
            "System.Runtime.CompilerServices.ValueTaskAwaiter`1",
            "System.Runtime.CompilerServices.ConfiguredTaskAwaitable+ConfiguredTaskAwaiter",
            "System.Runtime.CompilerServices.ConfiguredTaskAwaitable`1+ConfiguredTaskAwaiter",
            "System.Runtime.CompilerServices.ConfiguredValueTaskAwaitable+ConfiguredValueTaskAwaiter",
            "System.Runtime.CompilerServices.ConfiguredValueTaskAwaitable`1+ConfiguredValueTaskAwaiter");

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
                if (!profile.RequiresWebGL)
                {
                    return;
                }

                var bannedMembers = BannedMembers.Create(ctx.Compilation);
                if (bannedMembers.IsEmpty)
                {
                    return;
                }

                ctx.RegisterOperationAction(
                    opCtx => AnalyzeOperation(opCtx, bannedMembers),
                    OperationKind.Invocation,
                    OperationKind.PropertyReference);
            });
        }

        private static void AnalyzeOperation(OperationAnalysisContext context, BannedMembers bannedMembers)
        {
            switch (context.Operation)
            {
                case IInvocationOperation invocation:
                {
                    var method = invocation.TargetMethod.ReducedFrom ?? invocation.TargetMethod;
                    if (bannedMembers.IsBlockingMethod(method))
                    {
                        Report(context, invocation.Syntax, method.ContainingType.Name, method.Name);
                    }

                    break;
                }

                case IPropertyReferenceOperation propertyReference:
                {
                    if (bannedMembers.IsBlockingProperty(propertyReference.Property))
                    {
                        Report(context, propertyReference.Syntax, propertyReference.Property.ContainingType.Name, propertyReference.Property.Name);
                    }

                    break;
                }
            }
        }

        private static void Report(OperationAnalysisContext context, SyntaxNode syntax, string typeName, string memberName)
        {
            context.ReportDiagnostic(UpaDiagnostics.Create(
                Rule,
                GetReportLocation(syntax),
                $"{TrimGenericArity(typeName)}.{memberName}"));
        }

        // Chained expressions nest syntactically; the member name keeps each report readable.
        private static Location GetReportLocation(SyntaxNode syntax)
        {
            switch (syntax)
            {
                case InvocationExpressionSyntax invocation when invocation.Expression is MemberAccessExpressionSyntax memberAccess:
                    return memberAccess.Name.GetLocation();
                case MemberAccessExpressionSyntax memberAccess:
                    return memberAccess.Name.GetLocation();
                default:
                    return syntax.GetLocation();
            }
        }

        private static string TrimGenericArity(string typeName)
        {
            var backtick = typeName.IndexOf('`');
            return backtick < 0 ? typeName : typeName.Substring(0, backtick);
        }

        /// <summary>
        /// Banned-member lookup resolved once per compilation. Groups resolve independently:
        /// a project without Addressables simply has no WaitForCompletion entries while the
        /// Task and awaiter groups keep working, and vice versa.
        /// </summary>
        private sealed class BannedMembers
        {
            private readonly INamedTypeSymbol? _asyncOperationHandle;
            private readonly INamedTypeSymbol? _asyncOperationHandleGeneric;
            private readonly INamedTypeSymbol? _task;
            private readonly INamedTypeSymbol? _taskGeneric;
            private readonly ImmutableArray<INamedTypeSymbol> _awaiterTypes;

            private BannedMembers(
                INamedTypeSymbol? asyncOperationHandle,
                INamedTypeSymbol? asyncOperationHandleGeneric,
                INamedTypeSymbol? task,
                INamedTypeSymbol? taskGeneric,
                ImmutableArray<INamedTypeSymbol> awaiterTypes)
            {
                _asyncOperationHandle = asyncOperationHandle;
                _asyncOperationHandleGeneric = asyncOperationHandleGeneric;
                _task = task;
                _taskGeneric = taskGeneric;
                _awaiterTypes = awaiterTypes;
            }

            public bool IsEmpty =>
                _asyncOperationHandle is null &&
                _asyncOperationHandleGeneric is null &&
                _task is null &&
                _taskGeneric is null &&
                _awaiterTypes.IsEmpty;

            public static BannedMembers Create(Compilation compilation)
            {
                var awaiters = ImmutableArray.CreateBuilder<INamedTypeSymbol>();
                foreach (var metadataName in s_awaiterTypeNames)
                {
                    var type = compilation.GetTypeByMetadataName(metadataName);
                    if (type is object)
                    {
                        awaiters.Add(type);
                    }
                }

                return new BannedMembers(
                    compilation.GetTypeByMetadataName("UnityEngine.ResourceManagement.AsyncOperations.AsyncOperationHandle"),
                    compilation.GetTypeByMetadataName("UnityEngine.ResourceManagement.AsyncOperations.AsyncOperationHandle`1"),
                    compilation.GetTypeByMetadataName("System.Threading.Tasks.Task"),
                    compilation.GetTypeByMetadataName("System.Threading.Tasks.Task`1"),
                    awaiters.ToImmutable());
            }

            public bool IsBlockingMethod(IMethodSymbol method)
            {
                var containingType = method.ContainingType?.OriginalDefinition;
                if (containingType is null)
                {
                    return false;
                }

                switch (method.Name)
                {
                    case "WaitForCompletion":
                        return Matches(containingType, _asyncOperationHandle) ||
                               Matches(containingType, _asyncOperationHandleGeneric);
                    case "Wait":
                        return Matches(containingType, _task) || Matches(containingType, _taskGeneric);
                    case "WaitAll":
                    case "WaitAny":
                        return method.IsStatic && Matches(containingType, _task);
                    case "GetResult":
                        foreach (var awaiterType in _awaiterTypes)
                        {
                            if (Matches(containingType, awaiterType))
                            {
                                return true;
                            }
                        }

                        return false;
                    default:
                        return false;
                }
            }

            public bool IsBlockingProperty(IPropertySymbol property)
            {
                return property.Name == "Result" &&
                       Matches(property.ContainingType?.OriginalDefinition, _taskGeneric);
            }

            private static bool Matches(ITypeSymbol? candidate, INamedTypeSymbol? banned)
            {
                return banned is object &&
                       candidate is object &&
                       SymbolEqualityComparer.Default.Equals(candidate, banned);
            }
        }
    }
}
