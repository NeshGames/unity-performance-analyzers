using System.Threading.Tasks;
using Xunit;

namespace UnityPerformanceAnalyzers.Tests
{
    /// <summary>
    /// Option resolution through Rules.UnityPerformanceAnalyzers.additionalfile. Missing or
    /// invalid values use built-in defaults, junk is ignored, and duplicate keys keep their
    /// last value. Exercised end to end so analyzer wiring is covered with the parser.
    /// </summary>
    public class UpaOptionsTests
    {
        private const string Prelude = @"
static class Marker
{
    public static void Mark() { }
}
";

        private static Task VerifyHotPathAsync(string source, string? optionsFile = null) =>
            RuleVerifier.VerifyAsync<HotPathProbeAnalyzer>(source + Prelude, new RuleHarness
            {
                OptionsFile = optionsFile,
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

        [Fact]
        public Task OptionsFile_RedefinesHotMessages()
        {
            return VerifyHotPathAsync(
                StartAndUpdateSource,
                optionsFile: "upa_hot_path_messages = Start");
        }

        [Fact]
        public Task NoOptionSet_UsesDefaults()
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

        [Fact]
        public Task InvalidBool_UsesBuiltInDefault()
        {
            return VerifyHotPathAsync(@"
using UnityEngine;
using System;

class C : MonoBehaviour
{
    void Update()
    {
        Action a = () => {|UPATEST01:Marker.Mark()|};
        {|UPATEST01:a()|};
    }
}",
                optionsFile: "upa_hot_path_include_lambdas = ja");
        }

        [Fact]
        public Task DuplicateKey_LastValueWins()
        {
            return VerifyHotPathAsync(
                StartAndUpdateSource,
                optionsFile: string.Join("\n",
                    "upa_hot_path_messages = LateUpdate",
                    "upa_hot_path_messages = Start"));
        }

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
