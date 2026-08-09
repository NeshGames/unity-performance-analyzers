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

        /// <summary>
        /// How many compile errors the text channel lists before summarizing the rest. A set of
        /// compile errors is usually a handful of missing types repeated dozens of times, so a
        /// score of lines is enough to see the shape without burying the findings above it.
        /// </summary>
        private const int CompileErrorsShown = 20;

        /// <summary>
        /// Lists the compile errors behind the count. Without them the user is told a number
        /// and left with nowhere to go: under --whole-assembly the run is refused, a baseline
        /// cannot be written, and which type failed to resolve is the one thing that would
        /// make it fixable.
        /// </summary>
        private static void WriteCompileErrors(TextWriter stderr, AnalysisResult result)
        {
            foreach (var error in result.CompileErrors.Take(CompileErrorsShown))
            {
                stderr.WriteLine(
                    $"{error.File}({error.Line},{error.Column}): error {error.Id}: {error.Message}");
            }

            var remaining = result.CompileErrors.Length - CompileErrorsShown;
            if (remaining > 0)
            {
                stderr.WriteLine($"... and {remaining} more compile errors.");
            }
        }

        /// <summary>
        /// Filters a run through its baseline, if one was given. Reading a baseline places no
        /// completeness demand on the run: comparing a single changed file against the contract
        /// is the ordinary way to use this. Only producing the contract requires a full run.
        /// </summary>
        private static AnalysisResult ApplyBaseline(AnalysisResult result, CliOptions options)
        {
            if (options.BaselinePath is not { } path)
            {
                return result;
            }

            var outcome = BaselineFilter.Apply(
                result.Diagnostics,
                BaselineDocument.Read(path),
                result.AnalyzedFiles,
                result.IsComplete);

            return result with
            {
                Diagnostics = outcome.Reported,
                BaselineSuppressedCount = outcome.SuppressedCount,
                BaselineStaleCount = outcome.StaleCount,
            };
        }

        /// <summary>Writes the baseline for this run, or throws; returns the entry count.</summary>
        private static int WriteBaseline(string target, CliOptions options, AnalysisResult result)
        {
            BaselineWriter.EnsureRunIsWritable(options, result);

            if (File.Exists(target))
            {
                BaselineWriter.EnsureCoversExistingBaseline(
                    target, BaselineDocument.Read(target), result.AnalyzedFiles);
            }

            var document = BaselineFilter.Build(result.Diagnostics);
            BaselineWriter.Write(target, document);
            return document.Entries.Length;
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

                var result = ApplyBaseline(AnalysisRunner.Run(options), options);
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
                    WriteCompileErrors(stderr, result);

                    if (options.WholeAssembly)
                    {
                        stderr.WriteLine(
                            "Refusing to report success: --whole-assembly declares a complete compilation.");
                        return ExitError;
                    }
                }

                if (options.WriteBaselinePath is { } target)
                {
                    var written = WriteBaseline(target, options, result);
                    stderr.WriteLine($"Wrote {written} baseline entries to {target}.");

                    // Everything reported is part of the contract as of this moment, so
                    // --fail-on has nothing left to weigh. Applying it would make freezing
                    // existing debt an operation that always fails.
                    return ExitClean;
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
