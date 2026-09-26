using System.Collections.Immutable;

namespace UnityPerformanceAnalyzers.Cli;

/// <summary>
/// What Unity actually compiled one assembly with: its sources, its scripting defines, and
/// its references, partitioned by where they live.
/// </summary>
internal sealed record UnityCompileArgs(
    string SourceResponseFile,
    ImmutableArray<string> Defines,
    ImmutableArray<string> Sources,
    ImmutableArray<string> ProjectReferences,
    ImmutableArray<string> UnityDllDirectories,
    int DiscardedReferences,
    bool AllowUnsafe);

/// <summary>
/// Reads the response file Unity's build hands to csc.
/// </summary>
/// <remarks>
/// The alternative input was a Unity-generated <c>.csproj</c>, which requires the user to
/// have an IDE integration installed and to have regenerated project files. Bee's response
/// file is there after any compile, and it is what the compiler was actually given rather
/// than a projection of it.
/// <para>
/// Nothing here falls back to scanning <c>Assets/**/*.cs</c> when the file is missing. What
/// a fallback would have to guess at is the defines and the assembly boundary, and those two
/// decide whether the analysis is answering the right question at all — a wrong guess reads
/// exactly like a correct one.
/// </para>
/// </remarks>
internal static class UnityCompileArgsReader
{
    private const string DefinePrefix = "-define:";
    private const string ReferencePrefix = "-r:";

    /// <summary>
    /// Unity's own module assemblies. They are only in the editor installation, so they
    /// cannot be referenced by a project-relative path; the CLI takes the directory instead.
    /// </summary>
    private const string UnityManagedMarker = "/Managed/UnityEngine/";

    public static UnityCompileArgs Read(string projectDirectory, string assemblyName)
    {
        var responseFile = Locate(projectDirectory, assemblyName);
        var projectRoot = Path.GetFullPath(projectDirectory);

        var defines = ImmutableArray.CreateBuilder<string>();
        var sources = ImmutableArray.CreateBuilder<string>();
        var projectReferences = ImmutableArray.CreateBuilder<string>();
        var unityDirectories = new SortedSet<string>(StringComparer.Ordinal);
        var discarded = 0;
        var allowUnsafe = false;

        foreach (var raw in File.ReadAllLines(responseFile))
        {
            var line = raw.Trim();
            if (line.Length == 0)
            {
                continue;
            }

            if (line.StartsWith(DefinePrefix, StringComparison.Ordinal))
            {
                defines.Add(Unquote(line.Substring(DefinePrefix.Length)));
                continue;
            }

            // Unity writes it for an assembly definition with allowUnsafeCode, which Burst,
            // NativeArray pointer access and ZString all need. Dropping it turned every pointer
            // into CS0227, and under --whole-assembly that is an exit 2 and a baseline that can
            // never be written. The last spelling wins, as it does for csc.
            if (UnsafeSwitch(line) is { } unsafeValue)
            {
                allowUnsafe = unsafeValue;
                continue;
            }

            if (line.StartsWith(ReferencePrefix, StringComparison.Ordinal))
            {
                Classify(
                    Unquote(line.Substring(ReferencePrefix.Length)),
                    projectRoot,
                    projectReferences,
                    unityDirectories,
                    ref discarded);
                continue;
            }

            // Anything not an option is a source file. Unity quotes every one of them, which
            // is what separates them from the options that happen to lack a leading dash.
            if (line.StartsWith("\"", StringComparison.Ordinal))
            {
                sources.Add(Relative(Unquote(line), projectRoot));
            }
        }

        if (sources.Count == 0)
        {
            throw new CliException(
                $"{Relative(responseFile, projectRoot)} lists no source files. "
                + $"Is '{assemblyName}' the assembly you meant?");
        }

        return new UnityCompileArgs(
            Relative(responseFile, projectRoot),
            defines.ToImmutable(),
            sources.ToImmutable(),
            projectReferences.ToImmutable(),
            unityDirectories.ToImmutableArray(),
            discarded,
            allowUnsafe);
    }

    /// <summary>
    /// The value of an unsafe switch in any of csc's spellings, or null when the line is not
    /// one. <c>-langversion</c> and <c>-nullable</c> are deliberately not carried: this tool
    /// parses at C# 9, which is what both supported Unity versions compile at, and a nullable
    /// context only ever changes warnings, which are never counted as compile errors.
    /// </summary>
    private static bool? UnsafeSwitch(string line)
    {
        if (line.Length < 2 || (line[0] != '-' && line[0] != '/'))
        {
            return null;
        }

        return line.Substring(1) switch
        {
            "unsafe" or "unsafe+" => true,
            "unsafe-" => false,
            _ => null,
        };
    }

    /// <summary>
    /// Sorts one reference into the three kinds. Passing all of them through as
    /// <c>--reference</c> was measured on 2026-08-10: 266 references produced 1133 compile
    /// errors, every one of them a variant of "Predefined type 'System.Void' is not defined",
    /// because Unity's netstandard shims and this tool's own .NET core library leave Roslyn
    /// with no way to pick a core library. Dropping the shims produced zero.
    /// </summary>
    private static void Classify(
        string reference,
        string projectRoot,
        ImmutableArray<string>.Builder projectReferences,
        SortedSet<string> unityDirectories,
        ref int discarded)
    {
        var full = Path.GetFullPath(reference, projectRoot);
        var slashed = full.Replace(Path.DirectorySeparatorChar, '/');

        if (IsUnder(full, projectRoot))
        {
            projectReferences.Add(Relative(full, projectRoot));
            return;
        }

        var marker = slashed.IndexOf(UnityManagedMarker, StringComparison.OrdinalIgnoreCase);
        if (marker >= 0)
        {
            unityDirectories.Add(slashed.Substring(0, marker + UnityManagedMarker.Length - 1));
            return;
        }

        discarded++;
    }

    /// <summary>
    /// The response file for one assembly, or a refusal that says how to produce one. The
    /// newest wins when a project has several Bee artifact directories: they accumulate
    /// across editor versions and configurations, and the current one is the one just written.
    /// </summary>
    private static string Locate(string projectDirectory, string assemblyName)
    {
        var artifacts = Path.Combine(projectDirectory, "Library", "Bee", "artifacts");
        var candidates = Directory.Exists(artifacts)
            ? Directory.EnumerateDirectories(artifacts, "*.dag")
                .Select(directory => Path.Combine(directory, assemblyName + ".rsp"))
                .Where(File.Exists)
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .ToArray()
            : Array.Empty<string>();

        if (candidates.Length > 0)
        {
            return candidates[0];
        }

        throw new CliException(
            $"No Unity compile arguments for '{assemblyName}' under {artifacts}. "
            + "Open the project in Unity and let it compile once - this reads the response "
            + "file Unity hands to the C# compiler, which is written by every compile.");
    }

    private static bool IsUnder(string path, string root)
    {
        var relative = Path.GetRelativePath(root, path);
        return !Path.IsPathRooted(relative)
            && relative != ".."
            && !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            && !relative.StartsWith("../", StringComparison.Ordinal);
    }

    private static string Relative(string path, string projectRoot) =>
        Path.GetRelativePath(projectRoot, Path.GetFullPath(path, projectRoot))
            .Replace(Path.DirectorySeparatorChar, '/');

    private static string Unquote(string value) => value.Trim().Trim('"');
}
