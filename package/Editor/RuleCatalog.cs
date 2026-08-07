using System;
using System.IO;
using UnityEngine;

namespace NeshGames.UnityPerformanceAnalyzers.Editor
{
    /// <summary>
    /// The rule catalog shipped as Editor/rules.json — generated from the analyzer assembly
    /// at release time, so the window never has to load the analyzer (and its Roslyn
    /// dependencies) into the Editor domain.
    /// </summary>
    [Serializable]
    internal sealed class RuleCatalog
    {
        public string version;
        public RuleRow[] upa;
        public UntGroups unt;
        public OptionRow[] options;

        [Serializable]
        internal sealed class RuleRow
        {
            public string id;
            public string title;
            public string category;
            public string defaultSeverity;
            public bool enabledByDefault;
            public bool hotPath;
            public string condition;
            public string helpUri;
        }

        [Serializable]
        internal sealed class UntGroups
        {
            public string[] correctness;
            public string[] performance;
        }

        [Serializable]
        internal sealed class OptionRow
        {
            public string key;
            public string type;
            public string @default;
            public string description;
        }

        /// <summary>Absolute path of the installed package, resolved from this assembly.</summary>
        public static string ResolvePackagePath()
        {
            var package = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(RuleCatalog).Assembly);
            return package?.resolvedPath;
        }

        public static RuleCatalog Load(out string error)
        {
            var packagePath = ResolvePackagePath();
            if (string.IsNullOrEmpty(packagePath))
            {
                error = "Could not resolve the package installation path.";
                return null;
            }

            var catalogPath = Path.Combine(packagePath, "Editor", "rules.json");
            if (!File.Exists(catalogPath))
            {
                error = $"Rule catalog not found: {catalogPath}";
                return null;
            }

            try
            {
                var catalog = JsonUtility.FromJson<RuleCatalog>(File.ReadAllText(catalogPath));
                if (catalog?.upa is null || catalog.upa.Length == 0)
                {
                    error = "Rule catalog is empty or malformed.";
                    return null;
                }

                error = null;
                return catalog;
            }
            catch (Exception exception)
            {
                error = $"Failed to parse the rule catalog: {exception.Message}";
                return null;
            }
        }
    }
}
