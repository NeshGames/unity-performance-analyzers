using System;
using System.IO;
using System.Linq;

namespace UnityPerformanceAnalyzers.Tests
{
    /// <summary>
    /// The repository checkout the tests read committed artefacts from. Anchored on the test
    /// assembly's own location rather than the working directory: some tests change the
    /// working directory, and xUnit runs collections in parallel.
    /// </summary>
    internal static class TestRepository
    {
        public static readonly string Root = Find();

        private static string Find()
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is object)
            {
                if (directory.EnumerateFiles("*.sln").Any())
                {
                    return directory.FullName;
                }

                directory = directory.Parent;
            }

            throw new InvalidOperationException("no directory containing a .sln above " + AppContext.BaseDirectory);
        }
    }
}
