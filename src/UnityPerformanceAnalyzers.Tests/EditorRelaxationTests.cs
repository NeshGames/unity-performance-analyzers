using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;

using Microsoft.CodeAnalysis;

using UnityPerformanceAnalyzers.RuleManifest;

using Xunit;

namespace UnityPerformanceAnalyzers.Tests;

/// <summary>
/// The `[**/Editor/**.cs]` section every main preset carries, tested by resolving severities
/// through Roslyn's own <see cref="AnalyzerConfigSet"/> rather than by matching text.
/// </summary>
/// <remarks>
/// Text assertions would not have caught what these do. Through 0.8.2 the section was written
/// <c>[**/Editor/**/*.cs]</c> with a <c>category-Performance</c> line, and it relaxed nothing
/// at all for two independent reasons — the glob misses files sitting directly in an Editor
/// folder, and severity by rule id outranks severity by category however specific the section
/// is. Both spellings look right; only the resolved severity tells them apart.
/// </remarks>
public class EditorRelaxationTests
{
    private const string EditorFile = @"C:\proj\Assets\Scripts\Tools\Editor\ThingEditor.cs";
    private const string NestedEditorFile = @"C:\proj\Assets\Editor\Windows\ThingWindow.cs";
    private const string RuntimeFile = @"C:\proj\Assets\Scripts\Gameplay\Thing.cs";

    private static ReportDiagnostic Resolve(string presetName, string file, string ruleId)
    {
        var text = File.ReadAllText(PresetPath(presetName));
        var config = AnalyzerConfig.Parse(text, @"C:\proj\.editorconfig");
        var set = AnalyzerConfigSet.Create(ImmutableArray.Create(config));
        var options = set.GetOptionsForSourcePath(file);
        return options.TreeOptions.TryGetValue(ruleId, out var severity)
            ? severity
            : ReportDiagnostic.Default;
    }

    /// <summary>
    /// Anchored on the test assembly's own location, not the working directory. Other tests in
    /// this project call Directory.SetCurrentDirectory, xUnit runs collections in parallel, and
    /// a walk that starts from the working directory therefore finds the repository root only
    /// when the scheduling happens to cooperate. It did, until it did not.
    /// </summary>
    private static string PresetPath(string name)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "package")))
        {
            dir = dir.Parent;
        }

        Assert.NotNull(dir);
        return Path.Combine(dir!.FullName, "package", "Samples~", "Ruleset Presets", name + ".editorconfig");
    }

    [Theory]
    [InlineData("recommended")]
    [InlineData("strict")]
    [InlineData("cysharp-stack")]
    public void PerformanceRule_IsRelaxed_ForFileDirectlyInEditorFolder(string preset)
    {
        // UPA0010 is graded in every one of these presets, so a Default here would mean the
        // section did not apply rather than that the rule was already off.
        Assert.Equal(ReportDiagnostic.Info, Resolve(preset, EditorFile, "UPA0010"));
    }

    [Theory]
    [InlineData("recommended")]
    [InlineData("strict")]
    [InlineData("cysharp-stack")]
    public void PerformanceRule_IsRelaxed_ForFileInEditorSubfolder(string preset)
    {
        Assert.Equal(ReportDiagnostic.Info, Resolve(preset, NestedEditorFile, "UPA0010"));
    }

    [Fact]
    public void RuntimeCode_KeepsItsSeverity()
    {
        Assert.Equal(ReportDiagnostic.Warn, Resolve("recommended", RuntimeFile, "UPA0010"));
    }

    [Fact]
    public void CorrectnessRule_IsNotRelaxed_InEditorCode()
    {
        // Being in editor code does not make a non-exhaustive enum switch correct. UPA1001 is
        // absent from the preset table entirely, so it must resolve to Default in both places.
        Assert.Equal(ReportDiagnostic.Default, Resolve("recommended", EditorFile, "UPA1001"));
        Assert.Equal(ReportDiagnostic.Default, Resolve("recommended", RuntimeFile, "UPA1001"));
    }

    [Fact]
    public void DeclarationRule_IsNotRelaxed_InEditorCode()
    {
        // A struct used as a dictionary key is just as wrong in an editor window.
        Assert.Equal(ReportDiagnostic.Warn, Resolve("recommended", EditorFile, "UPA0028"));
    }

    /// <summary>
    /// The relaxation follows each rule's declared claim, not its diagnostic category, and
    /// these two are where the difference shows. Both are categorised Performance and both
    /// report a defect — UPA0019 because Unity reads a boxed yield as null, UPA0028 because a
    /// struct key is wrong wherever it is declared. A generator that asked the category
    /// downgraded UPA0019 here while the analyzer went on reporting it, which is one rule with
    /// two answers and nothing in either output to say so.
    /// </summary>
    [Theory]
    [InlineData("recommended", "UPA0019")]
    [InlineData("strict", "UPA0019")]
    [InlineData("cysharp-stack", "UPA0019")]
    [InlineData("recommended", "UPA0028")]
    [InlineData("strict", "UPA0028")]
    [InlineData("cysharp-stack", "UPA0028")]
    public void RulesClaimingCorrectness_KeepTheirSeverity_InEditorCode(string preset, string ruleId)
    {
        Assert.NotEqual(ReportDiagnostic.Info, Resolve(preset, EditorFile, ruleId));
        Assert.NotEqual(ReportDiagnostic.Suppress, Resolve(preset, EditorFile, ruleId));
        Assert.Equal(Resolve(preset, RuntimeFile, ruleId), Resolve(preset, EditorFile, ruleId));
    }

    [Fact]
    public void RelaxationNeverRaisesASeverity()
    {
        // UPA0011 is off in `recommended`. Relaxing must leave it off, not lift it to
        // suggestion — which is what writing every performance rule unconditionally would do.
        Assert.Equal(ReportDiagnostic.Suppress, Resolve("recommended", EditorFile, "UPA0011"));
    }

    [Fact]
    public void MinimalPreset_RelaxesNothing_BecauseNothingIsOn()
    {
        Assert.Equal(ReportDiagnostic.Suppress, Resolve("minimal", EditorFile, "UPA0010"));
    }

    [Fact]
    public void EveryMainPreset_SilencesDebugLogging_InEditorCode()
    {
        foreach (var preset in new[] { "minimal", "recommended", "strict", "cysharp-stack" })
        {
            Assert.Equal(ReportDiagnostic.Suppress, Resolve(preset, EditorFile, "UPA0005"));
        }
    }

    [Fact]
    public void NoMainPreset_UsesACategoryKeyForTheEditorSection()
    {
        // The category key cannot win against the per-rule entries in [*.cs]. Keeping it out
        // is what stops the section from silently going inert again.
        foreach (var preset in new[] { "minimal", "recommended", "strict", "cysharp-stack" })
        {
            var editorSection = File.ReadAllText(PresetPath(preset))
                .Split("[**/Editor/", 2)
                .Last();
            Assert.DoesNotContain("dotnet_analyzer_diagnostic.category-", editorSection);
        }
    }
}
