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
        public const int ExitClean = 0;
        public const int ExitDiagnostics = 1;
        public const int ExitError = 2;

        /// <summary>
        /// Exit code for a completed run. An analyzer that failed to execute outranks the
        /// severity threshold entirely: there is no finding to weigh, only an analysis
        /// that did not happen.
        /// </summary>
        public static int ResolveExitCode(AnalysisResult result, string failOn)
        {
            if (!result.AnalyzerFailures.IsEmpty)
            {
                return ExitError;
            }

            return AnalysisRunner.ShouldFail(result, failOn) ? ExitDiagnostics : ExitClean;
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

                var result = AnalysisRunner.Run(options);
                OutputWriter.WriteAnalysis(stdout, result, options.Format);

                if (!result.AnalyzerFailures.IsEmpty)
                {
                    // Independent of --fail-on: an analyzer that threw did not analyze
                    // anything, so no severity threshold can make this run meaningful.
                    stderr.WriteLine($"{result.AnalyzerFailures.Length} analyzer(s) failed to run:");
                    foreach (var failure in result.AnalyzerFailures)
                    {
                        stderr.WriteLine($"  {failure}");
                    }

                    return ResolveExitCode(result, options.FailOn);
                }

                if (result.CompileErrorCount > 0)
                {
                    // Analyzers key off resolved symbols, so a compilation that does not
                    // build can silently under-report. Whether that is a failure depends on
                    // what the caller claimed: --whole-assembly asserts the file set is a
                    // complete compilation unit, so errors there mean the results cannot be
                    // trusted as a gate. Without it, a partial file set is expected to have
                    // unresolved types and the run stays advisory.
                    stderr.WriteLine(
                        $"{result.CompileErrorCount} compile error(s); analyzer results may be incomplete.");

                    if (options.WholeAssembly)
                    {
                        stderr.WriteLine(
                            "Refusing to report success: --whole-assembly declares a complete compilation.");
                        return ExitError;
                    }
                }

                return ResolveExitCode(result, options.FailOn);
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
