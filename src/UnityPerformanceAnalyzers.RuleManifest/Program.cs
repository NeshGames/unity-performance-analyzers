namespace UnityPerformanceAnalyzers.RuleManifest;

/// <summary>
/// Generates ruleset presets and the sandbox verification ruleset. Configuration UI and its
/// generated catalog were removed: coding agents edit the same ruleset/additional-file inputs
/// Unity and upa-cli consume directly.
/// </summary>
internal static class Program
{
    private const string Usage =
        "Usage: RuleManifest --all <repo-root> | RuleManifest --presets <repo-root>";

    private static int Main(string[] args)
    {
        if (args.Length == 2 && (args[0] == "--all" || args[0] == "--presets"))
        {
            return WritePresets(args[1]);
        }

        Console.Error.WriteLine(Usage);
        return 1;
    }

    private static int WritePresets(string repoRoot)
    {
        var files = PresetEmitter.WriteAll(repoRoot, out var removed);
        Console.WriteLine($"Wrote {files.Count} preset files under {Path.GetFullPath(repoRoot)}");
        foreach (var file in removed)
        {
            Console.WriteLine($"Removed {file}: generated, and no longer produced");
        }

        return 0;
    }
}
