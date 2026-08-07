using System.Threading.Tasks;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Xunit;

namespace UnityPerformanceAnalyzers.Tests
{
    public class UPA0009ListCountInForLoopAnalyzerTests
    {
        private static Task VerifyAsync(string source)
        {
            var test = new CSharpAnalyzerTest<UPA0009ListCountInForLoopAnalyzer, DefaultVerifier>
            {
                TestCode = source,
                ReferenceAssemblies = ReferenceAssemblies.NetStandard.NetStandard20,
            };
            test.TestState.AdditionalReferences.Add(typeof(UnityEngine.MonoBehaviour).Assembly);
            // UPA0009 is disabled by default; enable it the same way a preset would.
            test.TestState.AnalyzerConfigFiles.Add(("/.editorconfig", @"
root = true

[*.cs]
dotnet_diagnostic.UPA0009.severity = warning
"));
            return test.RunAsync();
        }

        // UPA0009 test case 1
        [Fact]
        public Task CountInCondition_InUpdate_Triggers()
        {
            return VerifyAsync(@"
using System.Collections.Generic;
using UnityEngine;

class C : MonoBehaviour
{
    List<int> list = new List<int>();

    void Update()
    {
        for (int i = 0; i < {|UPA0009:list.Count|}; i++)
        {
        }
    }
}");
        }

        // UPA0009 test case 2
        [Fact]
        public Task HoistedCount_DoesNotTrigger()
        {
            return VerifyAsync(@"
using System.Collections.Generic;
using UnityEngine;

class C : MonoBehaviour
{
    List<int> list = new List<int>();

    void Update()
    {
        int n = list.Count;
        for (int i = 0; i < n; i++)
        {
        }
    }
}");
        }

        // UPA0009 test case 3 — array.Length is the JIT-friendly form
        [Fact]
        public Task ArrayLength_DoesNotTrigger()
        {
            return VerifyAsync(@"
using UnityEngine;

class C : MonoBehaviour
{
    int[] array = new int[8];

    void Update()
    {
        for (int i = 0; i < array.Length; i++)
        {
        }
    }
}");
        }

        // UPA0009 test case 4 — hoisting would change semantics
        [Fact]
        public Task BodyMutatesList_DoesNotTrigger()
        {
            return VerifyAsync(@"
using System.Collections.Generic;
using UnityEngine;

class C : MonoBehaviour
{
    List<int> list = new List<int>();

    void Update()
    {
        for (int i = 0; i < list.Count; i++)
        {
            list.Add(i);
        }
    }
}");
        }

        // UPA0009 test case 5
        [Fact]
        public Task CountInCondition_InStart_DoesNotTrigger()
        {
            return VerifyAsync(@"
using System.Collections.Generic;
using UnityEngine;

class C : MonoBehaviour
{
    List<int> list = new List<int>();

    void Start()
    {
        for (int i = 0; i < list.Count; i++)
        {
        }
    }
}");
        }

        // UPA0009 test case 6
        [Fact]
        public Task CountInCondition_InHotPathAttributedMethod_Triggers()
        {
            return VerifyAsync(@"
using System.Collections.Generic;

class HotPathAttribute : System.Attribute { }

class C
{
    List<int> list = new List<int>();

    [HotPath]
    void Tick()
    {
        for (int i = 0; i < {|UPA0009:this.list.Count|}; i++)
        {
        }
    }
}");
        }
    }
}
