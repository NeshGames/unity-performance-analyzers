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

                var baseline = BaselineSession.Open(options);
                var result = AnalysisRunner.Run(options);
                if (baseline is object)
                {
                    result = baseline.Filter(result);
                }

                OutputWriter.WriteAnalysis(stdout, result, options.Format);
                OutputWriter.WriteRunProblems(stderr, result, options);

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
