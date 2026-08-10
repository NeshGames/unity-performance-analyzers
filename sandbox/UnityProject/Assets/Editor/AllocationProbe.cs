using System;
using System.IO;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

/// <summary>
/// Batchmode entry point for the allocation measurements. Run with:
///   Unity -batchmode -quit -projectPath . -executeMethod AllocationProbe.Run
///     -logFile &lt;path&gt;
/// The report is written to Measurements/allocation-measurement.txt under the project
/// root — not Temp/, which the editor wipes on quit. Exits 0 when the run completes,
/// 1 on exception.
///
/// The measurement code itself lives in a runtime assembly so the same harness can run
/// from a player build; only the parts that need editor-only APIs (building an animator
/// controller to measure against) live here.
/// </summary>
public static class AllocationProbe
{
    /// <summary>Where the report is written, relative to the project root.</summary>
    public const string ReportDirectory = "Measurements";

    /// <summary>Runs the measurements, writes the report, and exits the editor.</summary>
    public static void Run()
    {
        try
        {
            // Without TMP's settings asset the TMP section measures the exception path.
            TmpEssentials.EnsureImported();

            var apiLevel = PlayerSettings.GetApiCompatibilityLevel(BuildTargetGroup.Standalone);
            var backend = PlayerSettings.GetScriptingBackend(BuildTargetGroup.Standalone);

            var report = "[MEASURE] context | editor batchmode"
                + Environment.NewLine
                + "[MEASURE] api compatibility level (Standalone) | " + apiLevel
                + Environment.NewLine
                + "[MEASURE] scripting backend (Standalone) | " + backend
                + Environment.NewLine
                + AllocationMeasurement.RunAll(BuildAnimatorController());

            Directory.CreateDirectory(ReportDirectory);
            var fileName = string.Format("allocation-{0}-{1}.txt", Application.unityVersion, apiLevel);
            File.WriteAllText(Path.Combine(ReportDirectory, fileName), report);
            Debug.Log(report);
        }
        catch (Exception exception)
        {
            Debug.LogError("[MEASURE] unexpected exception: " + exception);
            EditorApplication.Exit(1);
            return;
        }

        EditorApplication.Exit(0);
    }

    /// <summary>
    /// Builds a throwaway controller with one state playing a one-second clip, so the
    /// clip-info calls have something to report. Kept in memory — writing an asset would
    /// leave the sandbox project dirty between runs.
    /// </summary>
    private static RuntimeAnimatorController BuildAnimatorController()
    {
        var clip = new AnimationClip { name = "MeasurementClip", legacy = false };
        clip.SetCurve(string.Empty, typeof(Transform), "localPosition.x", AnimationCurve.Linear(0f, 0f, 1f, 1f));

        var controller = new AnimatorController { name = "MeasurementController" };
        controller.AddLayer("Base Layer");
        var state = controller.layers[0].stateMachine.AddState("MeasurementState");
        state.motion = clip;
        controller.layers[0].stateMachine.defaultState = state;
        return controller;
    }
}
