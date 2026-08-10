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

        // UPA0006 test case 16 — HasFlag boxes its argument in IL, and both Mono and IL2CPP
        // remove that box along with the call. Measured at 0.00 B/op on both editors against a
        // control that allocates in the same loop.
        [Fact]
        public Task HasFlagWithConstantArgument_InUpdate_DoesNotTrigger()
        {
            return VerifyAsync(@"
using System;
using UnityEngine;

[Flags]
enum Rights { None = 0, Read = 1, Write = 2 }

class C : MonoBehaviour
{
    Rights rights;

    void Update()
    {
        if (rights.HasFlag(Rights.Read))
        {
        }
    }
}");
        }

        // UPA0006 test case 17
        [Fact]
        public Task HasFlagWithLocalOrFieldArgument_InUpdate_DoesNotTrigger()
        {
            return VerifyAsync(@"
using System;
using UnityEngine;

[Flags]
enum Rights { None = 0, Read = 1, Write = 2 }

class C : MonoBehaviour
{
    Rights rights;
    Rights wanted;

    void Update()
    {
        var local = Rights.Read;
        if (rights.HasFlag(local))
        {
        }

        if (rights.HasFlag(wanted))
        {
        }
    }
}");
        }

        // UPA0006 test case 18 — the elision needs the box to sit next to the call. Written
        // inline, a conditional puts branches in between and the box survives: measured at
        // 0.12 B/op on 2022.3 and 0.39 on Unity 6, with collections running in both.
        [Fact]
        public Task HasFlagWithConditionalArgument_InUpdate_Triggers()
        {
            return VerifyAsync(@"
using System;
using UnityEngine;

[Flags]
enum Rights { None = 0, Read = 1, Write = 2 }

class C : MonoBehaviour
{
    Rights rights;
    bool toggle;

    void Update()
    {
        if (rights.HasFlag({|UPA0006:toggle ? Rights.Read : Rights.Write|}))
        {
        }
    }
}");
        }

        // UPA0006 test case 19 — the exclusion is bound to System.Enum.HasFlag, not to the name
        [Fact]
        public Task UserDefinedHasFlag_InUpdate_Triggers()
        {
            return VerifyAsync(@"
using UnityEngine;

struct Mask
{
    public bool HasFlag(object flag) => flag != null;
}

class C : MonoBehaviour
{
    Mask mask;

    void Update()
    {
        if (mask.HasFlag({|UPA0006:1|}))
        {
        }
    }
}");
        }
    }
}
