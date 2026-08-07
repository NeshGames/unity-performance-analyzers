using System;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace UnityPerformanceAnalyzers
{
    /// <summary>
    /// UPA2032: Reports string tween IDs — a <c>string</c> argument to <c>SetId(...)</c> or to
    /// a <c>DOTween</c> static filtered operation (<c>Kill</c>, <c>Play</c>, ...). Per DOTween's
    /// own guidance, ID filtering is the fast path but string IDs are the slowest of the fast
    /// options; int IDs (or filtering by target reference) are faster. Info severity.
    /// Registered only when the compilation references the DOTween assembly.
    /// </summary>
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class UPA2032StringTweenIdAnalyzer : DiagnosticAnalyzer
    {
        /// <summary>The diagnostic ID reported by this analyzer.</summary>
        public const string DiagnosticId = "UPA2032";

        private static readonly DiagnosticDescriptor Rule = new DiagnosticDescriptor(
            DiagnosticId,
            new LocalizableResourceString(Strings.UPA2032Title, Strings.ResourceManager, typeof(Strings)),
            new LocalizableResourceString(Strings.UPA2032MessageFormat, Strings.ResourceManager, typeof(Strings)),
            DiagnosticCategories.Ecosystem,
            DiagnosticSeverity.Info,
            isEnabledByDefault: false,
            description: new LocalizableResourceString(Strings.UPA2032Description, Strings.ResourceManager, typeof(Strings)),
            helpLinkUri: "https://github.com/NeshGames/unity-performance-analyzers/blob/main/docs/rules/UPA2032.md");

        private static readonly ImmutableArray<DiagnosticDescriptor> s_supportedDiagnostics =
            ImmutableArray.Create(Rule);

        private static readonly ImmutableHashSet<string> s_filteredOperationNames = ImmutableHashSet.Create(
            StringComparer.Ordinal,
            "Kill",
            "Play",
            "Pause",
            "Restart",
            "Rewind",
            "Complete",
            "Flip",
            "Goto",
            "TogglePause",
            "IsTweening");

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
                if (!profile.HasDOTween)
                {
                    return;
                }

                var tweenType = ctx.Compilation.GetTypeByMetadataName("DG.Tweening.Tween");
                var doTweenType = ctx.Compilation.GetTypeByMetadataName("DG.Tweening.DOTween");
                if (tweenType is null && doTweenType is null)
                {
                    return;
                }

                ctx.RegisterOperationAction(
                    opCtx => AnalyzeInvocation(opCtx, tweenType, doTweenType),
                    OperationKind.Invocation);
            });
        }

        private static void AnalyzeInvocation(
            OperationAnalysisContext context,
            INamedTypeSymbol? tweenType,
            INamedTypeSymbol? doTweenType)
        {
            var invocation = (IInvocationOperation)context.Operation;
            var method = invocation.TargetMethod;

            var isSetId = method.Name == "SetId" &&
                tweenType is object &&
                ReturnsTween(method.ReturnType, tweenType);
            var isFilteredOperation = s_filteredOperationNames.Contains(method.Name) &&
                doTweenType is object &&
                SymbolEqualityComparer.Default.Equals(method.ContainingType, doTweenType);

            if (!isSetId && !isFilteredOperation)
            {
                return;
            }

            if (!HasStringIdArgument(invocation))
            {
                return;
            }

            context.ReportDiagnostic(UpaDiagnostics.Create(
                Rule,
                invocation.Syntax.GetLocation()));
        }

        private static bool HasStringIdArgument(IInvocationOperation invocation)
        {
            foreach (var argument in invocation.Arguments)
            {
                // The receiver of a reduced extension method is not an ID argument.
                if (argument.Parameter?.Ordinal == 0 &&
                    invocation.TargetMethod.IsExtensionMethod &&
                    invocation.Instance is null)
                {
                    continue;
                }

                var value = argument.Value;
                while (value is IConversionOperation conversion)
                {
                    value = conversion.Operand;
                }

                if (value.Type?.SpecialType == SpecialType.System_String)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool ReturnsTween(ITypeSymbol? type, INamedTypeSymbol tweenType)
        {
            if (type is ITypeParameterSymbol typeParameter)
            {
                foreach (var constraint in typeParameter.ConstraintTypes)
                {
                    if (ReturnsTween(constraint, tweenType))
                    {
                        return true;
                    }
                }

                return false;
            }

            for (var current = type; current is object; current = current.BaseType)
            {
                if (SymbolEqualityComparer.Default.Equals(current.OriginalDefinition, tweenType))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
