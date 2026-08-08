using System;
using System.IO;
using UnityEngine;

/// <summary>
/// Runs the allocation measurements inside a built player and quits. This is the half the
/// editor cannot answer: the whole question is whether IL2CPP's class library picks the
/// same comparers as Mono's, so the numbers have to come from a real player.
///
/// The report goes to three places because a player has no console to read: the log file
/// (via Debug.Log), a file next to the executable, and persistentDataPath as a fallback
/// for when the install directory is not writable.
/// </summary>
public sealed class MeasurementBootstrap : MonoBehaviour
{
    /// <summary>File name used for the report in both output locations.</summary>
    public const string ReportFileName = "allocation-player-report.txt";

    private void Start()
    {
        // The backend is identified at runtime rather than stamped at build time: the
        // report's "runtime" line already distinguishes Mono from IL2CPP, which is the
        // distinction this run exists to make.
        //
        // No animator controller here: building one needs the editor API. The report labels
        // the controller as <none>, so the animator rows read as the empty-result path and
        // must not be compared against the editor numbers.
        var report = "[MEASURE] context | player build" + Environment.NewLine
            + AllocationMeasurement.RunAll();

        Debug.Log(report);
        TryWrite(Path.Combine(Application.dataPath, ".." + Path.DirectorySeparatorChar + ReportFileName), report);
        TryWrite(Path.Combine(Application.persistentDataPath, ReportFileName), report);

        Application.Quit(0);
    }

    private static void TryWrite(string path, string contents)
    {
        try
        {
            File.WriteAllText(path, contents);
            Debug.Log("[MEASURE] wrote report | " + Path.GetFullPath(path));
        }
        catch (Exception exception)
        {
            Debug.LogWarning("[MEASURE] could not write " + path + " | " + exception.Message);
        }
    }
}
