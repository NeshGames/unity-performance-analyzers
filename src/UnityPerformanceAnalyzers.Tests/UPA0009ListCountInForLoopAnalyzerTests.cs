using System.Threading.Tasks;
using Xunit;

namespace UnityPerformanceAnalyzers.Tests
{
    public class UPA0009ListCountInForLoopAnalyzerTests
    {
        private static Task VerifyAsync(string source) =>
            RuleVerifier.VerifyAsync<UPA0009ListCountInForLoopAnalyzer>(source, new RuleHarness
            {
                EnabledRules = { "UPA0009" },
            });

        // Same text on both sides asserts the diagnostic is reported and no fix is offered.
        private static Task VerifyFixAsync(string source, string fixedSource) =>
            RuleVerifier.VerifyCodeFixAsync<
                UPA0009ListCountInForLoopAnalyzer,
                CodeFixes.UPA0009HoistCountCodeFixProvider>(source, fixedSource, new RuleHarness
                {
                    EnabledRules = { "UPA0009" },
                });

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

        // UPA0009 test case 14
        [Fact]
        public Task CountInCondition_CodeFix_HoistsIntoLocal()
        {
            return VerifyFixAsync(@"
using System.Collections.Generic;
using UnityEngine;

class C : MonoBehaviour
{
    List<int> items = new List<int>();

    void Update()
    {
        for (int i = 0; i < {|UPA0009:items.Count|}; i++)
        {
        }
    }
}", @"
using System.Collections.Generic;
using UnityEngine;

class C : MonoBehaviour
{
    List<int> items = new List<int>();

    void Update()
    {
        int itemsCount = items.Count;
        for (int i = 0; i < itemsCount; i++)
        {
        }
    }
}");
        }

        // UPA0009 test case 15 - a name already in scope must not be shadowed
        [Fact]
        public Task CountInCondition_CodeFix_AvoidsNameCollision()
        {
            return VerifyFixAsync(@"
using System.Collections.Generic;
using UnityEngine;

class C : MonoBehaviour
{
    List<int> items = new List<int>();

    void Update()
    {
        int itemsCount = 3;
        for (int i = 0; i < {|UPA0009:items.Count|}; i++)
        {
        }

        _ = itemsCount;
    }
}", @"
using System.Collections.Generic;
using UnityEngine;

class C : MonoBehaviour
{
    List<int> items = new List<int>();

    void Update()
    {
        int itemsCount = 3;
        int itemsCount2 = items.Count;
        for (int i = 0; i < itemsCount2; i++)
        {
        }

        _ = itemsCount;
    }
}");
        }

        // UPA0009 test case 16 - hoisting past an embedded statement would mean synthesising
        // a block, which is a change to the shape of the code rather than to the expression
        [Fact]
        public Task EmbeddedForStatement_Triggers_WithoutFix()
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
            return VerifyFixAsync(Source, Source);
        }

        // UPA0009 test case 17 - the receiver written as this.items keeps the same local name
        [Fact]
        public Task ThisQualifiedReceiver_CodeFix_HoistsIntoLocal()
        {
            return VerifyFixAsync(@"
using System.Collections.Generic;
using UnityEngine;

class C : MonoBehaviour
{
    List<int> items = new List<int>();

    void Update()
    {
        for (int i = 0; i < {|UPA0009:this.items.Count|}; i++)
        {
        }
    }
}", @"
using System.Collections.Generic;
using UnityEngine;

class C : MonoBehaviour
{
    List<int> items = new List<int>();

    void Update()
    {
        int itemsCount = this.items.Count;
        for (int i = 0; i < itemsCount; i++)
        {
        }
    }
}");
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
    }
}
