using System;
using System.Collections.Immutable;
using System.IO;
using System.Runtime.CompilerServices;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

namespace UnityPerformanceAnalyzers
{
    /// <summary>
    /// Layered lookup for every analyzer option: the universal options file wins over
    /// .editorconfig, which wins over the built-in default, decided per key. Unity passes
    /// additional files to the compiler but never .editorconfig, so the options file is what
    /// makes configuration effective in Unity builds; .editorconfig remains the fallback for
    /// toolchains that pass one (upa-cli --editorconfig, dotnet build). Parsing never throws
    /// or reports: malformed lines and unknown keys are skipped, an invalid value counts as
    /// unset for its key and falls through to the next layer, and a duplicated key keeps its
    /// last value.
    /// </summary>
    internal sealed class UpaOptions
    {
        internal const string FileName = "Rules.UnityPerformanceAnalyzers.additionalfile";

        private static readonly UpaOptions s_empty =
            new UpaOptions(ImmutableDictionary.Create<string, string>(StringComparer.OrdinalIgnoreCase));

        private readonly ImmutableDictionary<string, string> _values;

        private UpaOptions(ImmutableDictionary<string, string> values)
        {
            _values = values;
        }

        // Keyed on the SourceText, never on the file path or the compilation. A SourceText is
        // immutable, so the parse of one can never go stale: an edited options file arrives as
        // a different SourceText and misses. The table holds its keys weakly, so it keeps no
        // text alive that the host has let go of. What it buys is that every analyzer in the
        // package shares one parse per compilation whenever the host hands each of them the
        // same text instance (csc does); a host that hands out a fresh instance per call just
        // misses, which costs the parse and nothing else.
        private static readonly ConditionalWeakTable<SourceText, UpaOptions> s_parsed =
            new ConditionalWeakTable<SourceText, UpaOptions>();

        private static readonly ConditionalWeakTable<SourceText, UpaOptions>.CreateValueCallback s_parse = Parse;

        /// <summary>
        /// Finds and parses the options file. Call once per compilation - through
        /// <see cref="UpaCompilationContext.Settings"/>, which does exactly that - and pass the
        /// instance to the node actions. When several additional files carry the expected
        /// name, the lowest path in ordinal order wins, so the choice is deterministic across
        /// platforms.
        /// </summary>
        public static UpaOptions Resolve(AnalyzerOptions options)
        {
            AdditionalText? file = null;
            foreach (var candidate in options.AdditionalFiles)
            {
                if (!string.Equals(Path.GetFileName(candidate.Path), FileName, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (file is null || string.CompareOrdinal(candidate.Path, file.Path) < 0)
                {
                    file = candidate;
                }
            }

            var text = file?.GetText();
            return text is null ? s_empty : s_parsed.GetValue(text, s_parse);
        }

        private static UpaOptions Parse(SourceText text)
        {
            var builder = ImmutableDictionary.CreateBuilder<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var line in text.Lines)
            {
                var raw = line.ToString().Trim();
                if (raw.Length == 0 || raw[0] == '#')
                {
                    continue;
                }

                var separator = raw.IndexOf('=');
                if (separator <= 0)
                {
                    continue;
                }

                var key = raw.Substring(0, separator).Trim();
                if (key.Length == 0)
                {
                    continue;
                }

                builder[key] = raw.Substring(separator + 1).Trim();
            }

            return builder.Count == 0 ? s_empty : new UpaOptions(builder.ToImmutable());
        }

        public bool GetBool(string key, SyntaxTree? tree, AnalyzerConfigOptionsProvider provider, bool fallback)
        {
            if (_values.TryGetValue(key, out var raw) && bool.TryParse(raw, out var parsed))
            {
                return parsed;
            }

            if (tree is object &&
                provider.GetOptions(tree).TryGetValue(key, out var configRaw) &&
                bool.TryParse(configRaw.Trim(), out var configParsed))
            {
                return configParsed;
            }

            return fallback;
        }

        /// <summary>
        /// Comma-separated list value. A value that parses to zero items (only commas or
        /// whitespace) is invalid and falls through, so an empty list can never mask a
        /// configured lower layer.
        /// </summary>
        public ImmutableArray<string> GetList(string key, SyntaxTree? tree, AnalyzerConfigOptionsProvider provider, ImmutableArray<string> fallback)
        {
            if (_values.TryGetValue(key, out var raw) && TryParseList(raw, out var parsed))
            {
                return parsed;
            }

            if (tree is object &&
                provider.GetOptions(tree).TryGetValue(key, out var configRaw) &&
                TryParseList(configRaw, out var configParsed))
            {
                return configParsed;
            }

            return fallback;
        }

        private static bool TryParseList(string raw, out ImmutableArray<string> list)
        {
            var builder = ImmutableArray.CreateBuilder<string>();
            foreach (var part in raw.Split(','))
            {
                var item = part.Trim();
                if (item.Length > 0)
                {
                    builder.Add(item);
                }
            }

            list = builder.ToImmutable();
            return list.Length > 0;
        }
    }
}
