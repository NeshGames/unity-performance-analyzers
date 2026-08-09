using System.Threading.Tasks;
using Xunit;

namespace UnityPerformanceAnalyzers.Tests
{
    public class UPA0020WaitPredicateLambdaAnalyzerTests
    {
        private static Task VerifyAsync(string source) =>
            RuleVerifier.VerifyAsync<UPA0020WaitPredicateLambdaAnalyzer>(source, new RuleHarness
            {
                EnabledRules = { "UPA0020" },
            });

        // UPA0020 test case 1
        [Fact]
        public Task WaitUntilWithLambda_Triggers()
        {
            return VerifyAsync(@"
using System.Collections;
using UnityEngine;

class C : MonoBehaviour
{
    bool ready;

    IEnumerator Run()
    {
        yield return {|UPA0020:new WaitUntil(() => ready)|};
    }
}");
        }

        // UPA0020 test case 2
        [Fact]
        public Task WaitUntilWithCachedPredicate_DoesNotTrigger()
        {
            return VerifyAsync(@"
using System;
using System.Collections;
using UnityEngine;

class C : MonoBehaviour
{
    Func<bool> cachedPredicate = () => true;

    IEnumerator Run()
    {
        yield return new WaitUntil(cachedPredicate);
    }
}");
        }

        // UPA0020 test case 3 — no flow analysis: field initializers report too
        [Fact]
        public Task WaitUntilWithLambda_InFieldInitializer_Triggers()
        {
            return VerifyAsync(@"
using UnityEngine;

class C : MonoBehaviour
{
    static bool ready;

    WaitUntil wait = {|UPA0020:new WaitUntil(() => ready)|};
}");
        }

        [Fact]
        public Task WaitWhileWithLambda_Triggers()
        {
            return VerifyAsync(@"
using System.Collections;
using UnityEngine;

class C : MonoBehaviour
{
    bool loading;

    IEnumerator Run()
    {
        yield return {|UPA0020:new WaitWhile(() => loading)|};
    }
}");
        }
    }
}
