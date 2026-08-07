using System.Collections.Generic;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace UnityPerformanceAnalyzers
{
    /// <summary>
    /// UPA0018: Reports reads of a closed deny-list of Unity properties that allocate and
    /// return a fresh array on every access (<c>Input.touches</c>, <c>Animator.parameters</c>,
    /// <c>Renderer.sharedMaterials</c>, <c>Camera.allCameras</c>) on per-frame hot paths. Each
    /// entry has a documented non-allocating counterpart that the message names. Physics
    /// queries and Mesh array properties are deliberately excluded — other analyzers own them.
    /// </summary>
    [HotPathRule]
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class UPA0018AllocatingArrayApiAnalyzer : UpaAnalyzer
    {
        /// <summary>The diagnostic ID reported by this analyzer.</summary>
        public const string DiagnosticId = "UPA0018";

        private static readonly DiagnosticDescriptor Rule = UpaDescriptor.Create(
            DiagnosticId,
            DiagnosticCategories.Performance,
            DiagnosticSeverity.Warning,
            isEnabledByDefault: true);

        private static readonly ImmutableArray<DiagnosticDescriptor> s_supportedDiagnostics =
            ImmutableArray.Create(Rule);

        /// <summary>One deny-list entry: a property on a Unity type plus its replacement text.</summary>
        private readonly struct DeniedProperty
        {
            public DeniedProperty(INamedTypeSymbol containingType, string propertyName, string replacement)
            {
                ContainingType = containingType;
                PropertyName = propertyName;
                Replacement = replacement;
            }

            public INamedTypeSymbol ContainingType { get; }
            public string PropertyName { get; }
            public string Replacement { get; }
        }

        /// <inheritdoc/>
        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => s_supportedDiagnostics;

        /// <inheritdoc/>
        private protected override void InitializeCore(CompilationStartAnalysisContext ctx)
        {
            var denied = ResolveDenyList(ctx.Compilation);
            if (denied.IsEmpty)
            {
                return;
            }

            var hotPathDetector = HotPathDetector.Create(ctx.Compilation, ctx.Options);

            ctx.RegisterOperationAction(
                opCtx => AnalyzePropertyReference(opCtx, denied, hotPathDetector),
                OperationKind.PropertyReference);
        }

        private static ImmutableArray<DeniedProperty> ResolveDenyList(Compilation compilation)
        {
            var builder = ImmutableArray.CreateBuilder<DeniedProperty>();

            Add(builder, compilation, "UnityEngine.Input", "touches",
                "Input.touchCount with Input.GetTouch(i)");
            Add(builder, compilation, "UnityEngine.Animator", "parameters",
                "Animator.parameterCount with Animator.GetParameter(i)");
            Add(builder, compilation, "UnityEngine.Renderer", "sharedMaterials",
                "Renderer.GetSharedMaterials(List<Material>)");
            Add(builder, compilation, "UnityEngine.Camera", "allCameras",
                "Camera.GetAllCameras(Camera[])");

            return builder.ToImmutable();
        }

        private static void Add(
            ImmutableArray<DeniedProperty>.Builder builder,
            Compilation compilation,
            string metadataName,
            string propertyName,
            string replacement)
        {
            var type = compilation.GetTypeByMetadataName(metadataName);
            if (type is object)
            {
                builder.Add(new DeniedProperty(type, propertyName, replacement));
            }
        }

        private static void AnalyzePropertyReference(
            OperationAnalysisContext context,
            ImmutableArray<DeniedProperty> denied,
            HotPathDetector hotPathDetector)
        {
            var propertyReference = (IPropertyReferenceOperation)context.Operation;
            var property = propertyReference.Property;

            var entryIndex = -1;
            for (var i = 0; i < denied.Length; i++)
            {
                if (property.Name == denied[i].PropertyName &&
                    SymbolEqualityComparer.Default.Equals(property.ContainingType, denied[i].ContainingType))
                {
                    entryIndex = i;
                    break;
                }
            }

            if (entryIndex < 0)
            {
                return;
            }

            // Writing (e.g. renderer.sharedMaterials = ...) does not allocate; only reads do.
            if (propertyReference.Parent is ISimpleAssignmentOperation assignment &&
                ReferenceEquals(assignment.Target, propertyReference))
            {
                return;
            }

            if (hotPathDetector.IsOutsideHotPath(propertyReference, context.CancellationToken))
            {
                return;
            }

            var entry = denied[entryIndex];
            context.ReportDiagnostic(UpaDiagnostics.Create(
                Rule,
                propertyReference.Syntax.GetLocation(),
                entry.ContainingType.Name + "." + entry.PropertyName,
                entry.Replacement));
        }
    }
}
