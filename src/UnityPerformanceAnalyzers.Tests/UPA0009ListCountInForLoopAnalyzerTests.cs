using System.Threading.Tasks;
using Xunit;

namespace UnityPerformanceAnalyzers.Tests
{
    public class UPA0009ListCountInForLoopAnalyzerTests
    {
        private static Task VerifyAsync(string source) =>
            RuleVerifier.VerifyAsync<UPA0009ListCountInForLoopAnalyzer>(source);

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
        // UPA0009 test case 7 - the collection leaves as an argument, so anything could have
        // happened to it. Hoisting Count would be advice to break the program.
        [Fact]
        public Task BodyPassesListAsArgument_DoesNotTrigger()
        {
            return VerifyAsync(@"
using System.Collections.Generic;
using UnityEngine;

class C : MonoBehaviour
{
    List<int> items = new List<int>();

    void Mutate(List<int> target) { target.Add(0); }

    void Update()
    {
        for (int i = 0; i < items.Count; i++)
        {
            Mutate(items);
        }
    }
}");
        }

        // UPA0009 test case 8 - an alias. The old check compared receiver identifiers, so the
        // Add on 'other' read as a mutation of something else entirely.
        [Fact]
        public Task BodyAliasesList_DoesNotTrigger()
        {
            return VerifyAsync(@"
using System.Collections.Generic;
using UnityEngine;

class C : MonoBehaviour
{
    List<int> items = new List<int>();

    void Update()
    {
        for (int i = 0; i < items.Count; i++)
        {
            var other = items;
            other.Add(0);
        }
    }
}");
        }

        // UPA0009 test case 9 - the narrowing must not swallow the ordinary case. An element
        // assignment names the collection but cannot change how many things are in it.
        [Fact]
        public Task BodyAssignsElements_StillTriggers()
        {
            return VerifyAsync(@"
using System.Collections.Generic;
using UnityEngine;

class C : MonoBehaviour
{
    List<int> items = new List<int>();

    void Update()
    {
        for (int i = 0; i < {|UPA0009:items.Count|}; i++)
        {
            items[i] = items[i] + 1;
        }
    }
}");
        }

        // UPA0009 test case 10 - a reduced extension call. The collection sits in the
        // receiver position under a name no list of mutating methods could have contained,
        // and the extension is free to mutate it.
        [Fact]
        public Task BodyCallsExtensionOnList_DoesNotTrigger()
        {
            return VerifyAsync(@"
using System.Collections.Generic;
using UnityEngine;

static class ListExtensions
{
    public static void TrimToLimit(this List<int> source)
    {
        source.RemoveAt(source.Count - 1);
    }
}

class C : MonoBehaviour
{
    List<int> items = new List<int>();

    void Update()
    {
        for (int i = 0; i < items.Count; i++)
        {
            items.TrimToLimit();
        }
    }
}");
        }

        // UPA0009 test cases 11-13 - the same three escapes written as this.items. The
        // receiver name is normalised to "items", so the question is whether the body scan
        // still recognises the qualified form.
        [Theory]
        [InlineData("Mutate(this.items);")]
        [InlineData("var alias = this.items; alias.Add(0);")]
        [InlineData("this.items.TrimToLimit();")]
        public Task BodyEscapesThroughQualifiedReceiver_DoesNotTrigger(string body)
        {
            return VerifyAsync(@"
using System.Collections.Generic;
using UnityEngine;

static class ListExtensions
{
    public static void TrimToLimit(this List<int> source) { source.RemoveAt(0); }
}

class C : MonoBehaviour
{
    List<int> items = new List<int>();

    void Mutate(List<int> target) { target.Add(0); }

    void Update()
    {
        for (int i = 0; i < this.items.Count; i++)
        {
            " + body + @"
        }
    }
}");
        }

        // UPA0009 test case 16 - hoisting past an embedded statement would mean synthesising
        // a block, which is a change to the shape of the code rather than to the expression
        [Fact]
        public Task EmbeddedForStatement_Triggers()
        {
            const string Source = @"
using System.Collections.Generic;
using UnityEngine;

class C : MonoBehaviour
{
    List<int> items = new List<int>();
    bool ready;

    void Update()
    {
        if (ready)
            for (int i = 0; i < {|UPA0009:items.Count|}; i++)
            {
            }
    }
}";
            return VerifyAsync(Source);
        }

        // UPA0009 test case 13b - the initializer runs before the hoisted read would, so a
        // mutation there changes how many times the loop runs once Count is lifted out
        [Fact]
        public Task InitializerReachesList_DoesNotTrigger()
        {
            return VerifyAsync(@"
using System.Collections.Generic;
using UnityEngine;

class C : MonoBehaviour
{
    List<int> items = new List<int>();

    int Reset(List<int> target) { target.Clear(); return 0; }

    void Update()
    {
        for (int i = Reset(items); i < items.Count; i++)
        {
        }
    }
}");
        }

        // UPA0009 test case 13c - the incrementor runs between iterations and can do the same
        [Fact]
        public Task IncrementorReachesList_DoesNotTrigger()
        {
            return VerifyAsync(@"
using System.Collections.Generic;
using UnityEngine;

class C : MonoBehaviour
{
    List<int> items = new List<int>();

    int Bump(List<int> target) { target.Add(0); return 1; }

    void Update()
    {
        for (int i = 0; i < items.Count; i = Bump(items))
        {
        }
    }
}");
        }

        // A field is reachable from every method of its own type. Kill never sees the list
        // as an argument, yet it removes from it, so a hoisted count runs past the end.
        [Theory]
        [InlineData("Kill(_enemies[i]);")]
        [InlineData("this.Kill(_enemies[i]);")]
        [InlineData("Registry.Report(this);")]
        public Task FieldReceiver_OwnInstanceMethodMutates_DoesNotTrigger(string body)
        {
            return VerifyAsync(@"
using System.Collections.Generic;
using UnityEngine;

static class Registry
{
    public static void Report(C owner) { owner.Kill(0); }
}

class C : MonoBehaviour
{
    List<int> _enemies = new List<int>();

    public void Kill(int enemy) { _enemies.Remove(enemy); }

    void Update()
    {
        for (int i = 0; i < _enemies.Count; i++)
        {
            " + body + @"
        }
    }
}");
        }

        // A static list and a static method of the same type: no instance, same reach.
        [Fact]
        public Task StaticFieldReceiver_OwnStaticMethodMutates_DoesNotTrigger()
        {
            return VerifyAsync(@"
using System.Collections.Generic;
using UnityEngine;

class C : MonoBehaviour
{
    static List<int> s_all = new List<int>();

    static void Unregister(int item) { s_all.Remove(item); }

    void Update()
    {
        for (int i = 0; i < s_all.Count; i++)
        {
            Unregister(s_all[i]);
        }
    }
}");
        }

        // Replacing the receiver is a mutation too: after the assignment the loop reads a
        // different list, and a count hoisted before the loop belongs to the old one.
        [Theory]
        [InlineData("items = GetMore();")]
        [InlineData("this.items = GetMore();")]
        [InlineData("(items, n) = (GetMore(), 1);")]
        public Task ReceiverReassigned_DoesNotTrigger(string body)
        {
            return VerifyAsync(@"
using System.Collections.Generic;
using UnityEngine;

static class Source
{
    public static List<int> More() => new List<int>();
}

class C : MonoBehaviour
{
    List<int> items = new List<int>();
    int n;

    static List<int> GetMore() => Source.More();

    void Update()
    {
        for (int i = 0; i < items.Count; i++)
        {
            " + body + @"
        }
    }
}");
        }

        [Fact]
        public Task LocalReceiverReassigned_DoesNotTrigger()
        {
            return VerifyAsync(@"
using System.Collections.Generic;
using UnityEngine;

static class Source
{
    public static List<int> More() => new List<int>();
}

class C : MonoBehaviour
{
    void Update()
    {
        var items = new List<int>();
        for (int i = 0; i < items.Count; i++)
        {
            items = Source.More();
        }
    }
}");
        }

        // The local function is declared outside the loop, and captured the list there; the
        // call inside the loop names neither.
        [Theory]
        [InlineData("void Drop() => items.RemoveAt(0);", "Drop();")]
        [InlineData("System.Action drop = () => items.RemoveAt(0);", "drop();")]
        public Task CapturingCallableMutatesLocal_DoesNotTrigger(string declaration, string call)
        {
            return VerifyAsync(@"
using System.Collections.Generic;
using UnityEngine;

class C : MonoBehaviour
{
    void Update()
    {
        var items = new List<int>();
        " + declaration + @"
        for (int i = 0; i < items.Count; i++)
        {
            " + call + @"
        }
    }
}");
        }

        // The widening is scoped by what can reach the receiver. A local list is out of an
        // instance method's reach, and a call into an unrelated type is out of a field's, so
        // both still report.
        [Fact]
        public Task CallsThatCannotReachReceiver_StillTrigger()
        {
            return VerifyAsync(@"
using System.Collections.Generic;
using UnityEngine;

class C : MonoBehaviour
{
    List<int> _weights = new List<int>();
    int _total;

    void Track(int value) { _total += value; }

    void Update()
    {
        var local = new List<int>();
        for (int i = 0; i < {|UPA0009:local.Count|}; i++)
        {
            Track(local[i]);
        }

        for (int i = 0; i < {|UPA0009:_weights.Count|}; i++)
        {
            _total += System.Math.Abs(_weights[i]);
        }
    }
}");
        }
    }
}
