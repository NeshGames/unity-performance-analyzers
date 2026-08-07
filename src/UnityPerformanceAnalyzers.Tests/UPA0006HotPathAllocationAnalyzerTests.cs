using System.Threading.Tasks;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Xunit;

namespace UnityPerformanceAnalyzers.Tests
{
    public class UPA0006HotPathAllocationAnalyzerTests
    {
        private static Task VerifyAsync(string source)
        {
            var test = new CSharpAnalyzerTest<UPA0006HotPathAllocationAnalyzer, DefaultVerifier>
            {
                TestCode = source,
                ReferenceAssemblies = ReferenceAssemblies.NetStandard.NetStandard20,
            };
            test.TestState.AdditionalReferences.Add(typeof(UnityEngine.MonoBehaviour).Assembly);
            return test.RunAsync();
        }

        // UPA0006 test case 1
        [Fact]
        public Task ReferenceTypeCreation_InUpdate_Triggers()
        {
            return VerifyAsync(@"
using System.Collections.Generic;
using UnityEngine;

class C : MonoBehaviour
{
    void Update()
    {
        var list = {|UPA0006:new List<int>()|};
    }
}");
        }

        // UPA0006 test case 2
        [Fact]
        public Task ValueTypeCreation_InUpdate_DoesNotTrigger()
        {
            return VerifyAsync(@"
using UnityEngine;

class C : MonoBehaviour
{
    void Update()
    {
        var v = new Vector4();
    }
}");
        }

        // UPA0006 test case 3
        [Fact]
        public Task ReferenceTypeCreation_InStart_DoesNotTrigger()
        {
            return VerifyAsync(@"
using System.Collections.Generic;
using UnityEngine;

class C : MonoBehaviour
{
    void Start()
    {
        var list = new List<int>();
    }
}");
        }

        // UPA0006 test case 4
        [Fact]
        public Task ArrayCreation_InUpdate_Triggers()
        {
            return VerifyAsync(@"
using UnityEngine;

class C : MonoBehaviour
{
    void Update()
    {
        var buffer = {|UPA0006:new int[16]|};
    }
}");
        }

        // UPA0006 test case 5 — exceptional paths are deliberately ignored
        [Fact]
        public Task ExceptionCreation_InThrow_DoesNotTrigger()
        {
            return VerifyAsync(@"
using System;
using UnityEngine;

class C : MonoBehaviour
{
    void Update()
    {
        if (name.Length == 0)
        {
            throw new InvalidOperationException(""no name"");
        }
    }
}");
        }

        // UPA0006 test case 6
        [Fact]
        public Task Creation_InHotPathAttributedMethod_Triggers()
        {
            return VerifyAsync(@"
class HotPathAttribute : System.Attribute { }

class C
{
    [HotPath]
    void Tick()
    {
        var o = {|UPA0006:new object()|};
    }
}");
        }

        // UPA0006 test case 7
        [Fact]
        public Task Creation_InLambdaInsideUpdate_Triggers()
        {
            return VerifyAsync(@"
using System;
using UnityEngine;

class C : MonoBehaviour
{
    void Update()
    {
        Action a = () =>
        {
            var o = {|UPA0006:new object()|};
        };
    }
}");
        }

        // UPA0006 test case 8 — method-group delegate creation is out of scope in v0.1
        [Fact]
        public Task MethodGroupDelegate_InUpdate_DoesNotTrigger()
        {
            return VerifyAsync(@"
using System;
using UnityEngine;

class C : MonoBehaviour
{
    void Update()
    {
        Action a = Helper;
    }

    void Helper()
    {
    }
}");
        }

        // UPA0006 test case 9
        [Fact]
        public Task Boxing_InUpdate_Triggers()
        {
            return VerifyAsync(@"
using UnityEngine;

class C : MonoBehaviour
{
    void Update()
    {
        object o = {|UPA0006:42|};
    }
}");
        }

        // UPA0006 test case 10
        [Fact]
        public Task GenericCall_NoBoxing_InUpdate_DoesNotTrigger()
        {
            return VerifyAsync(@"
using UnityEngine;

class C : MonoBehaviour
{
    void Update()
    {
        Consume(42);
    }

    void Consume<T>(T value)
    {
    }
}");
        }

        // UPA0006 test case 11
        [Fact]
        public Task Boxing_InStart_DoesNotTrigger()
        {
            return VerifyAsync(@"
using UnityEngine;

class C : MonoBehaviour
{
    void Start()
    {
        object o = 42;
    }
}");
        }
    }
}
