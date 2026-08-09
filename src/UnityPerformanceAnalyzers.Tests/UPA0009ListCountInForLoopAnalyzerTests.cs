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

    }
}
