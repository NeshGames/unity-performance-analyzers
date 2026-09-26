using System;
using System.Collections.Immutable;
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
    /// referenced assembly list and a lookup of the options file. The parse behind that lookup
    /// is shared, keyed on the file's immutable text (see <see cref="UpaOptions.Resolve"/>),
    /// which is the one key that cannot answer with another run's settings.
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
        private readonly UpaClaimKind _claim;
        private readonly Lazy<UpaOptions> _settings;
        private readonly Lazy<EditorOnlyMethods> _editorOnly;

        // RS1012 wants every method taking a start context to register an action, because one
        // that registers nothing is an analyzer that never runs. This one hands the context to
        // the rule, which registers through the delegating methods below.
        [SuppressMessage(
            "MicrosoftCodeAnalysisCorrectness",
            "RS1012:Start action has no registered non-end actions",
            Justification = "Registration is delegated to the rule through this type.")]
        internal UpaCompilationContext(CompilationStartAnalysisContext start, UpaClaimKind claim)
        {
            _start = start;
            _claim = claim;

            // Lazy so a rule that never asks does not pay, and thread-safe because nothing
            // promises the registered actions only touch these from the start callback.
            _editorOnly = new Lazy<EditorOnlyMethods>(
                () => EditorOnlyMethods.Create(start.Compilation),
                LazyThreadSafetyMode.ExecutionAndPublication);
            _settings = new Lazy<UpaOptions>(
                () => UpaOptions.Resolve(start.Options),
                LazyThreadSafetyMode.ExecutionAndPublication);
            _profile = new Lazy<UpaProfile>(
                () => UpaProfile.Resolve(start.Compilation, start.Options),
                LazyThreadSafetyMode.ExecutionAndPublication);
            _hotPath = new Lazy<HotPathDetector>(
                () => HotPathDetector.Create(
                    start.Compilation, _settings.Value, start.Options.AnalyzerConfigOptionsProvider),
                LazyThreadSafetyMode.ExecutionAndPublication);
        }

        public Compilation Compilation => _start.Compilation;

        public AnalyzerOptions Options => _start.Options;

        public CancellationToken CancellationToken => _start.CancellationToken;

        /// <summary>The options file, found and parsed at most once for this analyzer and
        /// compilation. Look keys up through <see cref="GetBool"/> and <see cref="GetList"/>,
        /// which add the .editorconfig layer for the file being asked about.</summary>
        public UpaOptions Settings => _settings.Value;

        /// <summary>
        /// An option as it applies to <paramref name="tree"/>. Always per file: an
        /// .editorconfig section applies to the files it globs, and an answer read once from
        /// the compilation's first syntax tree gave every file whatever that one was
        /// configured with, depending on the order the host listed them in.
        /// </summary>
        public bool GetBool(string key, SyntaxTree tree, bool fallback)
            => Settings.GetBool(key, tree, _start.Options.AnalyzerConfigOptionsProvider, fallback);

        /// <summary>A comma-separated option as it applies to <paramref name="tree"/>; see
        /// <see cref="GetBool"/> for why per file.</summary>
        public ImmutableArray<string> GetList(string key, SyntaxTree tree, ImmutableArray<string> fallback)
            => Settings.GetList(key, tree, _start.Options.AnalyzerConfigOptionsProvider, fallback);

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
        {
            if (_claim != UpaClaimKind.PerFrameCost)
            {
                _start.RegisterOperationAction(action, operationKinds);
                return;
            }

            var editorOnly = _editorOnly;
            _start.RegisterOperationAction(
                ctx =>
                {
                    if (ctx.Operation.SemanticModel is { } semanticModel
                        && editorOnly.Value.Contains(ctx.Operation.Syntax, semanticModel, ctx.CancellationToken))
                    {
                        return;
                    }

                    action(ctx);
                },
                operationKinds);
        }

        public void RegisterSyntaxNodeAction(Action<SyntaxNodeAnalysisContext> action, params SyntaxKind[] syntaxKinds)
        {
            if (_claim != UpaClaimKind.PerFrameCost)
            {
                _start.RegisterSyntaxNodeAction(action, syntaxKinds);
                return;
            }

            var editorOnly = _editorOnly;
            _start.RegisterSyntaxNodeAction(
                ctx =>
                {
                    if (editorOnly.Value.Contains(ctx.Node, ctx.SemanticModel, ctx.CancellationToken))
                    {
                        return;
                    }

                    action(ctx);
                },
                syntaxKinds);
        }

        // Per-frame cost in an editor-only method is filtered here rather than in each rule.
        // Forty analyzers each remembering to ask is forty chances to forget, and forgetting
        // produces a rule that reports in code Unity strips - which looks exactly like a rule
        // working correctly.
        //
        // It is asked before the rule runs, and that is cheap because of how Contains is built:
        // a parent walk to the enclosing method and two syntactic tests, with the declared-symbol
        // lookup reached only for a method that carries attributes or has a message's name. A
        // report-time filter was tried and dropped - it saved that walk only by allocating a
        // wrapped context on every callback of twenty-odd analyzers, nearly all of which never
        // report, and it needed a Roslyn constructor later versions mark obsolete.

        public void RegisterSymbolAction(Action<SymbolAnalysisContext> action, params SymbolKind[] symbolKinds)
            => _start.RegisterSymbolAction(action, symbolKinds);

        public void RegisterCompilationEndAction(Action<CompilationAnalysisContext> action)
            => _start.RegisterCompilationEndAction(action);
    }
}
