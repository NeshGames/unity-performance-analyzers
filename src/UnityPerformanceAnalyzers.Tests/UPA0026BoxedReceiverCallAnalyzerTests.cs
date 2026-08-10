using System.Threading.Tasks;
using Xunit;

namespace UnityPerformanceAnalyzers.Tests
{
    // UPA0026 is about the receiver box a constrained call performs. Measured on IL2CPP,
    // three of the four methods it used to name perform none: ToString, GetHashCode and
    // Equals(object) all read 0.00 B/op with the argument pre-boxed, so the receiver was the
    // only thing left that could allocate. Only GetType does, and it cannot be overridden,
    // so there is no negative case for "the value type overrides it".
    public class UPA0026BoxedReceiverCallAnalyzerTests
    {
        private static Task VerifyAsync(string source) =>
            RuleVerifier.VerifyAsync<UPA0026BoxedReceiverCallAnalyzer>(source);

        // Passing the same text as source and fixed source asserts the opposite of a rewrite:
        // the diagnostic is reported and no fix is offered for it.
        private static Task VerifyFixAsync(string source, string fixedSource) =>
            RuleVerifier.VerifyCodeFixAsync<
                UPA0026BoxedReceiverCallAnalyzer,
                CodeFixes.UPA0026GetTypeCodeFixProvider>(source, fixedSource);

        // UPA0026 test case 1
        [Fact]
        public Task StructGetType_InUpdate_Triggers()
        {
            return VerifyAsync(@"
using UnityEngine;

struct Point { public int X; }

class C : MonoBehaviour
{
    Point p;

    void Update()
    {
        var t = {|UPA0026:p.GetType()|};
    }
}");
        }

        // UPA0026 test case 2
        [Fact]
        public Task EnumGetType_InUpdate_Triggers()
        {
            return VerifyAsync(@"
using UnityEngine;

enum State { Idle, Dead }

class C : MonoBehaviour
{
    State state;

    void Update()
    {
        var t = {|UPA0026:state.GetType()|};
    }
}");
        }

        // UPA0026 test case 3 — no receiver box on IL2CPP, so no longer reported.
        [Fact]
        public Task EnumToString_InUpdate_DoesNotTrigger()
        {
            return VerifyAsync(@"
using UnityEngine;

enum State { Idle, Dead }

class C : MonoBehaviour
{
    State state;

    void Update()
    {
        var s = state.ToString();
    }
}");
        }

        // UPA0026 test case 4
        [Fact]
        public Task StructGetHashCode_InUpdate_DoesNotTrigger()
        {
            return VerifyAsync(@"
using UnityEngine;

struct Point { public int X; }

class C : MonoBehaviour
{
    Point p;

    void Update()
    {
        var h = p.GetHashCode();
    }
}");
        }

        // UPA0026 test case 5 — the allocation here is the boxed argument, which UPA0006
        // reports at the conversion. The receiver is not boxed.
        [Fact]
        public Task StructEqualsObject_InUpdate_DoesNotTrigger()
        {
            return VerifyAsync(@"
using UnityEngine;

struct Point { public int X; }

class C : MonoBehaviour
{
    Point p;
    Point other;

    void Update()
    {
        var eq = p.Equals(other);
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
    void Update()
    {
        M(1);
    }

    [HotPath]
    void M<T>(T value) where T : struct
    {
        var t = value.GetType();
    }
}

class HotPathAttribute : System.Attribute { }");
        }

        // UPA0026 test case 8
        [Fact]
        public Task StructGetType_InStart_DoesNotTrigger()
        {
            return VerifyAsync(@"
using UnityEngine;

struct Point { public int X; }

class C : MonoBehaviour
{
    Point p;

    void Start()
    {
        var t = p.GetType();
    }
}");
        }

        // UPA0026 test case 9
        [Fact]
        public Task ReferenceTypeGetType_InUpdate_DoesNotTrigger()
        {
            return VerifyAsync(@"
using UnityEngine;

class Holder { }

class C : MonoBehaviour
{
    Holder h = new Holder();

    void Update()
    {
        var t = h.GetType();
    }
}");
        }

        // UPA0026 test case 10 — a local receiver is the plain case
        [Fact]
        public Task LocalReceiver_CodeFix_UsesTypeof()
        {
            return VerifyFixAsync(@"
using UnityEngine;

struct Point { public int X; }

class C : MonoBehaviour
{
    void Update()
    {
        var p = new Point();
        var t = {|UPA0026:p.GetType()|};
    }
}", @"
using UnityEngine;

struct Point { public int X; }

class C : MonoBehaviour
{
    void Update()
    {
        var p = new Point();
        var t = typeof(Point);
    }
}");
        }

        // UPA0026 test case 11
        [Fact]
        public Task FieldReceiver_CodeFix_UsesTypeof()
        {
            return VerifyFixAsync(@"
using UnityEngine;

struct Point { public int X; }

class C : MonoBehaviour
{
    Point p;

    void Update()
    {
        var t = {|UPA0026:p.GetType()|};
    }
}", @"
using UnityEngine;

struct Point { public int X; }

class C : MonoBehaviour
{
    Point p;

    void Update()
    {
        var t = typeof(Point);
    }
}");
        }

        // UPA0026 test case 12 — no receiver written at all, inside the struct's own method
        [Fact]
        public Task ImplicitThisReceiver_CodeFix_UsesTypeof()
        {
            return VerifyFixAsync(@"
class HotPathAttribute : System.Attribute { }

struct Point
{
    public int X;

    [HotPath]
    public System.Type Describe()
    {
        return {|UPA0026:GetType()|};
    }
}", @"
class HotPathAttribute : System.Attribute { }

struct Point
{
    public int X;

    [HotPath]
    public System.Type Describe()
    {
        return typeof(Point);
    }
}");
        }

        // UPA0026 test case 13 — a getter can do anything, and the rewrite would stop calling it
        [Fact]
        public Task PropertyReceiver_Triggers_WithoutFix()
        {
            const string Source = @"
using UnityEngine;

struct Point { public int X; }

class C : MonoBehaviour
{
    Point Current => new Point();

    void Update()
    {
        var t = {|UPA0026:Current.GetType()|};
    }
}";
            return VerifyFixAsync(Source, Source);
        }

        // UPA0026 test case 14 — dropping the call would change the number of calls from one
        // to none
        [Fact]
        public Task MethodCallReceiver_Triggers_WithoutFix()
        {
            const string Source = @"
using UnityEngine;

struct Point { public int X; }

class C : MonoBehaviour
{
    Point Make() => new Point();

    void Update()
    {
        var t = {|UPA0026:Make().GetType()|};
    }
}";
            return VerifyFixAsync(Source, Source);
        }

        // UPA0026 test case 15 — GetType on a nullable returns the underlying type, and throws
        // when there is no value. typeof cannot express either half.
        [Fact]
        public Task NullableReceiver_Triggers_WithoutFix()
        {
            const string Source = @"
using UnityEngine;

class C : MonoBehaviour
{
    int? maybe;

    void Update()
    {
        var t = {|UPA0026:maybe.GetType()|};
    }
}";
            return VerifyFixAsync(Source, Source);
        }

        // UPA0026 test case 16
        [Fact]
        public Task EnumReceiver_CodeFix_UsesTypeof()
        {
            return VerifyFixAsync(@"
using UnityEngine;

enum State { Idle, Dead }

class C : MonoBehaviour
{
    State state;

    void Update()
    {
        var t = {|UPA0026:state.GetType()|};
    }
}", @"
using UnityEngine;

enum State { Idle, Dead }

class C : MonoBehaviour
{
    State state;

    void Update()
    {
        var t = typeof(State);
    }
}");
        }

        // UPA0026 test case 18 - dropping holder.Value drops the NullReferenceException that
        // dereferencing a null holder would have thrown
        [Fact]
        public Task FieldOfReferenceTypeReceiver_Triggers_WithoutFix()
        {
            const string Source = @"
using UnityEngine;

struct Point { public int X; }

class Holder { public Point Value; }

class C : MonoBehaviour
{
    Holder holder;

    void Update()
    {
        var t = {|UPA0026:holder.Value.GetType()|};
    }
}";
            return VerifyFixAsync(Source, Source);
        }

        // UPA0026 test case 19 - a static field's type initializer runs on access, and the
        // rewrite would stop running it
        [Fact]
        public Task StaticFieldReceiver_Triggers_WithoutFix()
        {
            const string Source = @"
using UnityEngine;

struct Point { public int X; }

static class StaticHolder { public static Point Value = new Point(); }

class C : MonoBehaviour
{
    void Update()
    {
        var t = {|UPA0026:StaticHolder.Value.GetType()|};
    }
}";
            return VerifyFixAsync(Source, Source);
        }

        // UPA0026 test case 20 - this.field stays fixable: it cannot dereference null and has
        // no type initializer to skip
        [Fact]
        public Task ThisQualifiedFieldReceiver_CodeFix_UsesTypeof()
        {
            return VerifyFixAsync(@"
using UnityEngine;

struct Point { public int X; }

class C : MonoBehaviour
{
    Point p;

    void Update()
    {
        var t = {|UPA0026:this.p.GetType()|};
    }
}", @"
using UnityEngine;

struct Point { public int X; }

class C : MonoBehaviour
{
    Point p;

    void Update()
    {
        var t = typeof(Point);
    }
}");
        }
    }
}
