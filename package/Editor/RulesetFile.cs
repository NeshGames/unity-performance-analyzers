using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace NeshGames.UnityPerformanceAnalyzers.Editor
{
    /// <summary>
    /// Read/write access to a .ruleset file that only ever touches what it understands:
    /// Rule entries are added, changed or removed per ID inside their AnalyzerId group,
    /// while Include elements, comments and rule entries of other analyzers are preserved
    /// verbatim. "Default" in the window means "no explicit entry" — an absent Rule row
    /// lets the analyzer default (or an Include) apply, which the WebGL add-on relies on.
    /// </summary>
    internal sealed class RulesetFile
    {
        public const string ProjectPath = "Assets/Default.ruleset";
        public const string UpaAnalyzerId = "UnityPerformanceAnalyzers";
        public const string UntAnalyzerId = "Microsoft.Unity.Analyzers";

        private readonly XDocument _document;

        public string Path { get; }

        private RulesetFile(string path, XDocument document)
        {
            Path = path;
            _document = document;
        }

        public static bool TryLoad(string path, out RulesetFile file, out string error)
        {
            file = null;
            error = null;
            if (!File.Exists(path))
            {
                error = "File does not exist.";
                return false;
            }

            try
            {
                var document = XDocument.Load(path);
                if (document.Root?.Name.LocalName != "RuleSet")
                {
                    error = "Not a ruleset file (missing RuleSet root element).";
                    return false;
                }

                file = new RulesetFile(path, document);
                return true;
            }
            catch (Exception exception)
            {
                error = exception.Message;
                return false;
            }
        }

        public static RulesetFile CreateNew(string path, string name)
        {
            var document = new XDocument(
                new XDeclaration("1.0", "utf-8", null),
                new XElement("RuleSet",
                    new XAttribute("Name", name),
                    new XAttribute("ToolsVersion", "10.0")));
            return new RulesetFile(path, document);
        }

        public string GetAction(string ruleId)
        {
            var rule = FindRule(ruleId);
            return rule?.Attribute("Action")?.Value;
        }

        public void SetAction(string ruleId, string action, string analyzerId)
        {
            var rule = FindRule(ruleId);
            if (action is null)
            {
                rule?.Remove();
                return;
            }

            if (rule is object)
            {
                rule.SetAttributeValue("Action", action);
                return;
            }

            var group = FindOrCreateGroup(analyzerId);
            group.Add(new XElement("Rule",
                new XAttribute("Id", ruleId),
                new XAttribute("Action", action)));
        }

        public bool HasInclude(string includePath)
        {
            return _document.Root.Elements("Include")
                .Any(include => string.Equals(include.Attribute("Path")?.Value, includePath, StringComparison.OrdinalIgnoreCase));
        }

        public void AddInclude(string includePath)
        {
            if (HasInclude(includePath))
            {
                return;
            }

            // Includes conventionally sit before the Rules groups.
            var element = new XElement("Include",
                new XAttribute("Path", includePath),
                new XAttribute("Action", "Default"));
            var firstRules = _document.Root.Elements("Rules").FirstOrDefault();
            if (firstRules is object)
            {
                firstRules.AddBeforeSelf(element);
            }
            else
            {
                _document.Root.Add(element);
            }
        }

        public void RemoveInclude(string includePath)
        {
            _document.Root.Elements("Include")
                .Where(include => string.Equals(include.Attribute("Path")?.Value, includePath, StringComparison.OrdinalIgnoreCase))
                .ToList()
                .ForEach(include => include.Remove());
        }

        /// <summary>
        /// Replaces the entries of every rule ID in <paramref name="managedIds"/> with the
        /// preset's entries for those IDs. Entries for unmanaged IDs, Includes and comments
        /// stay untouched.
        /// </summary>
        public void ApplyPreset(RulesetFile preset, IReadOnlyCollection<string> managedIds, Func<string, string> analyzerIdForRule)
        {
            foreach (var id in managedIds)
            {
                SetAction(id, preset.GetAction(id), analyzerIdForRule(id));
            }
        }

        public void Save()
        {
            // Stable ordering keeps diffs reviewable when the window rewrites the file.
            foreach (var group in _document.Root.Elements("Rules"))
            {
                var ordered = group.Elements("Rule")
                    .OrderBy(rule => rule.Attribute("Id")?.Value, StringComparer.Ordinal)
                    .ToList();
                group.Elements("Rule").Remove();
                group.Add(ordered);
            }

            // Drop Rules groups the edits emptied out.
            _document.Root.Elements("Rules")
                .Where(group => !group.HasElements)
                .ToList()
                .ForEach(group => group.Remove());

            _document.Save(Path);
        }

        private XElement FindRule(string ruleId)
        {
            return _document.Root.Elements("Rules")
                .SelectMany(group => group.Elements("Rule"))
                .FirstOrDefault(rule => rule.Attribute("Id")?.Value == ruleId);
        }

        private XElement FindOrCreateGroup(string analyzerId)
        {
            var group = _document.Root.Elements("Rules")
                .FirstOrDefault(candidate => candidate.Attribute("AnalyzerId")?.Value == analyzerId);
            if (group is null)
            {
                group = new XElement("Rules",
                    new XAttribute("AnalyzerId", analyzerId),
                    new XAttribute("RuleNamespace", analyzerId));
                _document.Root.Add(group);
            }

            return group;
        }
    }
}
