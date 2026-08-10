using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

namespace UnityPerformanceAnalyzers.Cli;

/// <summary>Everything the analyzers need for one run.</summary>
internal sealed record AnalysisInput(CSharpCompilation Compilation, AnalyzerOptions Options);

/// <summary>
/// Assembles the compilation the analyzers see: the given sources, a BCL reference set,
/// the Unity surface (bundled stubs or a real Unity managed directory), and one empty
/// assembly per --reference so package detection matches a real project.
/// </summary>
internal static class CompilationBuilder
{
    // Unity 2022.3 and Unity 6 both compile at C# 9; pinning it keeps the CLI from
    // accepting syntax the real build would reject.
    private const LanguageVersion UnityLanguageVersion = LanguageVersion.CSharp9;

    public static AnalysisInput Build(CliOptions options)
    {
        var parseOptions = new CSharpParseOptions(
            UnityLanguageVersion,
            preprocessorSymbols: options.Defines);

        var trees = options.Files
            .Select(file => CSharpSyntaxTree.ParseText(
                SourceText.From(File.ReadAllText(file)),
                parseOptions,
                path: file))
            .ToImmutableArray();

        var references = ImmutableArray.CreateBuilder<MetadataReference>();
        references.AddRange(RuntimeReferences());
        references.AddRange(UnityReferences(options.UnityDllDir));
        foreach (var reference in options.References)
        {
            // A path loads the real assembly, so code calling into that package resolves
            // and the package's rules see actual symbols. A bare name only makes the
            // package look present, which is all the activation checks need but leaves
            // its APIs unresolved.
            if (File.Exists(reference))
            {
                references.Add(MetadataReference.CreateFromFile(Path.GetFullPath(reference)));
                continue;
            }

            // A path that has gone missing is not a package name. Treating it as one is how
            // a generated argument file goes quiet after the project moves on: the fake
            // assembly satisfies the activation check, nothing from that package resolves,
            // and the rules that needed it simply stop reporting.
            if (LooksLikeAPath(reference))
            {
                throw new CliException(
                    $"Reference not found: {reference}. If the arguments were generated, "
                    + "regenerate them - the project has changed since.");
            }

            references.Add(FakeAssembly(reference));
        }

        var editorConfig = EditorConfigOptionsProvider.Create(options.EditorConfigPath, trees);

        // All severity resolution lives in the provider so that file-scoped .editorconfig
        // entries stay file-scoped; nothing goes into the compilation-wide map.
        var compilationOptions = new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
            .WithSyntaxTreeOptionsProvider(SeverityOptionsProvider.Create(
                editorConfig.SeveritiesByFile,
                LoadRuleset(options.RulesetPath),
                options.AllWarn));

        var compilation = CSharpCompilation.Create(
            options.AssemblyName,
            trees,
            references.ToImmutable(),
            compilationOptions);

        var additionalFiles = options.AdditionalFiles
            .Select(path => (AdditionalText)new AdditionalTextFile(path))
            .ToImmutableArray();

        var analyzerOptions = new AnalyzerOptions(additionalFiles, editorConfig.Provider);

        return new AnalysisInput(compilation, analyzerOptions);
    }

    /// <summary>
    /// A ruleset applies to the whole compilation, so it needs no file scoping. Both of
    /// its channels are kept: the per-rule entries and the IncludeAll blanket action.
    /// </summary>
    private static RulesetSeverities LoadRuleset(string? rulesetPath)
    {
        if (rulesetPath is null)
        {
            return RulesetSeverities.None;
        }

        var ruleSet = RuleSet.LoadEffectiveRuleSetFromFile(rulesetPath);
        var severities = ImmutableDictionary.CreateBuilder<string, ReportDiagnostic>(StringComparer.Ordinal);
        foreach (var entry in ruleSet.SpecificDiagnosticOptions)
        {
            severities[entry.Key] = entry.Value;
        }

        return new RulesetSeverities(severities.ToImmutable(), ruleSet.GeneralDiagnosticOption);
    }

    /// <summary>
    /// Whether a --reference value was meant as a file rather than a package name. Assembly
    /// names contain neither separators nor an extension, so either mark is enough.
    /// </summary>
    private static bool LooksLikeAPath(string reference) =>
        reference.Contains('/')
        || reference.Contains('\\')
        || reference.EndsWith(".dll", StringComparison.OrdinalIgnoreCase);

    private static IEnumerable<MetadataReference> RuntimeReferences()
    {
        var runtimeDir = Path.GetDirectoryName(typeof(object).Assembly.Location)!;
        foreach (var name in new[]
                 {
                     "System.Private.CoreLib.dll",
                     "System.Runtime.dll",
                     "netstandard.dll",
                     "System.Runtime.Extensions.dll",
                     "System.Collections.dll",
                     "System.Collections.Concurrent.dll",
                     "System.Linq.dll",
                     "System.Linq.Expressions.dll",
                     "System.Console.dll",
                     "System.Threading.dll",
                     "System.Threading.Tasks.dll",
                     "System.Threading.Thread.dll",
                     "System.Net.Primitives.dll",
                     "System.Net.Sockets.dll",
                     "System.IO.FileSystem.dll",
                     "System.Diagnostics.Process.dll",

                     // Added after a real Unity assembly failed to resolve IBufferWriter<T>
                     // through ZString: the list grows only when a measured run needs it.
                     "System.Memory.dll",
                 })
        {
            var path = Path.Combine(runtimeDir, name);
            if (File.Exists(path))
            {
                yield return MetadataReference.CreateFromFile(path);
            }
        }
    }

    /// <summary>
    /// The bundled UnityStubs by default; every managed DLL in --unity-dll-dir when given,
    /// so rules that need real Unity types (or Unity module assemblies) can be exercised.
    /// </summary>
    private static IEnumerable<MetadataReference> UnityReferences(string? unityDllDir)
    {
        if (unityDllDir is null)
        {
            yield return MetadataReference.CreateFromFile(typeof(UnityEngine.MonoBehaviour).Assembly.Location);
            yield break;
        }

        foreach (var dll in Directory.EnumerateFiles(unityDllDir, "*.dll").OrderBy(p => p, StringComparer.Ordinal))
        {
            MetadataReference? reference = null;
            try
            {
                reference = MetadataReference.CreateFromFile(dll);
            }
            catch (Exception)
            {
                // Native or otherwise unreadable DLLs sit next to managed ones; skipping
                // one is better than failing the whole run.
            }

            if (reference is object)
            {
                yield return reference;
            }
        }
    }

    /// <summary>
    /// An empty assembly with the requested name. Package detection reads
    /// Compilation.ReferencedAssemblyNames, so the name alone flips the conditional rules
    /// exactly as a real reference would — but nothing from that package resolves, so
    /// source calling into it needs the real DLL passed by path instead.
    /// </summary>
    private static MetadataReference FakeAssembly(string assemblyName)
    {
        var compilation = CSharpCompilation.Create(
            assemblyName,
            Array.Empty<Microsoft.CodeAnalysis.SyntaxTree>(),
            RuntimeReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        using var stream = new MemoryStream();
        var result = compilation.Emit(stream);
        if (!result.Success)
        {
            throw new CliException($"Could not synthesize a reference named '{assemblyName}'.");
        }

        return MetadataReference.CreateFromImage(stream.ToArray());
    }

    private sealed class AdditionalTextFile : AdditionalText
    {
        private readonly string _path;

        public AdditionalTextFile(string path) => _path = path;

        public override string Path => _path;

        public override SourceText GetText(CancellationToken cancellationToken = default) =>
            SourceText.From(File.ReadAllText(_path));
    }
}
