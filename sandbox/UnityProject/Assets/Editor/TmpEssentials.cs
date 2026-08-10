using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEngine;
using PackageInfo = UnityEditor.PackageManager.PackageInfo;

/// <summary>
/// Imports TextMeshPro's essential resources into the measurement project, and refuses to
/// continue without them.
/// </summary>
/// <remarks>
/// TMP's runtime reads <c>TMP_Settings</c> from Resources, and without that asset the first
/// call into <c>SetText</c> throws inside <c>PopulateTextProcessingArray</c>. A measurement
/// loop then times the exception path: 20,000 stack traces, and a number that describes
/// nothing. That is the failure this project keeps finding in its own measurements, so the
/// import is checked rather than assumed.
///
/// The resources are not committed. They are Unity's, they are two megabytes of fonts, and
/// every editor that can run this project already carries a copy:
///   2022.3   Library/PackageCache/com.unity.textmeshpro@3.0.7/Package Resources/
///   Unity 6  the built-in com.unity.ugui, where TextMeshPro now lives
/// </remarks>
public static class TmpEssentials
{
    private const string PackageFileName = "TMP Essential Resources.unitypackage";

    private const string SettingsAssetPath = "Assets/TextMesh Pro/Resources/TMP Settings.asset";

    /// <summary>
    /// Imports the essentials if they are missing. Throws when they cannot be imported, so a
    /// caller that ignores the result still fails loudly.
    /// </summary>
    public static void EnsureImported()
    {
        if (AssetDatabase.LoadAssetAtPath<ScriptableObject>(SettingsAssetPath) != null)
        {
            Debug.Log("[MEASURE] TMP essentials already present");
            return;
        }

        var packagePath = FindEssentialsPackage();
        if (packagePath is null)
        {
            throw new InvalidOperationException(
                "TMP essentials not found in any registered package. Looked for '" + PackageFileName
                + "' under com.unity.textmeshpro and com.unity.ugui.");
        }

        Debug.Log("[MEASURE] importing TMP essentials | " + packagePath);
        ImportImmediately(packagePath);
        AssetDatabase.Refresh();

        if (AssetDatabase.LoadAssetAtPath<ScriptableObject>(SettingsAssetPath) == null)
        {
            throw new InvalidOperationException(
                "TMP essentials were imported from '" + packagePath + "' but " + SettingsAssetPath
                + " is still absent. Measuring TMP without it times the exception path.");
        }

        Debug.Log("[MEASURE] TMP essentials imported");
    }

    private static string FindEssentialsPackage()
    {
        foreach (var package in PackageInfo.GetAllRegisteredPackages())
        {
            if (package.name != "com.unity.textmeshpro" && package.name != "com.unity.ugui")
            {
                continue;
            }

            var candidate = Path.Combine(package.resolvedPath, "Package Resources", PackageFileName);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    /// <summary>
    /// The synchronous import. <see cref="AssetDatabase.ImportPackage"/> is asynchronous and a
    /// batch-mode editor started with <c>-quit</c> exits before it finishes, leaving a project
    /// that looks imported to the log and is not. The immediate form is internal, so it is
    /// called by reflection and its absence is an error rather than a silent fallback.
    /// </summary>
    private static void ImportImmediately(string packagePath)
    {
        var method = typeof(AssetDatabase).GetMethod(
            "ImportPackageImmediately",
            BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public,
            binder: null,
            types: new[] { typeof(string) },
            modifiers: null);

        if (method is null)
        {
            throw new InvalidOperationException(
                "AssetDatabase.ImportPackageImmediately is not available in this editor ("
                + Application.unityVersion + "), and the asynchronous import cannot be awaited in "
                + "batch mode. Import '" + packagePath + "' by hand and re-run.");
        }

        method.Invoke(null, new object[] { packagePath });
    }
}
