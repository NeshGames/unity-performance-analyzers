using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace NeshGames.UnityPerformanceAnalyzers.Editor
{
    /// <summary>
    /// Rule Manager: edits the files the analyzers actually consume — Assets/Default.ruleset
    /// (rule severities, tab one) and the universal options file (analyzer options, tab two).
    /// The rule list comes from the shipped rules.json catalog; the analyzer assembly itself
    /// is never loaded here.
    /// </summary>
    internal sealed class RuleManagerWindow : EditorWindow
    {
        private const string WebGlAddonFileName = "webgl-addon.ruleset";
        private const string PresetsFolder = "Samples~/Ruleset Presets";

        private static readonly string[] s_presetNames = { "minimal", "recommended", "strict", "cysharp-stack" };

        // Popup order; index 0 means "no explicit entry" so analyzer defaults and
        // ruleset Includes stay in charge (the WebGL add-on relies on that).
        private static readonly string[] s_actionLabels = { "Default", "None", "Info", "Warning", "Error" };

        private RuleCatalog _catalog;
        private string _catalogError;

        private RulesetFile _ruleset;
        private string _rulesetError;

        private readonly Dictionary<string, string> _pendingActions = new Dictionary<string, string>();
        private bool _rulesDirty;

        private OptionsFile _optionsFile;
        private readonly Dictionary<string, string> _pendingOptions = new Dictionary<string, string>();
        private bool _optionsDirty;
        private bool _syncEditorConfig;

        private List<(string assemblyName, string path)> _asmdefOverrides = new List<(string, string)>();

        private int _tab;
        private int _presetIndex = 1;
        private Vector2 _rulesScroll;
        private Vector2 _optionsScroll;
        private bool _untFoldout;
        private bool _overridesFoldout;

        [MenuItem("Tools/Unity Performance Analyzers/Rule Manager")]
        public static void Open()
        {
            GetWindow<RuleManagerWindow>("Rule Manager").minSize = new Vector2(520f, 400f);
        }

        private void OnEnable()
        {
            _catalog = RuleCatalog.Load(out _catalogError);
            if (_catalog is null)
            {
                return;
            }

            ReloadRuleset();
            ReloadOptions();
            RefreshAsmdefOverrides();
        }

        private void OnGUI()
        {
            if (_catalog is null)
            {
                EditorGUILayout.HelpBox(_catalogError ?? "Rule catalog unavailable.", MessageType.Error);
                return;
            }

            _tab = GUILayout.Toolbar(_tab, new[] { "Rules", "Options" });
            EditorGUILayout.Space();
            if (_tab == 0)
            {
                DrawRulesTab();
            }
            else
            {
                DrawOptionsTab();
            }
        }

        // ---------------------------------------------------------------- Rules tab

        private void DrawRulesTab()
        {
            if (_ruleset is null)
            {
                DrawMissingRuleset();
                return;
            }

            if (_rulesetError is object)
            {
                EditorGUILayout.HelpBox(
                    $"{RulesetFile.ProjectPath} could not be parsed and will not be overwritten:\n{_rulesetError}",
                    MessageType.Error);
                return;
            }

            DrawRulesToolbar();
            EditorGUILayout.Space();
            DrawWebGlToggle();
            EditorGUILayout.Space();

            _rulesScroll = EditorGUILayout.BeginScrollView(_rulesScroll);
            foreach (var category in _catalog.upa.GroupBy(rule => rule.category))
            {
                EditorGUILayout.LabelField(category.Key, EditorStyles.boldLabel);
                foreach (var rule in category)
                {
                    DrawRuleRow(rule);
                }

                EditorGUILayout.Space();
            }

            _untFoldout = EditorGUILayout.Foldout(_untFoldout, "Microsoft.Unity.Analyzers (UNT) severities", toggleOnLabelClick: true);
            if (_untFoldout)
            {
                EditorGUILayout.HelpBox(
                    "These rules ship with Visual Studio's Unity integration; this window only manages their severity entries in the ruleset.",
                    MessageType.None);
                EditorGUILayout.LabelField("Correctness", EditorStyles.miniBoldLabel);
                foreach (var id in _catalog.unt.correctness)
                {
                    DrawUntRow(id);
                }

                EditorGUILayout.LabelField("Performance", EditorStyles.miniBoldLabel);
                foreach (var id in _catalog.unt.performance)
                {
                    DrawUntRow(id);
                }

                EditorGUILayout.Space();
            }

            _overridesFoldout = EditorGUILayout.Foldout(_overridesFoldout, $"Per-assembly overrides ({_asmdefOverrides.Count})", toggleOnLabelClick: true);
            if (_overridesFoldout)
            {
                if (_asmdefOverrides.Count == 0)
                {
                    EditorGUILayout.LabelField("No Default.ruleset found inside asmdef folders.", EditorStyles.miniLabel);
                }

                foreach (var (assemblyName, path) in _asmdefOverrides)
                {
                    EditorGUILayout.BeginHorizontal();
                    EditorGUILayout.LabelField(assemblyName, GUILayout.Width(220f));
                    EditorGUILayout.LabelField(path, EditorStyles.miniLabel);
                    if (GUILayout.Button("Open", GUILayout.Width(50f)))
                    {
                        EditorUtility.OpenWithDefaultApp(path);
                    }

                    EditorGUILayout.EndHorizontal();
                }

                if (GUILayout.Button("Rescan", GUILayout.Width(80f)))
                {
                    RefreshAsmdefOverrides();
                }
            }

            EditorGUILayout.EndScrollView();
        }

        private void DrawMissingRuleset()
        {
            EditorGUILayout.HelpBox(
                $"{RulesetFile.ProjectPath} does not exist yet. Severities fall back to each rule's built-in default; create the file from a preset to take control.",
                MessageType.Info);
            EditorGUILayout.BeginHorizontal();
            _presetIndex = EditorGUILayout.Popup("Preset", _presetIndex, s_presetNames);
            if (GUILayout.Button("Create " + RulesetFile.ProjectPath, GUILayout.Width(220f)))
            {
                CreateRulesetFromPreset(s_presetNames[_presetIndex]);
            }

            EditorGUILayout.EndHorizontal();
        }

        private void DrawRulesToolbar()
        {
            EditorGUILayout.BeginHorizontal();
            _presetIndex = EditorGUILayout.Popup(_presetIndex, s_presetNames, GUILayout.Width(140f));
            if (GUILayout.Button("Apply preset", GUILayout.Width(100f)) &&
                EditorUtility.DisplayDialog(
                    "Apply preset",
                    $"Replace the severities of all UPA and UNT rules in {RulesetFile.ProjectPath} with the '{s_presetNames[_presetIndex]}' preset? Other entries and Includes are kept.",
                    "Apply",
                    "Cancel"))
            {
                ApplyPreset(s_presetNames[_presetIndex]);
            }

            GUILayout.FlexibleSpace();
            using (new EditorGUI.DisabledScope(!_rulesDirty))
            {
                if (GUILayout.Button("Revert", GUILayout.Width(70f)))
                {
                    ReloadRuleset();
                }

                if (GUILayout.Button("Save", GUILayout.Width(70f)))
                {
                    SaveRuleset();
                }
            }

            EditorGUILayout.EndHorizontal();
        }

        private void DrawWebGlToggle()
        {
            var everywhere = WebGlTargetSupport.IsEnabledEverywhere();
            var somewhere = WebGlTargetSupport.IsEnabledSomewhere();

            EditorGUI.showMixedValue = somewhere && !everywhere;
            var requested = EditorGUILayout.ToggleLeft(
                new GUIContent(
                    "WebGL rules (UPA3000+)",
                    $"Adds the {WebGlTargetSupport.Define} define to every build target and stacks {WebGlAddonFileName} onto {RulesetFile.ProjectPath}."),
                everywhere);
            EditorGUI.showMixedValue = false;

            if (requested == everywhere && !(somewhere && !everywhere))
            {
                return;
            }

            if (requested)
            {
                EnableWebGl();
            }
            else if (everywhere || somewhere)
            {
                DisableWebGl();
            }
        }

        private void DrawRuleRow(RuleCatalog.RuleRow rule)
        {
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button(new GUIContent(rule.id, rule.title), EditorStyles.linkLabel, GUILayout.Width(70f)))
            {
                Application.OpenURL(rule.helpUri);
            }

            EditorGUILayout.LabelField(new GUIContent(rule.title, rule.title), GUILayout.MinWidth(120f));
            var badge = string.IsNullOrEmpty(rule.condition) ? (rule.hotPath ? "hot path" : "") : rule.condition;
            EditorGUILayout.LabelField(badge, EditorStyles.miniLabel, GUILayout.Width(60f));
            DrawActionPopup(rule.id);
            EditorGUILayout.EndHorizontal();
        }

        private void DrawUntRow(string id)
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(id, GUILayout.Width(70f));
            GUILayout.FlexibleSpace();
            DrawActionPopup(id);
            EditorGUILayout.EndHorizontal();
        }

        private void DrawActionPopup(string ruleId)
        {
            _pendingActions.TryGetValue(ruleId, out var current);
            var labels = s_actionLabels;
            var index = current is null ? 0 : Array.IndexOf(labels, current);
            if (index < 0)
            {
                // Preserve values this window does not offer (e.g. Hidden) until changed.
                labels = labels.Append(current).ToArray();
                index = labels.Length - 1;
            }

            var selected = EditorGUILayout.Popup(index, labels, GUILayout.Width(80f));
            if (selected != index)
            {
                _pendingActions[ruleId] = selected == 0 ? null : labels[selected];
                _rulesDirty = true;
            }
        }

        // ---------------------------------------------------------------- Options tab

        private void DrawOptionsTab()
        {
            EditorGUILayout.HelpBox(
                $"Values are written to {OptionsFile.ProjectPath}, which both Unity builds and the IDE honor (it wins over .editorconfig). Unset rows fall back to .editorconfig, then to built-in defaults.",
                MessageType.None);

            _optionsScroll = EditorGUILayout.BeginScrollView(_optionsScroll);
            foreach (var option in _catalog.options)
            {
                DrawOptionRow(option);
            }

            EditorGUILayout.EndScrollView();

            EditorGUILayout.Space();
            _syncEditorConfig = EditorGUILayout.ToggleLeft(
                new GUIContent(
                    "Also sync values to .editorconfig",
                    "Mirrors the set keys into the project root .editorconfig for toolchains that read options from there."),
                _syncEditorConfig);

            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            using (new EditorGUI.DisabledScope(!_optionsDirty))
            {
                if (GUILayout.Button("Revert", GUILayout.Width(70f)))
                {
                    ReloadOptions();
                }

                if (GUILayout.Button("Save", GUILayout.Width(70f)))
                {
                    SaveOptions();
                }
            }

            EditorGUILayout.EndHorizontal();
        }

        private void DrawOptionRow(RuleCatalog.OptionRow option)
        {
            var isSet = _pendingOptions.TryGetValue(option.key, out var value);

            EditorGUILayout.BeginHorizontal();
            var nowSet = EditorGUILayout.ToggleLeft(
                new GUIContent(option.key, option.description),
                isSet,
                GUILayout.Width(240f));
            if (nowSet != isSet)
            {
                if (nowSet)
                {
                    _pendingOptions[option.key] = value ?? option.@default;
                }
                else
                {
                    _pendingOptions.Remove(option.key);
                }

                _optionsDirty = true;
                isSet = nowSet;
                value = isSet ? _pendingOptions[option.key] : null;
            }

            using (new EditorGUI.DisabledScope(!isSet))
            {
                var display = isSet ? value : option.@default;
                string edited;
                if (option.type == "bool")
                {
                    var boolValue = string.Equals(display, "true", StringComparison.OrdinalIgnoreCase);
                    edited = EditorGUILayout.Toggle(boolValue, GUILayout.Width(20f)) ? "true" : "false";
                    GUILayout.FlexibleSpace();
                }
                else
                {
                    edited = EditorGUILayout.TextField(display);
                }

                if (isSet && edited != value)
                {
                    _pendingOptions[option.key] = edited;
                    _optionsDirty = true;
                }
            }

            EditorGUILayout.EndHorizontal();
        }

        // ---------------------------------------------------------------- actions

        private void ReloadRuleset()
        {
            _ruleset = null;
            _rulesetError = null;
            _pendingActions.Clear();
            _rulesDirty = false;

            if (!File.Exists(RulesetFile.ProjectPath))
            {
                return;
            }

            if (!RulesetFile.TryLoad(RulesetFile.ProjectPath, out var ruleset, out var error))
            {
                _ruleset = ruleset;
                _rulesetError = error;
                return;
            }

            _ruleset = ruleset;
            foreach (var id in ManagedRuleIds())
            {
                var action = ruleset.GetAction(id);
                if (action is object)
                {
                    _pendingActions[id] = action;
                }
            }
        }

        private void SaveRuleset()
        {
            foreach (var id in ManagedRuleIds())
            {
                _pendingActions.TryGetValue(id, out var action);
                _ruleset.SetAction(id, action, AnalyzerIdFor(id));
            }

            _ruleset.Save();
            AssetDatabase.Refresh();
            _rulesDirty = false;
        }

        private void CreateRulesetFromPreset(string presetName)
        {
            var source = PresetPath(presetName);
            if (!File.Exists(source))
            {
                EditorUtility.DisplayDialog("Preset not found", $"Missing preset file:\n{source}", "OK");
                return;
            }

            File.Copy(source, RulesetFile.ProjectPath);
            AssetDatabase.Refresh();
            ReloadRuleset();
        }

        private void ApplyPreset(string presetName)
        {
            if (!RulesetFile.TryLoad(PresetPath(presetName), out var preset, out var error))
            {
                EditorUtility.DisplayDialog("Preset not readable", error, "OK");
                return;
            }

            _ruleset.ApplyPreset(preset, ManagedRuleIds(), AnalyzerIdFor);
            _ruleset.Save();
            AssetDatabase.Refresh();
            ReloadRuleset();
        }

        private void EnableWebGl()
        {
            WebGlTargetSupport.SetDefine(true);

            var addonTarget = Path.Combine("Assets", WebGlAddonFileName);
            if (!File.Exists(addonTarget))
            {
                var addonSource = Path.Combine(RuleCatalog.ResolvePackagePath(), PresetsFolder, WebGlAddonFileName);
                if (File.Exists(addonSource))
                {
                    File.Copy(addonSource, addonTarget);
                }
            }

            _ruleset.AddInclude(WebGlAddonFileName);
            _ruleset.Save();
            AssetDatabase.Refresh();
        }

        private void DisableWebGl()
        {
            WebGlTargetSupport.SetDefine(false);
            _ruleset.RemoveInclude(WebGlAddonFileName);
            _ruleset.Save();
            AssetDatabase.Refresh();
        }

        private void ReloadOptions()
        {
            _optionsFile = OptionsFile.Load(OptionsFile.ProjectPath);
            _pendingOptions.Clear();
            _optionsDirty = false;
            foreach (var option in _catalog.options)
            {
                if (_optionsFile.TryGet(option.key, out var value))
                {
                    _pendingOptions[option.key] = value;
                }
            }
        }

        private void SaveOptions()
        {
            foreach (var option in _catalog.options)
            {
                if (_pendingOptions.TryGetValue(option.key, out var value))
                {
                    _optionsFile.Set(option.key, value);
                }
                else
                {
                    _optionsFile.Remove(option.key);
                }
            }

            _optionsFile.Save();
            if (_syncEditorConfig && _pendingOptions.Count > 0)
            {
                OptionsFile.SyncToEditorConfig(_pendingOptions);
            }

            AssetDatabase.Refresh();
            _optionsDirty = false;
        }

        private void RefreshAsmdefOverrides()
        {
            _asmdefOverrides = Directory
                .GetFiles("Assets", "Default.ruleset", SearchOption.AllDirectories)
                .Select(path => path.Replace('\\', '/'))
                .Where(path => !string.Equals(path, RulesetFile.ProjectPath, StringComparison.OrdinalIgnoreCase))
                .Select(path =>
                {
                    var asmdef = Directory
                        .GetFiles(Path.GetDirectoryName(path), "*.asmdef", SearchOption.TopDirectoryOnly)
                        .FirstOrDefault();
                    var assemblyName = asmdef is null
                        ? "(no asmdef in folder)"
                        : Path.GetFileNameWithoutExtension(asmdef);
                    return (assemblyName, path);
                })
                .OrderBy(entry => entry.path, StringComparer.Ordinal)
                .ToList();
        }

        private List<string> ManagedRuleIds()
        {
            return _catalog.upa.Select(rule => rule.id)
                .Concat(_catalog.unt.correctness)
                .Concat(_catalog.unt.performance)
                .ToList();
        }

        private static string AnalyzerIdFor(string ruleId)
        {
            return ruleId.StartsWith("UNT", StringComparison.Ordinal)
                ? RulesetFile.UntAnalyzerId
                : RulesetFile.UpaAnalyzerId;
        }

        private static string PresetPath(string presetName)
        {
            return Path.Combine(RuleCatalog.ResolvePackagePath(), PresetsFolder, presetName + ".ruleset");
        }
    }
}
