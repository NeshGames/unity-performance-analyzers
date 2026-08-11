using System.Collections.Generic;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace UnityPerformanceAnalyzers
{
    /// <summary>
    /// UPA0018: Reports a closed deny-list of UnityEngine members that allocate and return a
    /// fresh array on every call, on per-frame hot paths. Properties: <c>Input.touches</c>,
    /// <c>Animator.parameters</c>, <c>Renderer.sharedMaterials</c>, <c>Camera.allCameras</c>.
    /// Methods: <c>Animator.GetCurrentAnimatorClipInfo(int)</c> and the pixel readers on
    /// <c>Texture2D</c>. Each entry has a documented non-allocating counterpart that the
    /// message names, and every entry plus its replacement was measured in the sandbox before
    /// being listed.
    ///
    /// The line between this rule and UPA0030 is what an entry belongs to, not when it was
    /// added: UnityEngine members are listed here, BCL members there.
    ///
    /// Physics queries, Mesh array properties, and the GetComponents overloads are
    /// deliberately excluded — other analyzers own them.
    /// </summary>
    [HotPathRule]
    [UpaClaim(UpaClaimKind.PerFrameCost)]
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

        /// <summary>
        /// One deny-list entry: a member on a Unity type plus its replacement text. The same
        /// shape covers properties and methods; <see cref="IsMethod"/> says which operation
        /// kind carries it.
        /// </summary>
        private readonly struct DeniedMember
        {
            public DeniedMember(INamedTypeSymbol containingType, string memberName, string replacement, bool isMethod)
            {
                ContainingType = containingType;
                MemberName = memberName;
                Replacement = replacement;
                IsMethod = isMethod;
            }

            public INamedTypeSymbol ContainingType { get; }
            public string MemberName { get; }
            public string Replacement { get; }
            public bool IsMethod { get; }
        }

        /// <inheritdoc/>
        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => s_supportedDiagnostics;

        /// <inheritdoc/>
        private protected override void InitializeCore(UpaCompilationContext ctx)
        {
            var denied = ResolveDenyList(ctx.Compilation);
            if (denied.IsEmpty)
            {
                return;
            }

            var hotPathDetector = ctx.HotPath;

            ctx.RegisterOperationAction(
                opCtx => AnalyzePropertyReference(opCtx, denied, hotPathDetector),
                OperationKind.PropertyReference);

            ctx.RegisterOperationAction(
                opCtx => AnalyzeInvocation(opCtx, denied, hotPathDetector),
                OperationKind.Invocation);
        }

        private static ImmutableArray<DeniedMember> ResolveDenyList(Compilation compilation)
        {
            var builder = ImmutableArray.CreateBuilder<DeniedMember>();

            AddProperty(builder, compilation, "UnityEngine.Input", "touches",
                "Input.touchCount with Input.GetTouch(i)");
            AddProperty(builder, compilation, "UnityEngine.Animator", "parameters",
                "Animator.parameterCount with Animator.GetParameter(i)");
            AddProperty(builder, compilation, "UnityEngine.Renderer", "sharedMaterials",
                "Renderer.GetSharedMaterials(List<Material>)");
            AddProperty(builder, compilation, "UnityEngine.Camera", "allCameras",
                "Camera.GetAllCameras(Camera[])");

            AddMethod(builder, compilation, "UnityEngine.Animator", "GetCurrentAnimatorClipInfo",
                "the GetCurrentAnimatorClipInfo(int, List<AnimatorClipInfo>) overload with a cached list");
            AddMethod(builder, compilation, "UnityEngine.Texture2D", "GetPixels",
                "the generic GetRawTextureData<T>() overload, which returns a NativeArray view");
            AddMethod(builder, compilation, "UnityEngine.Texture2D", "GetPixels32",
                "the generic GetRawTextureData<T>() overload, which returns a NativeArray view");
            AddMethod(builder, compilation, "UnityEngine.Texture2D", "GetRawTextureData",
                "the generic GetRawTextureData<T>() overload, which returns a NativeArray view");

            return builder.ToImmutable();
        }

        private static void AddProperty(
            ImmutableArray<DeniedMember>.Builder builder,
            Compilation compilation,
            string metadataName,
            string propertyName,
            string replacement)
        {
            var type = compilation.GetTypeByMetadataName(metadataName);
            if (type is object)
            {
                builder.Add(new DeniedMember(type, propertyName, replacement, isMethod: false));
            }
        }

        private static void AddMethod(
            ImmutableArray<DeniedMember>.Builder builder,
            Compilation compilation,
            string metadataName,
            string methodName,
            string replacement)
        {
            var type = compilation.GetTypeByMetadataName(metadataName);
            if (type is object)
            {
                builder.Add(new DeniedMember(type, methodName, replacement, isMethod: true));
            }
        }

        private static void AnalyzePropertyReference(
            OperationAnalysisContext context,
            ImmutableArray<DeniedMember> denied,
            HotPathDetector hotPathDetector)
        {
            var propertyReference = (IPropertyReferenceOperation)context.Operation;
            var property = propertyReference.Property;

            var entryIndex = FindEntry(denied, property, isMethod: false);
            if (entryIndex < 0)
            {
                return;
            }

            // Writing (e.g. renderer.sharedMaterials = ...) does not allocate; only reads do.
            if (OperationFacts.IsOverwritten(propertyReference))
            {
                return;
            }

            Report(context, denied[entryIndex], propertyReference, hotPathDetector);
        }

        private static void AnalyzeInvocation(
            OperationAnalysisContext context,
            ImmutableArray<DeniedMember> denied,
            HotPathDetector hotPathDetector)
        {
            var invocation = (IInvocationOperation)context.Operation;
            var method = invocation.TargetMethod;

            // The non-allocating replacements share their names with the listed members:
            // GetCurrentAnimatorClipInfo has a List overload and GetRawTextureData has a
            // generic one. Reporting either would tell the user to switch to the call they
            // already wrote, so both are filtered out before the name lookup means anything.
            if (method.IsGenericMethod || ReturnsThroughCollectionParameter(method))
            {
                return;
            }

            var entryIndex = FindEntry(denied, method, isMethod: true);
            if (entryIndex < 0)
            {
                return;
            }

            Report(context, denied[entryIndex], invocation, hotPathDetector);
        }

        /// <summary>
        /// True when the method hands its results back through a caller-supplied collection
        /// rather than a fresh array — the shape every non-allocating counterpart here uses.
        /// </summary>
        private static bool ReturnsThroughCollectionParameter(IMethodSymbol method)
        {
            if (!method.ReturnsVoid)
            {
                return false;
            }

            foreach (var parameter in method.Parameters)
            {
                if (parameter.Type is INamedTypeSymbol named && named.IsGenericType)
                {
                    return true;
                }
            }

            return false;
        }

        private static int FindEntry(ImmutableArray<DeniedMember> denied, ISymbol member, bool isMethod)
        {
            for (var i = 0; i < denied.Length; i++)
            {
                if (denied[i].IsMethod == isMethod &&
                    member.Name == denied[i].MemberName &&
                    SymbolEqualityComparer.Default.Equals(member.ContainingType, denied[i].ContainingType))
                {
                    return i;
                }
            }

            return -1;
        }

        private static void Report(
            OperationAnalysisContext context,
            DeniedMember entry,
            IOperation operation,
            HotPathDetector hotPathDetector)
        {
            if (hotPathDetector.IsOutsideHotPath(operation, context.CancellationToken))
            {
                return;
            }

            context.ReportDiagnostic(UpaDiagnostics.Create(
                Rule,
                operation.Syntax.GetLocation(),
                entry.ContainingType.Name + "." + entry.MemberName,
                entry.Replacement));
        }
    }
}
