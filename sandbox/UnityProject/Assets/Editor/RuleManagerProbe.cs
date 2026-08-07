using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Batchmode probe for the Rule Manager's file layer. The window's GUI cannot run
/// headless, so this exercises the classes behind it (catalog loading, ruleset and
/// options-file round trips) against real project files via reflection — the types are
/// internal to the package's editor assembly on purpose. Run with:
///   Unity -batchmode -projectPath . -executeMethod RuleManagerProbe.Run
/// Exits 0 when every check passes, 1 otherwise; each check logs a [PROBE] line.
/// </summary>
public static class RuleManagerProbe
{
    private static int s_failures;

    public static void Run()
    {
        try
        {
            var assembly = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(candidate => candidate.GetName().Name == "NeshGames.UnityPerformanceAnalyzers.Editor");
            Check(assembly is object, "package editor assembly is loaded");
            if (assembly is null)
            {
                EditorApplication.Exit(1);
                return;
            }

            ProbeMenuItem(assembly);
            ProbeCatalog(assembly);
            ProbeRulesetRoundTrip(assembly);
            ProbeOptionsRoundTrip(assembly);
            ProbeEditorConfigSync(assembly);
        }
        catch (Exception exception)
        {
            Debug.LogError("[PROBE] unexpected exception: " + exception);
            s_failures++;
        }

        Debug.Log($"[PROBE] done, failures: {s_failures}");
        EditorApplication.Exit(s_failures == 0 ? 0 : 1);
    }

    private static void ProbeMenuItem(Assembly assembly)
    {
        var windowType = assembly.GetType("NeshGames.UnityPerformanceAnalyzers.Editor.RuleManagerWindow");
        Check(windowType is object, "RuleManagerWindow type exists");
        var menu = windowType?.GetMethod("Open", BindingFlags.Public | BindingFlags.Static)
            ?.GetCustomAttribute<MenuItem>();
        Check(menu?.menuItem == "Tools/Unity Performance Analyzers/Rule Manager",
            "menu item path is Tools/Unity Performance Analyzers/Rule Manager (got: " + (menu?.menuItem ?? "<none>") + ")");
    }

    private static void ProbeCatalog(Assembly assembly)
    {
        var catalogType = assembly.GetType("NeshGames.UnityPerformanceAnalyzers.Editor.RuleCatalog");
        var loadArgs = new object[] { null };
        var catalog = catalogType.GetMethod("Load").Invoke(null, loadArgs);
        Check(catalog is object, "rules.json loads (" + (loadArgs[0] ?? "no error") + ")");
        if (catalog is null)
        {
            return;
        }

        var upa = (Array)catalogType.GetField("upa").GetValue(catalog);
        Check(upa.Length == 25, "catalog lists 25 UPA rules (got " + upa.Length + ")");
        var ids = upa.Cast<object>()
            .Select(row => (string)row.GetType().GetField("id").GetValue(row))
            .ToArray();
        Check(ids.Contains("UPA0013") && !ids.Contains("UPA2001"), "catalog carries UPA0013, not UPA2001");
        Check(ids.Contains("UPA3004"), "catalog carries UPA3004");
    }

    private static void ProbeRulesetRoundTrip(Assembly assembly)
    {
        const string tempPath = "Temp/ProbeDefault.ruleset";
        File.Copy("Assets/Default.ruleset", tempPath, overwrite: true);

        var rulesetType = assembly.GetType("NeshGames.UnityPerformanceAnalyzers.Editor.RulesetFile");
        var tryLoadArgs = new object[] { tempPath, null, null };
        var loaded = (bool)rulesetType.GetMethod("TryLoad").Invoke(null, tryLoadArgs);
        Check(loaded, "ruleset loads (" + (tryLoadArgs[2] ?? "no error") + ")");
        var ruleset = tryLoadArgs[1];

        var currentAction = (string)rulesetType.GetMethod("GetAction").Invoke(ruleset, new object[] { "UPA0005" });
        Check(currentAction == "None", "UPA0005 reads back as None (got " + currentAction + ")");

        rulesetType.GetMethod("SetAction").Invoke(ruleset, new object[] { "UPA0005", "Warning", "UnityPerformanceAnalyzers" });
        rulesetType.GetMethod("Save").Invoke(ruleset, null);

        var saved = File.ReadAllText(tempPath);
        Check(saved.Contains("<Rule Id=\"UPA0005\" Action=\"Warning\" />"), "edited severity is saved");
        Check(saved.Contains("webgl-addon.ruleset"), "Include element preserved across save");
        Check(saved.Contains("UNT0006"), "other analyzer's rules preserved across save");
        Check(saved.Contains("unity-performance-analyzers preset: recommended"), "XML comment preserved across save");
    }

    private static void ProbeOptionsRoundTrip(Assembly assembly)
    {
        const string tempPath = "Temp/ProbeOptions.additionalfile";
        File.WriteAllText(tempPath, "# user comment\nmystery_key = keep me\nupa_hot_path_include_lambdas = true\n");

        var optionsType = assembly.GetType("NeshGames.UnityPerformanceAnalyzers.Editor.OptionsFile");
        var options = optionsType.GetMethod("Load").Invoke(null, new object[] { tempPath });
        optionsType.GetMethod("Set").Invoke(options, new object[] { "upa_hot_path_messages", "LateUpdate" });
        optionsType.GetMethod("Remove").Invoke(options, new object[] { "upa_hot_path_include_lambdas" });
        optionsType.GetMethod("Save").Invoke(options, null);

        var saved = File.ReadAllText(tempPath);
        Check(saved.Contains("# user comment"), "options file: user comment preserved");
        Check(saved.Contains("mystery_key = keep me"), "options file: unknown key preserved");
        Check(saved.Contains("upa_hot_path_messages = LateUpdate"), "options file: value written");
        Check(!saved.Contains("include_lambdas"), "options file: removed key is gone");
    }

    private static void ProbeEditorConfigSync(Assembly assembly)
    {
        // The sync must manage the [*.cs] section only — a key with the same name in a
        // scoped section (Editor folders here) has different semantics and must survive.
        const string path = ".editorconfig";
        var hadFile = File.Exists(path);
        var original = hadFile ? File.ReadAllText(path) : null;
        try
        {
            File.WriteAllText(path, string.Join("\n",
                "root = true",
                "",
                "[*.cs]",
                "upa_hot_path_messages = Update",
                "",
                "[Assets/Editor/**.cs]",
                "upa_hot_path_messages = LateUpdate",
                ""));

            var optionsType = assembly.GetType("NeshGames.UnityPerformanceAnalyzers.Editor.OptionsFile");
            optionsType.GetMethod("SyncToEditorConfig").Invoke(null, new object[]
            {
                new System.Collections.Generic.Dictionary<string, string>
                {
                    ["upa_hot_path_messages"] = "OnGUI",
                    ["upa_enum_switch_allow_default"] = "false",
                },
            });

            var saved = File.ReadAllText(path);
            var scopedSectionStart = saved.IndexOf("[Assets/Editor/**.cs]", StringComparison.Ordinal);
            Check(scopedSectionStart >= 0, "editorconfig sync: scoped section still present");
            Check(saved.Substring(scopedSectionStart).Contains("upa_hot_path_messages = LateUpdate"),
                "editorconfig sync: scoped override untouched");
            var mainSection = saved.Substring(0, scopedSectionStart);
            Check(mainSection.Contains("upa_hot_path_messages = OnGUI"),
                "editorconfig sync: [*.cs] value rewritten");
            Check(mainSection.Contains("upa_enum_switch_allow_default = false"),
                "editorconfig sync: new key inserted inside [*.cs]");
        }
        finally
        {
            if (hadFile)
            {
                File.WriteAllText(path, original);
            }
            else
            {
                File.Delete(path);
            }
        }
    }

    private static void Check(bool condition, string description)
    {
        if (condition)
        {
            Debug.Log("[PROBE] PASS " + description);
        }
        else
        {
            Debug.LogError("[PROBE] FAIL " + description);
            s_failures++;
        }
    }
}
