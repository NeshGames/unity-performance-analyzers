using System.Threading.Tasks;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Xunit;

namespace UnityPerformanceAnalyzers.Tests
{
    public class UPA2011CoroutineAnalyzerTests
    {
        private static Task VerifyAsync(string source)
        {
            var test = new CSharpAnalyzerTest<UPA2011CoroutineAnalyzer, DefaultVerifier>
            {
                TestCode = source,
                ReferenceAssemblies = ReferenceAssemblies.NetStandard.NetStandard20,
            };
            test.TestState.AdditionalReferences.Add(typeof(UnityEngine.MonoBehaviour).Assembly);
            // UPA2011 is disabled by default; enable it the same way a preset would.
            test.TestState.AnalyzerConfigFiles.Add(("/.editorconfig", @"
root = true

[*.cs]
dotnet_diagnostic.UPA2011.severity = warning
"));
            return test.RunAsync();
        }

        // UPA2011 test case 1
        [Fact]
        public Task CoroutineMethod_OnMonoBehaviour_Triggers()
        {
            return VerifyAsync(@"
using System.Collections;
using UnityEngine;

class C : MonoBehaviour
{
    IEnumerator {|UPA2011:FadeOut|}()
    {
        yield return null;
    }
}");
        }

        // UPA2011 test case 2 — coroutine-shaped Unity message
        [Fact]
        public Task CoroutineStartMessage_Triggers()
        {
            return VerifyAsync(@"
using System.Collections;
using UnityEngine;

class C : MonoBehaviour
{
    IEnumerator {|UPA2011:Start|}()
    {
        yield return null;
    }
}");
        }

        // UPA2011 test case 3 — ordinary enumerator pattern outside MonoBehaviour
        [Fact]
        public Task IEnumeratorMethod_OnPlainClass_DoesNotTrigger()
        {
            return VerifyAsync(@"
using System.Collections;

class C
{
    IEnumerator Enumerate()
    {
        yield return null;
    }
}");
        }

        // UPA2011 test case 4 — IEnumerator<T> is data iteration, not a coroutine
        [Fact]
        public Task GenericIEnumeratorMethod_OnMonoBehaviour_DoesNotTrigger()
        {
            return VerifyAsync(@"
using System.Collections.Generic;
using UnityEngine;

class C : MonoBehaviour
{
    IEnumerator<int> Numbers()
    {
        yield return 1;
    }
}");
        }

        // isEnabledByDefault: false — asserted on the descriptor because the
        // testing framework force-enables disabled-by-default rules when running analyzers.
        [Fact]
        public void Descriptor_IsDisabledByDefault()
        {
            var descriptor = Assert.Single(new UPA2011CoroutineAnalyzer().SupportedDiagnostics);
            Assert.False(descriptor.IsEnabledByDefault);
        }
    }
}
