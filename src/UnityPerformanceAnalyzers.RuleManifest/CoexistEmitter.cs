using System.Text;

namespace UnityPerformanceAnalyzers.RuleManifest;

/// <summary>
/// Writes the coexistence files: for each tool this package overlaps with, a ruleset that
/// defers the overlapping rules to it.
/// </summary>
/// <remarks>
/// <para>
/// <b>The include direction is the whole design.</b> A rule entry in the including file beats
/// the same entry in an included file. A coexistence file meant to be <em>included by</em> a
/// base preset therefore cannot silence anything that preset already grades — and every base
/// preset here grades every rule. It would produce three files that look correct and do
/// nothing, which is the worst available outcome: the team believes the duplicate reports
/// were dealt with, and they were not.
/// </para>
/// <para>
/// Measured before this was written, with this repository's own CLI. A base ruleset grading
/// UPA0001 as Warning and including an overlay that sets it to None still reported UPA0001.
/// Inverting it — the coexistence file as parent, including the base — silenced it. So the
/// generated file is the parent.
/// </para>
/// </remarks>
public static class CoexistEmitter
{
    /// <summary>Writes one ruleset per overlay into the preset directory.</summary>
    public static IReadOnlyList<string> Write(string presetDirectory)
    {
        var written = new List<string>();

        foreach (var coexist in PresetTable.Coexists)
        {
            var ruleset = Path.Combine(presetDirectory, coexist.Name + "-coexist.ruleset");
            File.WriteAllText(ruleset, Ruleset(coexist));
            written.Add(ruleset);
        }

        return written;
    }

    private static string Ruleset(PresetTable.Coexist coexist)
    {
        var quote = '"';
        var sb = new StringBuilder();

        sb.Append("<?xml version=" + quote + "1.0" + quote + " encoding=" + quote + "utf-8" + quote + "?>\n");
        sb.Append("<!-- unity-performance-analyzers coexistence ruleset: ");
        sb.Append(coexist.Rules.Length == 0
            ? "nothing is deferred to " + coexist.Defers + ".\n\n"
            : "defers to " + coexist.Defers + ".\n\n");
        sb.Append("     Copy this file and ").Append(coexist.Base)
          .Append(".ruleset into Assets/, then rename\n");
        sb.Append("     this one Default.ruleset. To defer from a different base preset, change the\n");
        sb.Append("     Include path below to another preset name.\n\n");
        sb.Append("     The base is included from here rather than the reverse because the including\n");
        sb.Append("     file wins: an entry in a preset overrides the same entry in a file that preset\n");
        sb.Append("     includes, so the disables have to sit in the parent to take effect.\n\n");

        foreach (var line in Wrap(coexist.Caveat))
        {
            sb.Append("     ").Append(line).Append('\n');
        }

        sb.Append(GeneratedNotice("     "));
        sb.Append("<RuleSet Name=" + quote + "UPA " + coexist.Name + "-coexist" + quote
            + " ToolsVersion=" + quote + "10.0" + quote + ">\n");
        sb.Append("  <Include Path=" + quote + coexist.Base + ".ruleset" + quote
            + " Action=" + quote + "Default" + quote + " />\n");
        sb.Append("  <Rules AnalyzerId=" + quote + "UnityPerformanceAnalyzers" + quote
            + " RuleNamespace=" + quote + "UnityPerformanceAnalyzers" + quote + ">\n");

        foreach (var (id, why) in coexist.Rules)
        {
            sb.Append("    <!-- ").Append(why).Append(" -->\n");
            sb.Append("    <Rule Id=" + quote + id + quote + " Action=" + quote + "None" + quote + " />\n");
        }

        sb.Append("  </Rules>\n");
        sb.Append("</RuleSet>\n");
        return sb.ToString();
    }

    /// <summary>Wraps a caveat to comment width so the generated files stay readable.</summary>
    private static IEnumerable<string> Wrap(string text, int width = 74)
    {
        var line = new StringBuilder();

        foreach (var word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (line.Length > 0 && line.Length + 1 + word.Length > width)
            {
                yield return line.ToString();
                line.Clear();
            }

            if (line.Length > 0)
            {
                line.Append(' ');
            }

            line.Append(word);
        }

        if (line.Length > 0)
        {
            yield return line.ToString();
        }
    }

    // XML comments must not contain a double hyphen, so the notice spells the regeneration
    // command without literal option syntax.
    private static string GeneratedNotice(string prefix)
    {
        var line1 = prefix + "GENERATED FILE - do not edit. The rule list lives in PresetTable.cs;";
        var line2 = prefix + PresetEmitter.OwnershipMarker + ".";
        var closing = prefix.TrimEnd().StartsWith("#", StringComparison.Ordinal) ? "\n" : " -->\n";
        return line1 + "\n" + line2 + closing;
    }
}
