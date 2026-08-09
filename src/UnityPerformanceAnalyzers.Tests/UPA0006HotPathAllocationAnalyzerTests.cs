using System.Threading.Tasks;
using Xunit;

namespace UnityPerformanceAnalyzers.Tests
{
    public class UPA0006HotPathAllocationAnalyzerTests
    {
        private static Task VerifyAsync(string source) =>
            RuleVerifier.VerifyAsync<UPA0006HotPathAllocationAnalyzer>(source);

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

        // UPA0006 test case 12 — interpolation holes box without a conversion node
        [Fact]
        public Task ValueTypeInterpolationHole_InUpdate_Triggers()
        {
            return VerifyAsync(@"
using UnityEngine;

class C : MonoBehaviour
{
    int hp;

    void Update()
    {
        var label = $""hp {|UPA0006:{hp}|}"";
        _ = label;
    }
}");
        }

        // UPA0006 test case 13 — string holes do not box; the string allocation is UPA2000's
        [Fact]
        public Task StringInterpolationHole_InUpdate_DoesNotTrigger()
        {
            return VerifyAsync(@"
using UnityEngine;

class C : MonoBehaviour
{
    string playerName = string.Empty;

    void Update()
    {
        var label = $""name {playerName}"";
        _ = label;
    }
}");
        }

        // UPA0006 test case 14 — struct passed as an interface parameter boxes
        [Fact]
        public Task StructToInterfaceArgument_InUpdate_Triggers()
        {
            return VerifyAsync(@"
using System;
using UnityEngine;

class C : MonoBehaviour
{
    void Update()
    {
        Consume({|UPA0006:42|});
    }

    void Consume(IComparable value)
    {
        _ = value;
    }
}");
        }

        // UPA0006 test case 15 — Nullable<T> to object boxes
        [Fact]
        public Task NullableToObject_InUpdate_Triggers()
        {
            return VerifyAsync(@"
using UnityEngine;

class C : MonoBehaviour
{
    int? maybe;

    void Update()
    {
        object o = {|UPA0006:maybe|};
        _ = o;
    }
}");
        }
    }
}
