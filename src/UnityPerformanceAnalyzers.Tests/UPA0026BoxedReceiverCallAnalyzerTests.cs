using System.Threading.Tasks;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Xunit;

namespace UnityPerformanceAnalyzers.Tests
{
    public class UPA0026BoxedReceiverCallAnalyzerTests
    {
        private static Task VerifyAsync(string source)
        {
            var test = new CSharpAnalyzerTest<UPA0026BoxedReceiverCallAnalyzer, DefaultVerifier>
            {
                TestCode = source,
                ReferenceAssemblies = ReferenceAssemblies.NetStandard.NetStandard20,
            };
            test.TestState.AdditionalReferences.Add(typeof(UnityEngine.MonoBehaviour).Assembly);
            return test.RunAsync();
        }

        // UPA0026 test case 1
        [Fact]
        public Task EnumToString_InUpdate_Triggers()
        {
            return VerifyAsync(@"
using UnityEngine;

enum Phase { Idle, Running }

class C : MonoBehaviour
{
    Phase _phase;

    void Update()
    {
        var label = {|UPA0026:_phase.ToString()|};
    }
}");
        }

        // UPA0026 test case 2
        [Fact]
        public Task StructGetHashCode_WithoutOverride_InUpdate_Triggers()
        {
            return VerifyAsync(@"
using UnityEngine;

struct GridPos
{
    public int X;
    public int Y;
}

class C : MonoBehaviour
{
    GridPos _pos;

    void Update()
    {
        var hash = {|UPA0026:_pos.GetHashCode()|};
    }
}");
        }

        // UPA0026 test case 3
        [Fact]
        public Task StructToString_WithOverride_DoesNotTrigger()
        {
            return VerifyAsync(@"
using UnityEngine;

struct GridPos
{
    public int X;

    public override string ToString() => ""x"";
}

class C : MonoBehaviour
{
    GridPos _pos;

    void Update()
    {
        var label = _pos.ToString();
    }
}");
        }

        // UPA0026 test case 4
        [Fact]
        public Task IntToString_DoesNotTrigger()
        {
            return VerifyAsync(@"
using UnityEngine;

class C : MonoBehaviour
{
    int _count;

    void Update()
    {
        var label = _count.ToString();
    }
}");
        }

        // UPA0026 test case 5
        [Fact]
        public Task EquatableEquals_DoesNotTrigger()
        {
            return VerifyAsync(@"
using System;
using UnityEngine;

struct GridPos : IEquatable<GridPos>
{
    public int X;

    public bool Equals(GridPos other) => X == other.X;
}

class C : MonoBehaviour
{
    GridPos _a;
    GridPos _b;

    void Update()
    {
        var same = _a.Equals(_b);
    }
}");
        }

        // UPA0026 test case 5b — Equals(object) on a struct without an override still boxes
        [Fact]
        public Task ObjectEquals_WithoutOverride_InUpdate_Triggers()
        {
            return VerifyAsync(@"
using UnityEngine;

struct GridPos
{
    public int X;
}

class C : MonoBehaviour
{
    GridPos _a;
    object _b;

    void Update()
    {
        var same = {|UPA0026:_a.Equals(_b)|};
    }
}");
        }

        // UPA0026 test case 6
        [Fact]
        public Task StructGetType_InUpdate_Triggers()
        {
            return VerifyAsync(@"
using UnityEngine;

struct GridPos
{
    public int X;
}

class C : MonoBehaviour
{
    GridPos _pos;

    void Update()
    {
        var type = {|UPA0026:_pos.GetType()|};
    }
}");
        }

        // UPA0026 test case 7
        [Fact]
        public Task TypeParameterReceiver_DoesNotTrigger()
        {
            return VerifyAsync(@"
using UnityEngine;

class C : MonoBehaviour
{
    int _value;

    void Update()
    {
        Describe(_value);
    }

    void Describe<T>(T value) where T : struct
    {
        var label = value.ToString();
    }
}");
        }

        // UPA0026 test case 8
        [Fact]
        public Task EnumToString_InStart_DoesNotTrigger()
        {
            return VerifyAsync(@"
using UnityEngine;

enum Phase { Idle, Running }

class C : MonoBehaviour
{
    Phase _phase;

    void Start()
    {
        var label = _phase.ToString();
    }
}");
        }
    }
}
