namespace UnityPerformanceAnalyzers.Cli;

/// <summary>
/// What the process returns, and the whole of the reasoning behind it.
/// </summary>
/// <remarks>
/// There were three places that decided this and only one of them was a function. The other
/// two were <c>return</c> statements in the middle of the entry point, which meant the piece
/// with a name -- and the only piece with a direct test -- was also the piece that the two
/// most consequential cases never reached. The three values are a published contract that
/// scripts depend on; the precedence below is not, and it is stated here rather than left to
/// be inferred from where an early return happened to sit.
/// </remarks>
internal static class ExitCode
{
    public const int Clean = 0;
    public const int Diagnostics = 1;
    public const int Error = 2;

    /// <summary>
    /// The code for a completed run. The four rules are in precedence order, and each one
    /// outranks the threshold for a different reason.
    /// </summary>
    public static int For(AnalysisResult result, CliOptions options, bool baselineWritten)
        => For(result, options.FailOn, options.WholeAssembly, baselineWritten);

    /// <summary>
    /// The same rules from the two values they actually depend on, for callers that do not
    /// have a parsed command line -- the options object cannot be constructed from outside
    /// its own parser, which is a property worth keeping.
    /// </summary>
    public static int For(AnalysisResult result, string failOn, bool wholeAssembly, bool baselineWritten)
    {
        // An analyzer that threw did not analyze anything. There is no finding to weigh
        // against a severity threshold, only an analysis that did not happen.
        if (!result.AnalyzerFailures.IsEmpty)
        {
            return Error;
        }

        // Analyzers match on resolved symbols, so a compilation that does not build
        // under-reports silently. Whether that is fatal depends on what the caller claimed:
        // --whole-assembly asserts the file set is a complete compilation unit, so errors
        // there mean the run cannot be trusted as a gate. Without it, a partial file set is
        // expected to have unresolved types and the run stays advisory.
        if (result.CompileErrorCount > 0 && wholeAssembly)
        {
            return Error;
        }

        // Everything reported is part of the contract as of this moment, so --fail-on has
        // nothing left to weigh. Applying it would make freezing existing debt an operation
        // that always fails.
        if (baselineWritten)
        {
            return Clean;
        }

        return AnalysisRunner.ShouldFail(result, failOn) ? Diagnostics : Clean;
    }
}
