using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace NeshGames.UnityPerformanceAnalyzers.Editor
{
    /// <summary>
    /// Read/write access to the universal options file. The file is line-oriented
    /// (key = value, # comments); the window only rewrites lines whose key it is setting
    /// and preserves everything else — user comments, unknown keys, ordering — mirroring
    /// the analyzer's tolerant parser. The analyzer takes the last occurrence of a
    /// duplicated key, so Set replaces the last occurrence too.
    /// </summary>
    internal sealed class OptionsFile
    {
        public const string ProjectPath = "Assets/Rules.UnityPerformanceAnalyzers.additionalfile";

        private const string NewFileHeader =
            "# Unity Performance Analyzers options.\n" +
            "# key = value, one per line.\n" +
            "# Unity builds and upa-cli (--additionalfile) both read this file; it wins over .editorconfig.\n";

        private readonly List<string> _lines;

        public string Path { get; }

        private OptionsFile(string path, List<string> lines)
        {
            Path = path;
            _lines = lines;
        }

        public static OptionsFile Load(string path)
        {
            var lines = File.Exists(path)
                ? File.ReadAllLines(path).ToList()
                : NewFileHeader.Split('\n').ToList();
            return new OptionsFile(path, lines);
        }

        public bool TryGet(string key, out string value)
        {
            value = null;
            var index = LastIndexOf(key);
            if (index < 0)
            {
                return false;
            }

            var line = _lines[index];
            value = line.Substring(line.IndexOf('=') + 1).Trim();
            return true;
        }

        public void Set(string key, string value)
        {
            var index = LastIndexOf(key);
            if (index >= 0)
            {
                _lines[index] = $"{key} = {value}";
            }
            else
            {
                if (_lines.Count > 0 && _lines[_lines.Count - 1].Trim().Length == 0)
                {
                    _lines.RemoveAt(_lines.Count - 1);
                }

                _lines.Add($"{key} = {value}");
            }
        }

        public void Remove(string key)
        {
            for (var i = _lines.Count - 1; i >= 0; i--)
            {
                if (KeyOf(_lines[i]) is string lineKey &&
                    string.Equals(lineKey, key, StringComparison.OrdinalIgnoreCase))
                {
                    _lines.RemoveAt(i);
                }
            }
        }

        public void Save()
        {
            File.WriteAllText(Path, string.Join("\n", _lines).TrimEnd('\n') + "\n");
        }

        private int LastIndexOf(string key)
        {
            for (var i = _lines.Count - 1; i >= 0; i--)
            {
                if (KeyOf(_lines[i]) is string lineKey &&
                    string.Equals(lineKey, key, StringComparison.OrdinalIgnoreCase))
                {
                    return i;
                }
            }

            return -1;
        }

        private static string KeyOf(string line)
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed[0] == '#')
            {
                return null;
            }

            var separator = trimmed.IndexOf('=');
            if (separator <= 0)
            {
                return null;
            }

            var key = trimmed.Substring(0, separator).Trim();
            return key.Length == 0 ? null : key;
        }
    }
}
