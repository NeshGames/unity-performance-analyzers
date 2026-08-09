using System.Collections.Immutable;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace UnityPerformanceAnalyzers.Cli;

/// <summary>
/// What identifies one violation across runs. Deliberately without a line number: a violation
/// that moved down ten lines is the same violation, and re-reporting it would defeat the point
/// of freezing existing debt.
/// </summary>
internal sealed record BaselineKey(
    string File,
    string Rule,
    string Type,
    string Member,
    string Snippet);

/// <summary>One line of the contract: a key and how many times it occurred.</summary>
internal sealed record BaselineEntry(BaselineKey Key, int Count);

/// <summary>
/// A baseline as stored. Plain text rather than hashes so the contract can be read, diffed and
/// reviewed — otherwise it is a blob nobody dares touch.
/// </summary>
internal sealed class BaselineDocument
{
    /// <summary>Raised only when an existing field changes meaning; new fields do not bump it.</summary>
    public const int SchemaVersion = 1;

    /// <summary>Guards against overflow and against a hostile file claiming absurd quotas.</summary>
    private const int MaxCount = 1_000_000;

    public BaselineDocument(ImmutableArray<BaselineEntry> entries)
    {
        Entries = entries;
    }

    public ImmutableArray<BaselineEntry> Entries { get; }

    /// <summary>Quota per key, for matching.</summary>
    public ImmutableDictionary<BaselineKey, int> Counts =>
        Entries.ToImmutableDictionary(e => e.Key, e => e.Count);

    /// <summary>
    /// Reads and validates. Every failure below is a usage error, because the alternative —
    /// carrying on with a file that could not be trusted — suppresses either too much or too
    /// little while looking entirely normal.
    /// </summary>
    public static BaselineDocument Read(string path)
    {
        if (!File.Exists(path))
        {
            // Not an empty baseline: a typo would then suppress nothing at all, and a run that
            // suppresses nothing is indistinguishable from one with nothing to suppress.
            throw new CliException($"--baseline file not found: {path}");
        }

        var bytes = File.ReadAllBytes(path);
        JsonDocument document;
        try
        {
            RejectDuplicateProperties(path, bytes);
            document = JsonDocument.Parse(bytes);
        }
        catch (JsonException ex)
        {
            throw new CliException($"{path}: not valid JSON - {ex.Message}");
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                throw new CliException($"{path}: expected a JSON object at the root.");
            }

            ValidateSchemaVersion(path, root);

            if (!root.TryGetProperty("entries", out var entries)
                || entries.ValueKind != JsonValueKind.Array)
            {
                throw new CliException($"{path}: 'entries' is missing or not an array.");
            }

            var parsed = ImmutableArray.CreateBuilder<BaselineEntry>();
            var seen = new HashSet<BaselineKey>();

            foreach (var element in entries.EnumerateArray())
            {
                var entry = ReadEntry(path, element);
                if (!seen.Add(entry.Key))
                {
                    // Neither summing nor last-wins: both silently over-suppress, and which one
                    // an implementation picks would not be visible in any output.
                    throw new CliException(
                        $"{path}: duplicate entry for {entry.Key.File} {entry.Key.Rule} "
                        + $"{entry.Key.Type}.{entry.Key.Member}. Each key must appear once.");
                }

                parsed.Add(entry);
            }

            return new BaselineDocument(parsed.ToImmutable());
        }
    }

    /// <summary>
    /// Rejects a repeated property name inside one object. Parsers differ on which one wins,
    /// so the same file would suppress differently depending on the runtime that read it —
    /// and every one of those outcomes looks like a normal run.
    /// </summary>
    private static void RejectDuplicateProperties(string path, byte[] bytes)
    {
        var reader = new Utf8JsonReader(bytes);
        var scopes = new Stack<HashSet<string>>();

        while (reader.Read())
        {
            switch (reader.TokenType)
            {
                case JsonTokenType.StartObject:
                    scopes.Push(new HashSet<string>(StringComparer.Ordinal));
                    break;
                case JsonTokenType.EndObject:
                    if (scopes.Count > 0)
                    {
                        scopes.Pop();
                    }

                    break;
                case JsonTokenType.PropertyName when scopes.Count > 0:
                    var name = reader.GetString() ?? string.Empty;
                    if (!scopes.Peek().Add(name))
                    {
                        throw new CliException(
                            $"{path}: property '{name}' appears twice in one object.");
                    }

                    break;
            }
        }
    }

    private static void ValidateSchemaVersion(string path, JsonElement root)
    {
        if (!root.TryGetProperty("schemaVersion", out var version)
            || version.ValueKind != JsonValueKind.Number
            || !version.TryGetInt32(out var value)
            || value < 1)
        {
            throw new CliException($"{path}: 'schemaVersion' must be a positive integer.");
        }

        if (value > SchemaVersion)
        {
            throw new CliException(
                $"{path}: schemaVersion {value} is newer than this tool supports "
                + $"({SchemaVersion}). Upgrade upa-cli.");
        }
    }

    private static BaselineEntry ReadEntry(string path, JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            throw new CliException($"{path}: every entry must be a JSON object.");
        }

        // Unknown fields are ignored - the one lenient rule, so a later version can add a
        // field without invalidating baselines already committed.
        var file = RequiredString(path, element, "file");
        var rule = RequiredString(path, element, "rule");
        var type = RequiredString(path, element, "type", allowEmpty: true);
        var member = RequiredString(path, element, "member", allowEmpty: true);
        var snippet = RequiredString(path, element, "snippet");

        if (!BaselinePath.IsCanonical(file))
        {
            throw new CliException(
                $"{path}: 'file' must be a normalized relative path with '/' separators "
                + $"and no '.' or '..' segments, got '{file}'.");
        }

        if (!element.TryGetProperty("count", out var count)
            || count.ValueKind != JsonValueKind.Number
            || !count.TryGetInt32(out var value)
            || value < 1
            || value > MaxCount)
        {
            throw new CliException(
                $"{path}: 'count' for {file} must be an integer between 1 and {MaxCount}.");
        }

        return new BaselineEntry(new BaselineKey(file, rule, type, member, snippet), value);
    }

    private static string RequiredString(
        string path,
        JsonElement element,
        string name,
        bool allowEmpty = false)
    {
        if (!element.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String)
        {
            throw new CliException($"{path}: every entry needs a string '{name}'.");
        }

        var text = value.GetString() ?? string.Empty;
        if (!allowEmpty && text.Length == 0)
        {
            throw new CliException($"{path}: '{name}' must not be empty.");
        }

        return text;
    }

    /// <summary>
    /// The file's exact bytes. Sorted so regenerating an unchanged baseline produces an
    /// unchanged file, which is what makes it reviewable in a diff.
    /// </summary>
    public byte[] Serialize()
    {
        var text = new StringBuilder();
        text.Append("{\n");
        text.Append($"  \"schemaVersion\": {SchemaVersion},\n");
        text.Append($"  \"toolVersion\": {Quote(AnalyzerCatalog.ToolVersion)},\n");
        text.Append("  \"entries\": [\n");

        var ordered = Entries
            .OrderBy(e => e.Key.File, StringComparer.Ordinal)
            .ThenBy(e => e.Key.Rule, StringComparer.Ordinal)
            .ThenBy(e => e.Key.Type, StringComparer.Ordinal)
            .ThenBy(e => e.Key.Member, StringComparer.Ordinal)
            .ThenBy(e => e.Key.Snippet, StringComparer.Ordinal)
            .ToArray();

        for (var i = 0; i < ordered.Length; i++)
        {
            var entry = ordered[i];
            text.Append("    {\n");
            text.Append($"      \"file\": {Quote(entry.Key.File)},\n");
            text.Append($"      \"rule\": {Quote(entry.Key.Rule)},\n");
            text.Append($"      \"type\": {Quote(entry.Key.Type)},\n");
            text.Append($"      \"member\": {Quote(entry.Key.Member)},\n");
            text.Append($"      \"snippet\": {Quote(entry.Key.Snippet)},\n");
            text.Append($"      \"count\": {entry.Count}\n");
            text.Append(i == ordered.Length - 1 ? "    }\n" : "    },\n");
        }

        text.Append("  ]\n");
        text.Append("}\n");

        // UTF-8 without a BOM, newlines as \n, on every platform: the file is committed and
        // read back elsewhere, and a byte-level difference between platforms would show up as
        // a diff on every regeneration.
        return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(text.ToString());
    }

    // Relaxed escaping so the file stays readable. The default encoder writes &lt; &gt; &amp; and
    // every non-ASCII character as a \uXXXX sequence, which turns a snippet of ordinary C# into
    // something nobody can review in a diff - and being reviewable is the whole reason this
    // format is plain text rather than hashes.
    private static readonly JsonSerializerOptions s_quoting = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private static string Quote(string value) => JsonSerializer.Serialize(value, s_quoting);
}
