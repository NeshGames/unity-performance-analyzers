using System.Collections.Generic;

namespace UnityPerformanceAnalyzers
{
    /// <summary>How an option's value is read.</summary>
    public enum UpaOptionKind
    {
        /// <summary><c>true</c> or <c>false</c>; anything else counts as unset.</summary>
        Bool,

        /// <summary>Comma-separated names. An empty list counts as unset, so it can never
        /// mask a configured lower layer.</summary>
        List,
    }

    /// <summary>One configurable option, as the analyzers understand it.</summary>
    public sealed class UpaOptionDefinition
    {
        /// <summary>Creates a definition.</summary>
        public UpaOptionDefinition(string key, UpaOptionKind kind, string defaultValue, string description)
        {
            Key = key;
            Kind = kind;
            Default = defaultValue;
            Description = description;
        }

        /// <summary>The key, as written in the options file and in <c>.editorconfig</c>.</summary>
        public string Key { get; }

        /// <summary>How the value is parsed.</summary>
        public UpaOptionKind Kind { get; }

        /// <summary>What applies when no layer sets it.</summary>
        public string Default { get; }

        /// <summary>One line, for the generated catalog and the generated preset comments.</summary>
        public string Description { get; }
    }

    /// <summary>
    /// Every option the analyzers read, declared once.
    /// </summary>
    /// <remarks>
    /// <para>
    /// An option is only real if it reaches all of its channels: the options file (the one
    /// Unity passes to the compiler, so the only one that works in a build),
    /// <c>.editorconfig</c>, the catalog the Rule Manager window reads, and the commented
    /// examples in the generated presets. Keeping the list in each channel meant keeping four
    /// lists in step by hand, and two of the seven keys had fallen out of three of them:
    /// <c>upa_shader_property_hot_path_only</c> and <c>upa_log_wrapper_types</c> read
    /// <c>.editorconfig</c> directly, so they did nothing at all in a Unity build.
    /// </para>
    /// <para>
    /// The channels are generated from this list now, and
    /// <c>UpaOptionCatalogTests</c> holds the last edge: every option key constant declared
    /// anywhere in this assembly must appear here, and every entry here must be a real key.
    /// </para>
    /// </remarks>
    public static class UpaOptionCatalog
    {
        /// <summary>The file Unity passes to the compiler, and so the only channel that
        /// applies to a build rather than only to the IDE.</summary>
        public const string OptionsFileName = UpaOptions.FileName;

        private static readonly UpaOptionDefinition[] s_options =
        {
            new UpaOptionDefinition(
                HotPathDetector.MessagesOptionKey,
                UpaOptionKind.List,
                "Update,FixedUpdate,LateUpdate,OnGUI,OnAnimatorMove,OnAnimatorIK,OnPreCull," +
                "OnPreRender,OnPostRender,OnRenderObject,OnWillRenderObject,OnRenderImage," +
                "OnTriggerStay,OnTriggerStay2D,OnCollisionStay,OnCollisionStay2D,OnParticleUpdateJobScheduled",
                "Unity messages treated as per-frame hot paths. Replaces the default set."),
            new UpaOptionDefinition(
                HotPathDetector.AttributesOptionKey,
                UpaOptionKind.List,
                "HotPath,PerformanceCritical",
                "Attribute short names that mark any method as a hot path ('Attribute' suffix optional)."),
            new UpaOptionDefinition(
                HotPathDetector.IncludeLambdasOptionKey,
                UpaOptionKind.Bool,
                "true",
                "Treat lambdas and local functions declared inside a hot-path method as hot."),
            new UpaOptionDefinition(
                UPA1001NonExhaustiveEnumSwitchAnalyzer.AllowDefaultOptionKey,
                UpaOptionKind.Bool,
                "true",
                "For UPA1001, a default branch (or discard arm) counts as exhaustive."),
            new UpaOptionDefinition(
                UPA0029SequentialAddAnalyzer.HotPathOnlyOptionKey,
                UpaOptionKind.Bool,
                "false",
                "Narrow UPA0029 to per-frame code instead of reporting copy loops anywhere."),
            new UpaOptionDefinition(
                UPA0003StringPropertyAccessAnalyzer.HotPathOnlyOptionKey,
                UpaOptionKind.Bool,
                "false",
                "Narrow UPA0003 to per-frame code instead of reporting every string-keyed lookup."),
            new UpaOptionDefinition(
                UPA0005DirectDebugLoggingAnalyzer.WrapperTypesOptionKey,
                UpaOptionKind.List,
                string.Empty,
                "Type names that already wrap logging; UPA0005 stays quiet inside them."),
        };

        /// <summary>Every option, in declaration order.</summary>
        public static IReadOnlyList<UpaOptionDefinition> Options => s_options;
    }
}
