namespace UnityPerformanceAnalyzers.Cli;

/// <summary>Writes a baseline, or refuses to.</summary>
internal static class BaselineWriter
{
    /// <summary>
    /// Refuses when the run that produced the diagnostics was not a complete one. Freezing an
    /// under-reported analysis writes "this was never really checked" into the contract, and
    /// the debt it missed surfaces later as new violations.
    /// </summary>
    public static void EnsureRunIsWritable(CliOptions options, AnalysisResult result)
    {
        if (!options.WholeAssembly)
        {
            throw new CliException(
                "--write-baseline requires --whole-assembly: without it symbol resolution is "
                + "incomplete by design, so rules go quiet rather than firing and the recorded "
                + "debt would be short.");
        }

        if (!result.AnalyzerFailures.IsEmpty)
        {
            throw new CliException(
                $"Refusing to write a baseline: {result.AnalyzerFailures.Length} analyzer(s) "
                + "failed to run, so this result is short of what a working run would report.");
        }

        if (result.CompileErrorCount > 0)
        {
            throw new CliException(
                $"Refusing to write a baseline: {result.CompileErrorCount} compile error(s) "
                + "leave types unresolved, so rules that key off them did not fire.");
        }
    }

    /// <summary>
    /// Refuses to replace a baseline covering files this run did not analyze — but only those
    /// still on disk.
    /// </summary>
    /// <remarks>
    /// Without that qualifier a deleted or renamed file locks the baseline out of regeneration
    /// for good, since its old path can never appear in a successful run again, leaving
    /// hand-editing the JSON as the only way out. Entries whose file is gone are simply
    /// dropped; a rename is a disappearance plus an appearance, which is why no rename
    /// tracking is needed.
    /// </remarks>
    public static void EnsureCoversExistingBaseline(
        string path,
        BaselineDocument existing,
        IReadOnlyCollection<string> analyzedFiles)
    {
        var covered = new HashSet<string>(analyzedFiles, StringComparer.Ordinal);
        var directory = DirectoryOf(path);

        var uncovered = existing.Entries
            .Select(e => e.Key.File)
            .Where(file => !covered.Contains(file))
            .Where(file => File.Exists(Path.Combine(directory, file)))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(file => file, StringComparer.Ordinal)
            .ToArray();

        if (uncovered.Length == 0)
        {
            return;
        }

        var shown = string.Join(", ", uncovered.Take(5));
        var more = uncovered.Length > 5 ? $" (and {uncovered.Length - 5} more)" : string.Empty;
        throw new CliException(
            $"Refusing to overwrite {path}: it covers files this run did not analyze - "
            + $"{shown}{more}. Regenerating a baseline means analyzing the whole project; "
            + "a partial run would drop the rest of the contract.");
    }

    /// <summary>
    /// Replaces the file atomically. An interrupted write destroys a contract that is already
    /// committed, so the existing file is either untouched or wholly replaced.
    /// </summary>
    /// <summary>
    /// Refuses when the path names a symbolic link. Call this before anything else touches the
    /// path: reading it first means a link whose target is not a baseline is refused for the
    /// target's contents rather than for being a link, and the caller is told the wrong thing
    /// about the thing they own.
    /// </summary>
    public static void EnsureNotSymbolicLink(string path)
    {
        // Asked of the link itself rather than through File.Exists, which answers false for a
        // dangling link — and that is precisely the case where replacing it destroys a link
        // someone placed deliberately. Following one would write to whatever sits at the other
        // end, which is not the file the caller named.
        if (IsSymbolicLink(path))
        {
            throw new CliException($"Refusing to write through a symbolic link: {path}");
        }
    }

    public static void Write(string path, BaselineDocument document)
    {
        var directory = DirectoryOf(path);
        Directory.CreateDirectory(directory);

        // Repeated rather than assumed of the caller: this is the method that replaces the
        // file, and the guard belongs where the damage would be done as well as where the
        // message is most useful.
        EnsureNotSymbolicLink(path);

        // Same directory, so the replacement stays on one file system and can be atomic.
        var temporary = Path.Combine(directory, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            using (var stream = new FileStream(
                temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                var bytes = document.Serialize();
                stream.Write(bytes, 0, bytes.Length);
                stream.Flush(flushToDisk: true);
            }

            File.Move(temporary, path, overwrite: true);
        }
        catch (Exception ex) when (ex is not CliException)
        {
            throw new CliException($"Failed to write {path}: {ex.Message}");
        }
        finally
        {
            if (File.Exists(temporary))
            {
                try
                {
                    File.Delete(temporary);
                }
                catch (IOException)
                {
                }
            }
        }
    }

    /// <summary>
    /// Whether the path names a symbolic link, whether or not its target exists. Only the
    /// target itself is checked; a symlinked parent directory is an ordinary layout, and the
    /// substitution worth defending against happens at the file the caller named.
    /// </summary>
    private static bool IsSymbolicLink(string path)
    {
        try
        {
            // ResolveLinkTarget reports the link rather than following it, so a dangling link
            // answers the same as a live one. FileInfo.LinkTarget is documented to do that too,
            // but it reaches the answer through a FileInfo whose own existence checks differ by
            // platform, and the dangling case is the one that has to be right.
            return File.ResolveLinkTarget(path, returnFinalTarget: false) is object;
        }
        catch (IOException)
        {
            // No entry at this path at all, which is the ordinary case: a baseline being
            // written for the first time.
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static string DirectoryOf(string path)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        return string.IsNullOrEmpty(directory) ? Directory.GetCurrentDirectory() : directory;
    }
}
