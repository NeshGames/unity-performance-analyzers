using Xunit;

namespace UnityPerformanceAnalyzers.Tests
{
    /// <summary>
    /// The tests that change the process working directory. It is one value for the whole
    /// process, and xUnit runs collections in parallel, so two of these running at once would
    /// each resolve relative paths against the other's directory. Run on their own instead.
    /// </summary>
    [CollectionDefinition(Name, DisableParallelization = true)]
    public sealed class WorkingDirectoryCollection
    {
        public const string Name = "Working directory";
    }
}
