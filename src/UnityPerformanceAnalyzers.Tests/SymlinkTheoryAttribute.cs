using System;
using System.IO;
using Xunit;

namespace UnityPerformanceAnalyzers.Tests;

/// <summary>
/// A theory that is reported as skipped where symbolic links cannot be created, rather than
/// returning early and being reported as a pass.
/// </summary>
/// <remarks>
/// Windows grants the privilege only to an administrator or with Developer Mode on, so a
/// developer machine may genuinely be unable to run these. The reason this is an attribute
/// rather than an early return inside the test is that an early return is indistinguishable
/// from a test that ran: the symlink test passed on Windows for two version lines while both
/// of its branches were broken on Linux, which is where CI runs it. Deciding at discovery is
/// also the only spelling xunit 2.x reports as skipped - a thrown skip reaches VSTest as a
/// failure.
/// </remarks>
public sealed class SymlinkTheoryAttribute : TheoryAttribute
{
    public SymlinkTheoryAttribute()
    {
        if (!SymlinkSupport.Available)
        {
            Skip = "symbolic links cannot be created on this machine "
                + "(Windows needs an administrator or Developer Mode)";
        }
    }
}

internal static class SymlinkSupport
{
    /// <summary>Probed once, by creating one, because no property answers this reliably.</summary>
    internal static readonly bool Available = Probe();

    private static bool Probe()
    {
        var directory = Path.Combine(
            Path.GetTempPath(), "upa-symlink-probe-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(directory);
            // A dangling link on purpose: that is the case the tests care about, and creating
            // one proves the privilege without needing a target.
            File.CreateSymbolicLink(
                Path.Combine(directory, "link"), Path.Combine(directory, "target"));
            return true;
        }
        catch (Exception)
        {
            return false;
        }
        finally
        {
            try
            {
                Directory.Delete(directory, recursive: true);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}
