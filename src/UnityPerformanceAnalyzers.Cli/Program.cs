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
            => ExitCode.For(result, failOn, wholeAssembly: false, baselineUpdated: false);

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

                var analysis = AnalysisRunner.Run(options);
                var result = baseline is object ? baseline.Filter(analysis) : analysis;

                OutputWriter.WriteAnalysis(stdout, result, options.Format);
                OutputWriter.WriteRunProblems(stderr, result, options);

                // A refused run never updates the contract: freezing what an incomplete
                // analysis saw is worse than reporting nothing.
                var updated = ExitCode.For(result, options, baselineUpdated: false) == ExitCode.Error
                    ? null
                    : baseline?.Update(analysis);

                if (updated is { } entries)
                {
                    stderr.WriteLine($"Updated {entries} baseline entries at {options.UpdateBaselinePath}.");
                }

                return ExitCode.For(result, options, updated is object);
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
