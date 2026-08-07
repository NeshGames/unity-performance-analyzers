using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace UnityPerformanceAnalyzers
{
    /// <summary>
    /// UPA3000–UPA3003: One analyzer, four descriptors, one banned-API walk. Reports method
    /// calls on banned types, construction of their instances, and static member accesses —
    /// registered only when the compilation defines UPA_TARGET_WEBGL
    /// (UpaProfile.RequiresWebGL). Scopes: UPA3000 threading (plus Task.Run/Task.Delay/
    /// TaskFactory.StartNew; lock/Interlocked/CancellationToken/await stay allowed),
    /// UPA3001 System.Net.Sockets, UPA3002 synchronous file IO (Path and MemoryStream stay
    /// allowed), UPA3003 System.Diagnostics.Process.
    /// </summary>
    [ConditionalRule("WebGL")]
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class UPA3000WebGlUnsupportedApiAnalyzer : UpaAnalyzer
    {
        /// <summary>The threading diagnostic ID reported by this analyzer.</summary>
        public const string ThreadingDiagnosticId = "UPA3000";

        /// <summary>The sockets diagnostic ID reported by this analyzer.</summary>
        public const string SocketsDiagnosticId = "UPA3001";

        /// <summary>The file IO diagnostic ID reported by this analyzer.</summary>
        public const string FileIoDiagnosticId = "UPA3002";

        /// <summary>The process diagnostic ID reported by this analyzer.</summary>
        public const string ProcessDiagnosticId = "UPA3003";

        private static readonly DiagnosticDescriptor s_threadingRule = UpaDescriptor.Create(
            ThreadingDiagnosticId,
            DiagnosticCategories.Platform,
            DiagnosticSeverity.Warning,
            isEnabledByDefault: false);

        private static readonly DiagnosticDescriptor s_socketsRule = UpaDescriptor.Create(
            SocketsDiagnosticId,
            DiagnosticCategories.Platform,
            DiagnosticSeverity.Warning,
            isEnabledByDefault: false);

        private static readonly DiagnosticDescriptor s_fileIoRule = UpaDescriptor.Create(
            FileIoDiagnosticId,
            DiagnosticCategories.Platform,
            DiagnosticSeverity.Warning,
            isEnabledByDefault: false);

        private static readonly DiagnosticDescriptor s_processRule = UpaDescriptor.Create(
            ProcessDiagnosticId,
            DiagnosticCategories.Platform,
            DiagnosticSeverity.Warning,
            isEnabledByDefault: false);

        private static readonly ImmutableArray<DiagnosticDescriptor> s_supportedDiagnostics =
            ImmutableArray.Create(s_threadingRule, s_socketsRule, s_fileIoRule, s_processRule);

        /// <inheritdoc/>
        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => s_supportedDiagnostics;

        /// <inheritdoc/>
        private protected override void InitializeCore(CompilationStartAnalysisContext ctx)
        {
            var profile = UpaProfile.Resolve(ctx.Compilation, ctx.Options);
            if (!profile.RequiresWebGL)
            {
                return;
            }

            var bannedApis = BannedApis.Create(ctx.Compilation);

            ctx.RegisterOperationAction(
                opCtx => AnalyzeOperation(opCtx, bannedApis),
                OperationKind.Invocation,
                OperationKind.ObjectCreation,
                OperationKind.PropertyReference,
                OperationKind.FieldReference);
        }

        private static void AnalyzeOperation(OperationAnalysisContext context, BannedApis bannedApis)
        {
            switch (context.Operation)
            {
                case IInvocationOperation invocation:
                {
                    var method = invocation.TargetMethod.ReducedFrom ?? invocation.TargetMethod;
                    var rule = bannedApis.ClassifyMember(method.ContainingType, method.Name);
                    if (rule is object)
                    {
                        Report(context, rule, GetInvocationLocation(invocation.Syntax),
                            $"{method.ContainingType.Name}.{method.Name}");
                    }

                    break;
                }

                case IObjectCreationOperation creation:
                {
                    if (creation.Type is INamedTypeSymbol createdType)
                    {
                        var rule = bannedApis.ClassifyConstruction(createdType);
                        if (rule is object)
                        {
                            Report(context, rule, creation.Syntax.GetLocation(), createdType.Name);
                        }
                    }

                    break;
                }

                case IMemberReferenceOperation memberReference:
                {
                    var member = memberReference.Member;
                    if (member.IsStatic && member.ContainingType is object)
                    {
                        var rule = bannedApis.ClassifyType(member.ContainingType);
                        if (rule is object)
                        {
                            Report(context, rule, memberReference.Syntax.GetLocation(),
                                $"{member.ContainingType.Name}.{member.Name}");
                        }
                    }

                    break;
                }
            }
        }

        private static void Report(
            OperationAnalysisContext context,
            DiagnosticDescriptor rule,
            Location location,
            string displayName)
        {
            context.ReportDiagnostic(UpaDiagnostics.Create(rule, location, displayName));
        }

        // Chained calls nest syntactically; anchor on the method name where possible.
        private static Location GetInvocationLocation(SyntaxNode syntax)
        {
            if (syntax is InvocationExpressionSyntax invocation &&
                invocation.Expression is MemberAccessExpressionSyntax memberAccess)
            {
                return memberAccess.Name.GetLocation();
            }

            return syntax.GetLocation();
        }

        private sealed class BannedApis
        {
            private readonly ImmutableHashSet<INamedTypeSymbol> _threadingTypes;
            private readonly INamedTypeSymbol? _taskType;
            private readonly INamedTypeSymbol? _taskFactoryType;
            private readonly INamedTypeSymbol? _fileType;
            private readonly INamedTypeSymbol? _directoryType;
            private readonly INamedTypeSymbol? _fileStreamType;
            private readonly INamedTypeSymbol? _processType;

            private BannedApis(
                ImmutableHashSet<INamedTypeSymbol> threadingTypes,
                INamedTypeSymbol? taskType,
                INamedTypeSymbol? taskFactoryType,
                INamedTypeSymbol? fileType,
                INamedTypeSymbol? directoryType,
                INamedTypeSymbol? fileStreamType,
                INamedTypeSymbol? processType)
            {
                _threadingTypes = threadingTypes;
                _taskType = taskType;
                _taskFactoryType = taskFactoryType;
                _fileType = fileType;
                _directoryType = directoryType;
                _fileStreamType = fileStreamType;
                _processType = processType;
            }

            public static BannedApis Create(Compilation compilation)
            {
                var threadingTypes = ImmutableHashSet.CreateBuilder<INamedTypeSymbol>(
                    (SymbolEqualityComparer)SymbolEqualityComparer.Default);
                foreach (var metadataName in new[]
                {
                    "System.Threading.Thread",
                    "System.Threading.ThreadPool",
                    "System.Threading.Timer",
                    "System.Threading.Mutex",
                    "System.Threading.Semaphore",
                    "System.Threading.SemaphoreSlim",
                    "System.Threading.Barrier",
                    "System.Threading.EventWaitHandle",
                    "System.Threading.Tasks.Parallel",
                })
                {
                    var type = compilation.GetTypeByMetadataName(metadataName);
                    if (type is object)
                    {
                        threadingTypes.Add(type);
                    }
                }

                return new BannedApis(
                    threadingTypes.ToImmutable(),
                    compilation.GetTypeByMetadataName("System.Threading.Tasks.Task"),
                    compilation.GetTypeByMetadataName("System.Threading.Tasks.TaskFactory"),
                    compilation.GetTypeByMetadataName("System.IO.File"),
                    compilation.GetTypeByMetadataName("System.IO.Directory"),
                    compilation.GetTypeByMetadataName("System.IO.FileStream"),
                    compilation.GetTypeByMetadataName("System.Diagnostics.Process"));
            }

            /// <summary>Type-level bans, applicable to any usage shape.</summary>
            public DiagnosticDescriptor? ClassifyType(INamedTypeSymbol type)
            {
                var definition = type.OriginalDefinition;

                if (IsInSocketsNamespace(definition))
                {
                    return s_socketsRule;
                }

                if (_processType is object && SymbolEqualityComparer.Default.Equals(definition, _processType))
                {
                    return s_processRule;
                }

                if ((_fileType is object && SymbolEqualityComparer.Default.Equals(definition, _fileType)) ||
                    (_directoryType is object && SymbolEqualityComparer.Default.Equals(definition, _directoryType)))
                {
                    return s_fileIoRule;
                }

                // Base walk catches derived handles such as AutoResetEvent : EventWaitHandle.
                for (var current = definition; current is object; current = current.BaseType)
                {
                    if (_threadingTypes.Contains(current))
                    {
                        return s_threadingRule;
                    }
                }

                return null;
            }

            /// <summary>Type-level bans plus the method-specific Task entries.</summary>
            public DiagnosticDescriptor? ClassifyMember(INamedTypeSymbol? containingType, string memberName)
            {
                if (containingType is null)
                {
                    return null;
                }

                var typeRule = ClassifyType(containingType);
                if (typeRule is object)
                {
                    return typeRule;
                }

                var definition = containingType.OriginalDefinition;
                if (_taskType is object && SymbolEqualityComparer.Default.Equals(definition, _taskType) &&
                    (memberName == "Run" || memberName == "Delay"))
                {
                    return s_threadingRule;
                }

                if (_taskFactoryType is object && SymbolEqualityComparer.Default.Equals(definition, _taskFactoryType) &&
                    memberName == "StartNew")
                {
                    return s_threadingRule;
                }

                return null;
            }

            /// <summary>Type-level bans plus FileStream, which is banned at construction only.</summary>
            public DiagnosticDescriptor? ClassifyConstruction(INamedTypeSymbol createdType)
            {
                var typeRule = ClassifyType(createdType);
                if (typeRule is object)
                {
                    return typeRule;
                }

                return _fileStreamType is object &&
                       SymbolEqualityComparer.Default.Equals(createdType.OriginalDefinition, _fileStreamType)
                    ? s_fileIoRule
                    : null;
            }

            private static bool IsInSocketsNamespace(INamedTypeSymbol type)
            {
                var ns = type.ContainingNamespace;
                return ns is object && ns.Name == "Sockets" &&
                       ns.ContainingNamespace is object && ns.ContainingNamespace.Name == "Net" &&
                       ns.ContainingNamespace.ContainingNamespace is object &&
                       ns.ContainingNamespace.ContainingNamespace.Name == "System" &&
                       ns.ContainingNamespace.ContainingNamespace.ContainingNamespace is object &&
                       ns.ContainingNamespace.ContainingNamespace.ContainingNamespace.IsGlobalNamespace;
            }
        }
    }
}
