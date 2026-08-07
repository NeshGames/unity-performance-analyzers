using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace UnityPerformanceAnalyzers
{
    /// <summary>
    /// UPA1000: Reports concrete, non-sealed classes that have no derived types in the
    /// compilation. Sealing enables devirtualization in the JIT and IL2CPP. Classes declaring
    /// their own virtual (non-override) members are excluded — sealing them is a compile error
    /// (CS0549), and an unused extension point may be intentional. Analysis is per-compilation;
    /// external derivation of public types is a documented limitation.
    /// </summary>
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class UPA1000LeafClassNotSealedAnalyzer : DiagnosticAnalyzer
    {
        /// <summary>The diagnostic ID reported by this analyzer.</summary>
        public const string DiagnosticId = "UPA1000";

        private static readonly DiagnosticDescriptor Rule = new DiagnosticDescriptor(
            DiagnosticId,
            new LocalizableResourceString(Strings.UPA1000Title, Strings.ResourceManager, typeof(Strings)),
            new LocalizableResourceString(Strings.UPA1000MessageFormat, Strings.ResourceManager, typeof(Strings)),
            DiagnosticCategories.Correctness,
            DiagnosticSeverity.Warning,
            isEnabledByDefault: false,
            description: new LocalizableResourceString(Strings.UPA1000Description, Strings.ResourceManager, typeof(Strings)),
            helpLinkUri: "https://github.com/NeshGames/unity-performance-analyzers/blob/main/docs/rules/UPA1000.md",
            customTags: "CompilationEnd");

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
                // Per-compilation, thread-safe collections (analyzers run concurrently;
                // static mutable state is forbidden — CLAUDE.md 3.2).
                var candidates = new ConcurrentDictionary<INamedTypeSymbol, Location>(SymbolEqualityComparer.Default);
                var derivedFromTypes = new ConcurrentDictionary<INamedTypeSymbol, byte>(SymbolEqualityComparer.Default);

                ctx.RegisterSymbolAction(
                    symbolCtx => CollectType(symbolCtx, candidates, derivedFromTypes),
                    SymbolKind.NamedType);

                ctx.RegisterCompilationEndAction(endCtx =>
                {
                    foreach (var candidate in candidates)
                    {
                        if (!derivedFromTypes.ContainsKey(candidate.Key.OriginalDefinition))
                        {
                            endCtx.ReportDiagnostic(UpaDiagnostics.Create(
                                Rule,
                                candidate.Value,
                                candidate.Key.Name));
                        }
                    }
                });
            });
        }

        private static void CollectType(
            SymbolAnalysisContext context,
            ConcurrentDictionary<INamedTypeSymbol, Location> candidates,
            ConcurrentDictionary<INamedTypeSymbol, byte> derivedFromTypes)
        {
            var type = (INamedTypeSymbol)context.Symbol;

            if (type.BaseType is INamedTypeSymbol baseType)
            {
                derivedFromTypes.TryAdd(baseType.OriginalDefinition, 0);
            }

            if (type.TypeKind != TypeKind.Class ||
                type.IsAbstract ||
                type.IsStatic ||
                type.IsSealed ||
                type.IsImplicitlyDeclared)
            {
                return;
            }

            if (DeclaresVirtualMember(type))
            {
                return;
            }

            var location = type.Locations.FirstOrDefault();
            if (location is object)
            {
                candidates.TryAdd(type, location);
            }
        }

        private static bool DeclaresVirtualMember(INamedTypeSymbol type)
        {
            foreach (var member in type.GetMembers())
            {
                if (member.IsVirtual)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
