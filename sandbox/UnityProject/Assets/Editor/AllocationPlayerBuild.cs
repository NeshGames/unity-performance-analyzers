using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Builds an IL2CPP standalone player that runs the allocation measurements and quits.
/// Run with:
///   Unity -batchmode -quit -projectPath . -executeMethod AllocationPlayerBuild.BuildIl2Cpp
///     -logFile &lt;path&gt;
/// The player lands in Builds/il2cpp/ under the project root; launch it with
/// -batchmode -nographics -logFile to collect the report.
///
/// The scene is generated rather than committed: it holds one object with one component,
/// and a generated scene cannot drift away from the harness it is supposed to run.
/// </summary>
public static class AllocationPlayerBuild
{
    /// <summary>Directory the player is built into, relative to the project root.</summary>
    public const string BuildDirectory = "Builds/il2cpp";

    private const string ScenePath = "Assets/Measurement/MeasurementScene.unity";

    /// <summary>Switches the standalone target to IL2CPP and builds the measurement player.</summary>
    public static void BuildIl2Cpp()
    {
        try
        {
            // The player carries whatever is in the project, so the essentials have to be here
            // before the build, not before the run.
            TmpEssentials.EnsureImported();

            PlayerSettings.SetScriptingBackend(BuildTargetGroup.Standalone, ScriptingImplementation.IL2CPP);
            PlayerSettings.SetApiCompatibilityLevel(BuildTargetGroup.Standalone, ApiCompatibilityLevel.NET_Standard_2_0);

            CreateMeasurementScene();

            Directory.CreateDirectory(BuildDirectory);
            var options = new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = Path.Combine(BuildDirectory, "MeasurementPlayer.exe"),
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.None,
            };

            var report = BuildPipeline.BuildPlayer(options);
            Debug.Log("[MEASURE] build result | " + report.summary.result
                + " | errors: " + report.summary.totalErrors);

            if (report.summary.result != BuildResult.Succeeded)
            {
                EditorApplication.Exit(1);
                return;
            }
        }
        catch (Exception exception)
        {
            Debug.LogError("[MEASURE] build threw | " + exception);
            EditorApplication.Exit(1);
            return;
        }
        finally
        {
            // Leave the project on Mono so the editor half of the harness keeps running the
            // combination it is supposed to.
            PlayerSettings.SetScriptingBackend(BuildTargetGroup.Standalone, ScriptingImplementation.Mono2x);
            AssetDatabase.DeleteAsset(ScenePath);
        }

        EditorApplication.Exit(0);
    }

    private static void CreateMeasurementScene()
    {
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var host = new GameObject("MeasurementBootstrap");
        host.AddComponent<MeasurementBootstrap>();
        SceneManager.MoveGameObjectToScene(host, scene);
        EditorSceneManager.SaveScene(scene, ScenePath);
    }
}
