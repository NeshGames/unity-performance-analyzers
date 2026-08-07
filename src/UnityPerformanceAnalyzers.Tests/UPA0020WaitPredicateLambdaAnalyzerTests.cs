using System.Threading.Tasks;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Xunit;

namespace UnityPerformanceAnalyzers.Tests
{
    public class UPA0020WaitPredicateLambdaAnalyzerTests
    {
        private static Task VerifyAsync(string source)
        {
            var test = new CSharpAnalyzerTest<UPA0020WaitPredicateLambdaAnalyzer, DefaultVerifier>
            {
                TestCode = source,
                ReferenceAssemblies = ReferenceAssemblies.NetStandard.NetStandard20,
            };
            test.TestState.AdditionalReferences.Add(typeof(UnityEngine.MonoBehaviour).Assembly);
            // UPA0020 is disabled by default; enable it the same way a preset would.
            test.TestState.AnalyzerConfigFiles.Add(("/.editorconfig", @"
root = true

[*.cs]
dotnet_diagnostic.UPA0020.severity = warning
"));
            return test.RunAsync();
        }

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
