using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Batchmode probe for how analyzers reach the IDE. Run with:
///   Unity -batchmode -quit -projectPath . -executeMethod CsprojAnalyzerProbe.Run
///     -logFile &lt;path&gt;
///
/// Unity hands the compiler its analyzers through the response file, but Rider and
/// Visual Studio read the generated .csproj instead. Those are two different channels,
/// and a package can reach one without reaching the other — which is exactly what
/// decides whether shipping the code fix assembly actually delivers code fixes.
///
/// Regenerates the project files, then reports every Analyzer item it finds.
/// </summary>
public static class CsprojAnalyzerProbe
{
    /// <summary>Regenerates project files and reports the analyzer items they carry.</summary>
    public static void Run()
    {
        var report = new StringBuilder();

        try
        {
            SyncProjectFiles(report);

            var root = Directory.GetCurrentDirectory();
            var projects = Directory.GetFiles(root, "*.csproj", SearchOption.TopDirectoryOnly)
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToArray();

            report.AppendLine(Line("csproj files found", projects.Length.ToString()));

            foreach (var project in projects)
            {
                var name = Path.GetFileName(project);
                var analyzerLines = File.ReadAllLines(project)
                    .Where(line => line.Contains("Analyzer Include"))
                    .Select(line => line.Trim())
                    .ToArray();

                report.AppendLine(Line(name + " analyzer items", analyzerLines.Length.ToString()));
                foreach (var line in analyzerLines)
                {
                    report.AppendLine(Line(name + "   ", line));
                }
            }
        }
        catch (Exception exception)
        {
            Debug.LogError("[CSPROJ] unexpected exception: " + exception);
            EditorApplication.Exit(1);
            return;
        }

        Debug.Log(report.ToString());
        EditorApplication.Exit(0);
    }

    /// <summary>
    /// Triggers project-file generation. The entry point moved between Unity versions and
    /// is not public in either, so both known shapes are tried and the one that exists wins.
    /// </summary>
    private static void SyncProjectFiles(StringBuilder report)
    {
        // The modern path: an installed IDE integration registers itself as the current
        // editor and owns generation. In batch mode nothing has selected one, so the probe
        // picks the first registered editor before asking it to sync.
        var codeEditorType = Type.GetType("Unity.CodeEditor.CodeEditor, Unity.CodeEditor");
        if (codeEditorType is object)
        {
            try
            {
                var editorsProperty = codeEditorType.GetProperty(
                    "Editor", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                var editor = editorsProperty?.GetValue(null);

                var pathsMethod = codeEditorType.GetMethod(
                    "GetFoundScriptEditorPaths",
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static
                        | System.Reflection.BindingFlags.NonPublic);
                if (pathsMethod?.Invoke(null, null) is System.Collections.IDictionary found && found.Count > 0)
                {
                    foreach (System.Collections.DictionaryEntry entry in found)
                    {
                        var setter = codeEditorType.GetMethod(
                            "SetCodeEditor",
                            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                        setter?.Invoke(null, new object[] { entry.Key });
                        report.AppendLine(Line("selected editor", entry.Key?.ToString() ?? "<null>"));
                        break;
                    }
                }

                var syncAll = editor?.GetType().GetMethod("SyncAll");
                if (syncAll is object)
                {
                    syncAll.Invoke(editor, null);
                    report.AppendLine(Line("sync entry point", "CodeEditor.Editor.SyncAll"));
                    return;
                }
            }
            catch (Exception exception)
            {
                report.AppendLine(Line("CodeEditor sync failed", exception.GetBaseException().Message));
            }
        }

        foreach (var candidate in new[]
        {
            ("UnityEditor.SyncVS, UnityEditor", "SyncSolution"),
            ("UnityEditor.VisualStudioIntegration.SyncVS, UnityEditor", "SyncSolution"),
        })
        {
            var type = Type.GetType(candidate.Item1);
            var method = type?.GetMethod(
                candidate.Item2,
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic
                    | System.Reflection.BindingFlags.Static);
            if (method is null)
            {
                continue;
            }

            method.Invoke(null, null);
            report.AppendLine(Line("sync entry point", candidate.Item1 + "." + candidate.Item2));
            return;
        }

        report.AppendLine(Line("sync entry point", "<none found — reporting existing files>"));
    }

    private static string Line(string label, string value) => "[CSPROJ] " + label + " | " + value;
}
