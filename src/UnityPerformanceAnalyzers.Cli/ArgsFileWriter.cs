using System.Globalization;
using System.Text;

namespace UnityPerformanceAnalyzers.Cli;

/// <summary>
/// Writes the response file that turns this tool into a gate over a real Unity assembly.
/// </summary>
/// <remarks>
/// The arguments a real gate needs — every define, every reference, the complete source set —
/// run to tens of thousands of characters, past what Windows will accept on a command line.
/// The response file was always the answer; producing it was the part left to the user.
/// </remarks>
internal static class ArgsFileWriter
{
    public static string Write(CliOptions options, string generatedAtUtc)
    {
        var project = options.ProjectDirectory;
        if (!Directory.Exists(project))
        {
            throw new CliException($"No such directory: {project}");
        }

        var args = UnityCompileArgsReader.Read(project, options.AssemblyName);
        var text = Render(options.AssemblyName, args, generatedAtUtc);

        var outputPath = options.InitArgsPath!;
        var directory = Path.GetDirectoryName(Path.GetFullPath(outputPath));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(outputPath, text);

        return $"Wrote {args.Sources.Length} sources, {args.Defines.Length} defines and "
            + $"{args.ProjectReferences.Length} references to {outputPath}, "
            + $"from {args.SourceResponseFile} ({args.DiscardedReferences} framework "
            + "references dropped).";
    }

    private static string Render(string assemblyName, UnityCompileArgs args, string generatedAtUtc)
    {
        var text = new StringBuilder();

        text.Append("# upa-cli ").Append(AnalyzerCatalog.ToolVersion)
            .Append(" --init-args, generated ").Append(generatedAtUtc).AppendLine();
        text.Append("# source: ").Append(args.SourceResponseFile).AppendLine();
        text.AppendLine("# Paths are relative to the Unity project root, so run it from there:");
        text.AppendLine("#   cd <project> && upa-cli @<this file>");
        text.AppendLine("# Regenerate after changing packages, defines or the editor version.");

        if (!args.UnityDllDirectories.IsEmpty)
        {
            text.AppendLine("# --unity-dll-dir below is this machine's Unity installation.");
            text.AppendLine("# On CI, point it at that machine's Unity or drop it for the bundled stubs.");
        }

        text.AppendLine();
        Emit(text, "--assembly-name", assemblyName);

        // Without it the whole-assembly rules do not run and a compile error is not fatal,
        // which is how a gate goes quiet: rules match on resolved symbols, so a missing
        // reference produces no findings rather than wrong ones.
        text.AppendLine("--whole-assembly");

        foreach (var define in args.Defines)
        {
            Emit(text, "--define", define);
        }

        foreach (var directory in args.UnityDllDirectories)
        {
            Emit(text, "--unity-dll-dir", directory);
        }

        foreach (var reference in args.ProjectReferences)
        {
            Emit(text, "--reference", reference);
        }

        foreach (var source in args.Sources)
        {
            text.AppendLine(source);
        }

        return text.ToString();
    }

    /// <summary>
    /// One argument per line, matching the response file format: a line is an argument, and
    /// no quoting rules are inherited.
    /// </summary>
    private static void Emit(StringBuilder text, string option, string value)
    {
        text.AppendLine(option);
        text.AppendLine(value);
    }

    /// <summary>
    /// The timestamp in the header. Passed in rather than read here so the tests can assert
    /// the header's shape, and stamped in UTC so two machines' files differ by content only.
    /// </summary>
    public static string Now() =>
        DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
}
