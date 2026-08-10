using UnityPerformanceAnalyzers.Cli;

// Exit codes are part of the public contract: 0 clean, 1 diagnostics at or above the
// fail threshold, 2 usage or execution error. Results go to stdout and nothing else
// does, so JSON output is directly parseable.
return CliEntryPoint.Run(args, Console.Out, Console.Error);

namespace UnityPerformanceAnalyzers.Cli
{
    /// <summary>Entry point, separated from the top-level statements so tests can drive it.</summary>
    internal static class CliEntryPoint
    {
        public const int ExitClean = ExitCode.Clean;
        public const int ExitDiagnostics = ExitCode.Diagnostics;
        public const int ExitError = ExitCode.Error;

        /// <summary>
        /// Exit code for a run with no baseline written. Kept here because it is what the
        /// tests reach for; the reasoning lives in <see cref="ExitCode"/> with the two cases
        /// this signature cannot express.
        /// </summary>
        public static int ResolveExitCode(AnalysisResult result, string failOn)
            => ExitCode.For(result, failOn, wholeAssembly: false, baselineWritten: false);

        /// <summary>
        /// The --fail-on-stale verdict, or null when staleness is not what decides this run.
        /// </summary>
        /// <remarks>
        /// A null count means the run could not tell, and answering "not stale" there would be
        /// a gate reporting a clean result it never established — the same failure the compile
        /// error rule exists to prevent, in the one place a user reaches for to be told the
        /// opposite.
        /// </remarks>
        private static int? StaleGate(AnalysisResult result, TextWriter stderr)
        {
            if (result.BaselineStaleCount is not { } stale)
            {
                stderr.WriteLine(
                    "--fail-on-stale cannot be answered: the analysis was incomplete, so "
                    + "unused baseline quota cannot be told apart from rules that did not run.");
                return ExitError;
            }

            if (stale == 0)
            {
                return null;
            }

            stderr.WriteLine(
                $"{stale} baseline entr{(stale == 1 ? "y is" : "ies are")} no longer matched. "
                + "Run again with --prune-baseline to remove the unused quota.");
            return ExitDiagnostics;
        }

        public static int Run(string[] args, TextWriter stdout, TextWriter stderr)
        {
            var options = CliOptions.Parse(args, out var parseError);
            if (options is null)
            {
                stderr.WriteLine(parseError);
                stderr.WriteLine("Run upa-cli --help for usage.");
                return ExitError;
            }

            if (options.ShowHelp)
            {
                stdout.Write(OutputWriter.Help());
                return ExitClean;
            }

            if (options.ShowVersion)
            {
                stdout.WriteLine(AnalyzerCatalog.ToolVersion);
                return ExitClean;
            }

            try
            {
                if (options.ListRules)
                {
                    OutputWriter.WriteRules(stdout, AnalyzerCatalog.Rules(), options.Format);
                    return ExitClean;
                }

                if (options.InitArgsPath is object)
                {
                    // The summary goes to stderr for the same reason the baseline's does:
                    // stdout carries results, and this mode produced none.
                    stderr.WriteLine(ArgsFileWriter.Write(options, ArgsFileWriter.Now()));
                    return ExitClean;
                }

                var baseline = BaselineSession.Open(options);

                // The unfiltered run is kept: filtering removes the occurrences the baseline
                // matched, and those are exactly the ones pruning has to count. Handing the
                // filtered result to Prune would find nothing for every baselined key and
                // delete the entire contract, which is the failure this feature exists to
                // prevent rather than cause.
                var analysis = AnalysisRunner.Run(options);
                var result = baseline is object ? baseline.Filter(analysis) : analysis;

                OutputWriter.WriteAnalysis(stdout, result, options.Format, options.ReportStaleBaseline);
                OutputWriter.WriteRunProblems(stderr, result, options);

                if (baseline?.Prune(analysis) is { } remaining)
                {
                    stderr.WriteLine(
                        $"Pruned {options.BaselinePath} to {remaining} entries; "
                        + "quota this run did not use is gone.");
                    return ExitClean;
                }

                // Before the threshold, and after pruning: a run asked to prune has just made
                // the answer zero, so the gate is about the runs that were not asked to.
                if (options.FailOnStale && StaleGate(result, stderr) is { } staleCode)
                {
                    return staleCode;
                }

                // A refused run writes no baseline: freezing what a broken analysis saw is
                // the one outcome worse than reporting nothing.
                var written = ExitCode.For(result, options, baselineWritten: false) == ExitCode.Error
                    ? null
                    : baseline?.Write(result);

                if (written is { } entries)
                {
                    stderr.WriteLine($"Wrote {entries} baseline entries to {options.WriteBaselinePath}.");
                }

                return ExitCode.For(result, options, written is object);
            }
            catch (CliException ex)
            {
                stderr.WriteLine(ex.Message);
                return ExitError;
            }
            catch (Exception ex)
            {
                stderr.WriteLine($"upa-cli failed: {ex.Message}");
                return ExitError;
            }
        }
    }
}
