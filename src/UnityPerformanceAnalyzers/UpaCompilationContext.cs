using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace UnityPerformanceAnalyzers
{
    /// <summary>
    /// What a rule needs to know about the compilation it is about to analyze, resolved once
    /// per compilation start and handed to <see cref="UpaAnalyzer.InitializeCore"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Deliberately <em>not</em> shared between analyzers. Roslyn hands one analyzer instance
    /// to every compilation it serves, so anything cached on the instance leaks across
    /// compilations, and a cache keyed on the compilation alone would answer with the settings
    /// of a previous run once the options change — output that is indistinguishable from the
    /// right answer. Each analyzer resolves its own; the duplicated work is one pass over the
    /// referenced assembly list and one options parse, measured in microseconds.
    /// </para>
    /// <para>
    /// Everything here is per-callback state. The base class creates it inside the
    /// compilation-start callback and it is never reachable from an analyzer field.
    /// </para>
    /// </remarks>
    internal sealed class UpaCompilationContext
    {
        private readonly CompilationStartAnalysisContext _start;
        private readonly Lazy<UpaProfile> _profile;
        private readonly Lazy<HotPathDetector> _hotPath;

        // RS1012 wants every method taking a start context to register an action, because one
        // that registers nothing is an analyzer that never runs. This one hands the context to
        // the rule, which registers through the delegating methods below.
        [SuppressMessage(
            "MicrosoftCodeAnalysisCorrectness",
            "RS1012:Start action has no registered non-end actions",
            Justification = "Registration is delegated to the rule through this type.")]
        internal UpaCompilationContext(CompilationStartAnalysisContext start)
        {
            _start = start;

            // Lazy so a rule that never asks does not pay, and thread-safe because nothing
            // promises the registered actions only touch these from the start callback.
            _profile = new Lazy<UpaProfile>(
                () => UpaProfile.Resolve(start.Compilation, start.Options),
                LazyThreadSafetyMode.ExecutionAndPublication);
            _hotPath = new Lazy<HotPathDetector>(
                () => HotPathDetector.Create(start.Compilation, start.Options),
                LazyThreadSafetyMode.ExecutionAndPublication);
        }

        public Compilation Compilation => _start.Compilation;

        public AnalyzerOptions Options => _start.Options;

        public CancellationToken CancellationToken => _start.CancellationToken;

        /// <summary>Which of the supported packages this assembly references, and whether it
        /// is built for WebGL.</summary>
        public UpaProfile Profile => _profile.Value;

        /// <summary>Which methods count as per-frame work here.</summary>
        public HotPathDetector HotPath => _hotPath.Value;

        /// <summary>Whether this assembly is editor-only, decided by its name.</summary>
        public bool IsEditorAssembly => UpaProfile.IsEditorAssembly(_start.Compilation);

        /// <summary>The type with this metadata name, or null when it is not referenced. A
        /// rule that cannot resolve the types it is about has nothing to say and should
        /// register nothing.</summary>
        public INamedTypeSymbol? Type(string metadataName)
            => _start.Compilation.GetTypeByMetadataName(metadataName);

        public void RegisterOperationAction(Action<OperationAnalysisContext> action, params OperationKind[] operationKinds)
            => _start.RegisterOperationAction(action, operationKinds);

        public void RegisterSyntaxNodeAction(Action<SyntaxNodeAnalysisContext> action, params SyntaxKind[] syntaxKinds)
            => _start.RegisterSyntaxNodeAction(action, syntaxKinds);

        public void RegisterSymbolAction(Action<SymbolAnalysisContext> action, params SymbolKind[] symbolKinds)
            => _start.RegisterSymbolAction(action, symbolKinds);

        public void RegisterCompilationEndAction(Action<CompilationAnalysisContext> action)
            => _start.RegisterCompilationEndAction(action);
    }
}
