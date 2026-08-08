using System.Threading.Tasks;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Xunit;

namespace UnityPerformanceAnalyzers.Tests
{
    public class UPA0027ParamsArrayAllocationAnalyzerTests
    {
        private static Task VerifyAsync(string source)
        {
            var test = new CSharpAnalyzerTest<UPA0027ParamsArrayAllocationAnalyzer, DefaultVerifier>
            {
                TestCode = source,
                ReferenceAssemblies = ReferenceAssemblies.NetStandard.NetStandard20,
            };
            test.TestState.AdditionalReferences.Add(typeof(UnityEngine.MonoBehaviour).Assembly);
            return test.RunAsync();
        }

        // UPA0027 test case 1
        [Fact]
        public Task MathfMaxThreeArguments_InUpdate_Triggers()
        {
            return VerifyAsync(@"
using UnityEngine;

class C : MonoBehaviour
{
    float a, b, c;

    void Update()
    {
        var m = {|UPA0027:Mathf.Max(a, b, c)|};
    }
}");
        }

        // UPA0027 test case 2
        [Fact]
        public Task MathfMaxTwoArguments_InUpdate_DoesNotTrigger()
        {
            return VerifyAsync(@"
using UnityEngine;

class C : MonoBehaviour
{
    float a, b;

    void Update()
    {
        var m = Mathf.Max(a, b);
    }
}");
        }

        // UPA0027 test case 3
        [Fact]
        public Task MathfMaxWithArrayField_InUpdate_DoesNotTrigger()
        {
            return VerifyAsync(@"
using UnityEngine;

class C : MonoBehaviour
{
    float[] buffer;

    void Update()
    {
        var m = Mathf.Max(buffer);
    }
}");
        }

        // UPA0027 test case 4
        [Fact]
        public Task MathfMaxWithExplicitArray_InUpdate_DoesNotTrigger()
        {
            return VerifyAsync(@"
using UnityEngine;

class C : MonoBehaviour
{
    float a, b, c;

    void Update()
    {
        var m = Mathf.Max(new[] { a, b, c });
    }
}");
        }

        // UPA0027 test case 5
        [Fact]
        public Task MathfMaxThreeArguments_InStart_DoesNotTrigger()
        {
            return VerifyAsync(@"
using UnityEngine;

class C : MonoBehaviour
{
    float a, b, c;

    void Start()
    {
        var m = Mathf.Max(a, b, c);
    }
}");
        }

        // UPA0027 test case 6 — params object[] reports the boxing count instead.
        [Fact]
        public Task StringFormatWithFourValueTypeArguments_InUpdate_TriggersBoxingVariant()
        {
            return VerifyAsync(@"
using UnityEngine;

class C : MonoBehaviour
{
    int i, j, k, l;

    void Update()
    {
        var s = {|UPA0027:string.Format(""{0}{1}{2}{3}"", i, j, k, l)|};
    }
}");
        }

        // UPA0027 test case 7
        [Fact]
        public Task StringConcatThreeStrings_InUpdate_DoesNotTrigger()
        {
            return VerifyAsync(@"
using UnityEngine;

class C : MonoBehaviour
{
    string s1, s2, s3;

    void Update()
    {
        var s = string.Concat(s1, s2, s3);
    }
}");
        }

        // UPA0027 test case 8
        [Fact]
        public Task UserDefinedParamsMethod_InUpdate_Triggers()
        {
            return VerifyAsync(@"
using UnityEngine;

class C : MonoBehaviour
{
    void My(params int[] values) { }

    void Update()
    {
        {|UPA0027:My(1, 2)|};
    }
}");
        }

        // UPA0027 test case 9 — zero arguments compile to Array.Empty.
        [Fact]
        public Task UserDefinedParamsMethodWithNoArguments_InUpdate_DoesNotTrigger()
        {
            return VerifyAsync(@"
using UnityEngine;

class C : MonoBehaviour
{
    void My(params int[] values) { }

    void Update()
    {
        My();
    }
}");
        }

        // UPA0027 test case 10
        [Fact]
        public Task MathfMinInsideLambdaInUpdate_Triggers()
        {
            return VerifyAsync(@"
using System;
using UnityEngine;

class C : MonoBehaviour
{
    float a, b, c;

    void Update()
    {
        Action run = () => { var m = {|UPA0027:Mathf.Min(a, b, c)|}; };
        run();
    }
}");
        }

        // UPA0027 test case 11
        [Fact]
        public Task DebugLogFormat_InUpdate_Triggers()
        {
            return VerifyAsync(@"
using UnityEngine;

class C : MonoBehaviour
{
    int x;

    void Update()
    {
        {|UPA0027:Debug.LogFormat(""{0}"", x)|};
    }
}");
        }
    }
}
