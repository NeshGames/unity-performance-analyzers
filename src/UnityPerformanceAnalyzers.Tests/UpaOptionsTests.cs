using System.Threading.Tasks;
using Xunit;

namespace UnityPerformanceAnalyzers.Tests
{
    /// <summary>
    /// Layered option resolution through the universal options file
    /// (Rules.UnityPerformanceAnalyzers.additionalfile): file wins over .editorconfig,
    /// invalid or missing values fall through per key, parsing tolerates junk. Exercised
    /// end to end through the hot-path probe and UPA1001 rather than unit-testing the
    /// parser, so the wiring inside the analyzers is covered too.
    /// </summary>
    public class UpaOptionsTests
    {
        private const string Prelude = @"
static class Marker
{
    public static void Mark() { }
}
";

        private static Task VerifyHotPathAsync(string source, string? optionsFile = null, string? editorConfig = null) =>
            RuleVerifier.VerifyAsync<HotPathProbeAnalyzer>(source + Prelude, new RuleHarness
            {
                OptionsFile = optionsFile,
                EditorConfig = editorConfig,
            });

        private const string StartAndUpdateSource = @"
using UnityEngine;

class C : MonoBehaviour
{
    void Start()
    {
        {|UPATEST01:Marker.Mark()|};
    }

    void Update()
    {
        Marker.Mark();
    }
}";

        // Options-file test case 1 — the file alone redefines the hot message set
        [Fact]
        public Task OptionsFile_RedefinesHotMessages()
        {
            return VerifyHotPathAsync(
                StartAndUpdateSource,
                optionsFile: "upa_hot_path_messages = Start");
        }

        // Options-file test case 2 — the file wins over a conflicting .editorconfig value
        [Fact]
        public Task OptionsFile_WinsOverEditorConfig()
        {
            return VerifyHotPathAsync(
                StartAndUpdateSource,
                optionsFile: "upa_hot_path_messages = Start",
                editorConfig: "upa_hot_path_messages = LateUpdate");
        }

        // Options-file test case 3 — a key the file does not set falls to .editorconfig
        [Fact]
        public Task MissingKey_FallsToEditorConfig()
        {
            return VerifyHotPathAsync(
                StartAndUpdateSource,
                optionsFile: "upa_hot_path_include_lambdas = true",
                editorConfig: "upa_hot_path_messages = Start");
        }

        // Options-file test case 4 — neither channel set: built-in defaults apply
        [Fact]
        public Task NoChannelSet_UsesDefaults()
        {
            return VerifyHotPathAsync(@"
using UnityEngine;

class C : MonoBehaviour
{
    void Update()
    {
        {|UPATEST01:Marker.Mark()|};
    }
}",
                optionsFile: "# only a comment\n");
        }

        // Options-file test case 5 — junk lines are ignored without errors
        [Fact]
        public Task MalformedLines_AreIgnored()
        {
            return VerifyHotPathAsync(
                StartAndUpdateSource,
                optionsFile: string.Join("\n",
                    "# comment line",
                    "",
                    "this line has no separator",
                    "= value without key",
                    "unknown_key = whatever",
                    "upa_hot_path_messages = Start"));
        }

        // Options-file test case 6 — an invalid value counts as unset and falls through
        [Fact]
        public Task InvalidBool_FallsToEditorConfig()
        {
            // include_lambdas resolves to false (editorconfig layer): the direct call in
            // Update stays hot, the call inside the lambda does not.
            return VerifyHotPathAsync(@"
using UnityEngine;
using System;

class C : MonoBehaviour
{
    void Update()
    {
        Action a = () => Marker.Mark();
        {|UPATEST01:a()|};
    }
}",
                optionsFile: "upa_hot_path_include_lambdas = ja",
                editorConfig: "upa_hot_path_include_lambdas = false");
        }

        // Options-file test case 7 — a duplicated key keeps its last value
        [Fact]
        public Task DuplicateKey_LastValueWins()
        {
            return VerifyHotPathAsync(
                StartAndUpdateSource,
                optionsFile: string.Join("\n",
                    "upa_hot_path_messages = LateUpdate",
                    "upa_hot_path_messages = Start"));
        }

        // Options-file test case 8 — UPA1001 reads its option through the same channel
        [Fact]
        public Task EnumSwitchAllowDefault_ViaOptionsFile()
        {
            return RuleVerifier.VerifyAsync<UPA1001NonExhaustiveEnumSwitchAnalyzer>(
                @"
enum State { Idle, Running, Dead }

class C
{
    void M(State state)
    {
        switch ({|UPA1001:state|})
        {
            case State.Idle:
                break;
            default:
                break;
        }
    }
}",
                new RuleHarness
                {
                    UnityStubs = false,
                    OptionsFile = "upa_enum_switch_allow_default = false",
                });
        }
    }
}
