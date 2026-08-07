using System.Threading.Tasks;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using UnityPerformanceAnalyzers.CodeFixes;
using Xunit;

namespace UnityPerformanceAnalyzers.Tests
{
    public class UPA0019BoxedYieldAnalyzerTests
    {
        private static Task VerifyAsync(string source)
        {
            var test = new CSharpAnalyzerTest<UPA0019BoxedYieldAnalyzer, DefaultVerifier>
            {
                TestCode = source,
                ReferenceAssemblies = ReferenceAssemblies.NetStandard.NetStandard20,
            };
            test.TestState.AdditionalReferences.Add(typeof(UnityEngine.MonoBehaviour).Assembly);
            return test.RunAsync();
        }

        private static Task VerifyFixAsync(string source, string fixedSource)
        {
            var test = new CSharpCodeFixTest<UPA0019BoxedYieldAnalyzer, UPA0019BoxedYieldCodeFixProvider, DefaultVerifier>
            {
                TestCode = source,
                FixedCode = fixedSource,
                ReferenceAssemblies = ReferenceAssemblies.NetStandard.NetStandard20,
            };
            test.TestState.AdditionalReferences.Add(typeof(UnityEngine.MonoBehaviour).Assembly);
            return test.RunAsync();
        }

        // UPA0019 test case 1 (trigger half; the code fix half is YieldedZero_CodeFix_ReplacesWithNull)
        [Fact]
        public Task YieldReturnZero_InCoroutine_Triggers()
        {
            return VerifyAsync(@"
using System.Collections;
using UnityEngine;

class C : MonoBehaviour
{
    IEnumerator Fade()
    {
        yield return {|UPA0019:0|};
    }
}");
        }

        // UPA0019 test case 2
        [Fact]
        public Task YieldReturnNull_DoesNotTrigger()
        {
            return VerifyAsync(@"
using System.Collections;
using UnityEngine;

class C : MonoBehaviour
{
    IEnumerator Fade()
    {
        yield return null;
    }
}");
        }

        // UPA0019 test case 3
        [Fact]
        public Task YieldReturnWaitForSeconds_DoesNotTrigger()
        {
            return VerifyAsync(@"
using System.Collections;
using UnityEngine;

class C : MonoBehaviour
{
    IEnumerator Fade()
    {
        yield return new WaitForSeconds(1f);
    }
}");
        }

        // UPA0019 test case 4
        [Fact]
        public Task YieldReturnValue_OnNonMonoBehaviour_DoesNotTrigger()
        {
            return VerifyAsync(@"
using System.Collections;

class NumberSource
{
    public IEnumerator Numbers()
    {
        yield return 1;
    }
}");
        }

        [Fact]
        public Task GenericEnumeratorMethod_DoesNotTrigger()
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

        // UPA0019 test case 1 — code fix half
        [Fact]
        public Task YieldedZero_CodeFix_ReplacesWithNull()
        {
            return VerifyFixAsync(@"
using System.Collections;
using UnityEngine;

class C : MonoBehaviour
{
    IEnumerator Fade()
    {
        yield return {|UPA0019:0|};
    }
}", @"
using System.Collections;
using UnityEngine;

class C : MonoBehaviour
{
    IEnumerator Fade()
    {
        yield return null;
    }
}");
        }
    }
}
