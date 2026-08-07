using System.Threading.Tasks;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Xunit;

namespace UnityPerformanceAnalyzers.Tests
{
    public class UPA0005DirectDebugLoggingAnalyzerTests
    {
        private static Task VerifyAsync(string source, string? extraConfig = null)
        {
            var test = new CSharpAnalyzerTest<UPA0005DirectDebugLoggingAnalyzer, DefaultVerifier>
            {
                TestCode = source,
                ReferenceAssemblies = ReferenceAssemblies.NetStandard.NetStandard20,
            };
            test.TestState.AdditionalReferences.Add(typeof(UnityEngine.Debug).Assembly);
            // UPA0005 is disabled by default; enable it the same way a preset would.
            test.TestState.AnalyzerConfigFiles.Add(("/.editorconfig", $@"
root = true

[*.cs]
dotnet_diagnostic.UPA0005.severity = warning
{extraConfig}
"));
            return test.RunAsync();
        }

        // UPA0005 test case 1
        [Fact]
        public Task Log_InOrdinaryMethod_Triggers()
        {
            return VerifyAsync(@"
using UnityEngine;

class C
{
    void M()
    {
        {|UPA0005:Debug.Log(""x"")|};
    }
}");
        }

        // UPA0005 test case 2
        [Fact]
        public Task LogError_FullyQualified_Triggers()
        {
            return VerifyAsync(@"
class C
{
    void M(System.Exception e)
    {
        {|UPA0005:UnityEngine.Debug.LogError(e)|};
    }
}");
        }

        // UPA0005 test case 3
        [Fact]
        public Task Assert_DoesNotTrigger()
        {
            return VerifyAsync(@"
using UnityEngine;

class C
{
    void M(bool cond)
    {
        Debug.Assert(cond);
        Debug.AssertFormat(cond, ""{0}"", 1);
    }
}");
        }

        // UPA0005 test case 4
        [Fact]
        public Task Log_InsideConditionalMethod_DoesNotTrigger()
        {
            return VerifyAsync(@"
using UnityEngine;

class C
{
    [System.Diagnostics.Conditional(""X"")]
    void M()
    {
        Debug.Log(""x"");
    }
}");
        }

        // UPA0005 test case 5
        [Fact]
        public Task Log_InsideConfiguredWrapperType_DoesNotTrigger()
        {
            return VerifyAsync(@"
using UnityEngine;

static class GameLog
{
    public static void Info(string message)
    {
        Debug.Log(message);
    }
}",
                extraConfig: "upa_log_wrapper_types = Log,GameLog");
        }

        // Companion to case 5: without the option the same wrapper type is reported.
        [Fact]
        public Task Log_InsideWrapperType_WithoutConfiguration_Triggers()
        {
            return VerifyAsync(@"
using UnityEngine;

static class GameLog
{
    public static void Info(string message)
    {
        {|UPA0005:Debug.Log(message)|};
    }
}");
        }

        // UPA0005 test case 6
        [Fact]
        public Task CustomDebugType_DoesNotTrigger()
        {
            return VerifyAsync(@"
static class MyDebug
{
    public static void Log(string message) { }
}

class C
{
    void M()
    {
        MyDebug.Log(""x"");
    }
}");
        }

        // UPA0005 test case 7
        [Fact]
        public Task Log_InsideLambda_Triggers()
        {
            return VerifyAsync(@"
using System;
using UnityEngine;

class C
{
    void M()
    {
        Action a = () => {|UPA0005:Debug.Log(""x"")|};
        a();
    }
}");
        }

        // UPA0005 test case 8
        [Fact]
        public Task ShortForm_WithUsingDirective_Triggers()
        {
            return VerifyAsync(@"
using UnityEngine;

class C
{
    void M(System.Exception e)
    {
        {|UPA0005:Debug.LogWarning(""x"")|};
        {|UPA0005:Debug.LogFormat(""{0}"", 1)|};
        {|UPA0005:Debug.LogException(e)|};
    }
}");
        }
    }
}
