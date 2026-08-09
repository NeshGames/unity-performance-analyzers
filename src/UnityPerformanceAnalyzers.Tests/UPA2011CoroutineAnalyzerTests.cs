using System.Threading.Tasks;
using Xunit;

namespace UnityPerformanceAnalyzers.Tests
{
    public class UPA2011CoroutineAnalyzerTests
    {
        private static Task VerifyAsync(string source, bool referenceUniTask = true)
        {
            var harness = new RuleHarness { EnabledRules = { "UPA2011" } };
            if (referenceUniTask)
            {
                harness.PackageAssemblies.Add(UpaProfile.UniTaskAssemblyName);
            }

            return RuleVerifier.VerifyAsync<UPA2011CoroutineAnalyzer>(source, harness);
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

        // UPA2011 test case 5 — without UniTask the rule never registers: there is no
        // allocation-free rewrite to suggest.
        [Fact]
        public Task CoroutineMethod_WithoutUniTask_DoesNotTrigger()
        {
            return VerifyAsync(@"
using System.Collections;
using UnityEngine;

class C : MonoBehaviour
{
    IEnumerator FadeOut()
    {
        yield return null;
    }

    IEnumerator Start()
    {
        yield return null;
    }
}", referenceUniTask: false);
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
