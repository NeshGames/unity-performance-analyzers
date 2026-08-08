using System.Threading.Tasks;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Xunit;

namespace UnityPerformanceAnalyzers.Tests
{
    public class UPA0029SequentialAddAnalyzerTests
    {
        private static Task VerifyAsync(string source)
        {
            var test = new CSharpAnalyzerTest<UPA0029SequentialAddAnalyzer, DefaultVerifier>
            {
                TestCode = source,
                ReferenceAssemblies = ReferenceAssemblies.NetStandard.NetStandard20,
            };
            return test.RunAsync();
        }

        // UPA0029 test case 1
        [Fact]
        public Task ForEachOverList_Triggers()
        {
            return VerifyAsync(@"
using System.Collections.Generic;

class C
{
    void Copy(List<int> source, List<int> target)
    {
        {|UPA0029:foreach (var item in source)
            target.Add(item);|}
    }
}");
        }

        // UPA0029 test case 2 — AddRange cannot pre-size an IEnumerable, so there is nothing
        // to gain and the rule stays quiet.
        [Fact]
        public Task ForEachOverEnumerable_DoesNotTrigger()
        {
            return VerifyAsync(@"
using System.Collections.Generic;

class C
{
    void Copy(IEnumerable<int> source, List<int> target)
    {
        foreach (var item in source)
            target.Add(item);
    }
}");
        }

        // UPA0029 test case 3
        [Fact]
        public Task ForEachOverLinqQuery_DoesNotTrigger()
        {
            return VerifyAsync(@"
using System.Collections.Generic;
using System.Linq;

class C
{
    void Copy(List<int> source, List<int> target)
    {
        foreach (var item in source.Where(x => x > 0))
            target.Add(item);
    }
}");
        }

        // UPA0029 test case 4 — arrays implement ICollection<T>.
        [Fact]
        public Task ForEachOverArray_Triggers()
        {
            return VerifyAsync(@"
using System.Collections.Generic;

class C
{
    void Copy(int[] source, List<int> target)
    {
        {|UPA0029:foreach (var item in source)
            target.Add(item);|}
    }
}");
        }

        // UPA0029 test case 5
        [Fact]
        public Task ForEachAddingMember_DoesNotTrigger()
        {
            return VerifyAsync(@"
using System.Collections.Generic;

class Item { public int Id; }

class C
{
    void Copy(List<Item> source, List<int> target)
    {
        foreach (var item in source)
            target.Add(item.Id);
    }
}");
        }

        // UPA0029 test case 6
        [Fact]
        public Task ForEachWithFilter_DoesNotTrigger()
        {
            return VerifyAsync(@"
using System.Collections.Generic;

class C
{
    void Copy(List<int> source, List<int> target)
    {
        foreach (var item in source)
        {
            if (item > 0)
                target.Add(item);
        }
    }
}");
        }

        // UPA0029 test case 7
        [Fact]
        public Task ForEachWithExtraStatement_DoesNotTrigger()
        {
            return VerifyAsync(@"
using System.Collections.Generic;

class C
{
    int count;

    void Copy(List<int> source, List<int> target)
    {
        foreach (var item in source)
        {
            target.Add(item);
            count++;
        }
    }
}");
        }

        // UPA0029 test case 8 — the indexed form of the same copy.
        [Fact]
        public Task IndexedForOverList_Triggers()
        {
            return VerifyAsync(@"
using System.Collections.Generic;

class C
{
    void Copy(List<int> source, List<int> target)
    {
        {|UPA0029:for (int i = 0; i < source.Count; i++)
            target.Add(source[i]);|}
    }
}");
        }

        // UPA0029 test case 9 — HashSet has no AddRange taking IEnumerable.
        [Fact]
        public Task ForEachIntoHashSet_DoesNotTrigger()
        {
            return VerifyAsync(@"
using System.Collections.Generic;

class C
{
    void Copy(List<int> source, HashSet<int> target)
    {
        foreach (var item in source)
            target.Add(item);
    }
}");
        }

        // A for loop bounded by something other than the source length is not a full copy.
        [Fact]
        public Task IndexedForWithDifferentBound_DoesNotTrigger()
        {
            return VerifyAsync(@"
using System.Collections.Generic;

class C
{
    void Copy(List<int> source, List<int> target, int limit)
    {
        for (int i = 0; i < limit; i++)
            target.Add(source[i]);
    }
}");
        }

        // Self-copy: the loop throws today (the collection is modified while enumerating),
        // and AddRange would quietly succeed. Turning broken code into working code is still
        // a behaviour change, so the rule does not offer it.
        [Fact]
        public Task ForEachCopyingCollectionIntoItself_DoesNotTrigger()
        {
            return VerifyAsync(@"
using System.Collections.Generic;

class C
{
    void Duplicate(List<int> items)
    {
        foreach (var item in items)
            items.Add(item);
    }
}");
        }

        // The indexed form of the same thing never terminates. AddRange would make it finite.
        [Fact]
        public Task IndexedForCopyingCollectionIntoItself_DoesNotTrigger()
        {
            return VerifyAsync(@"
using System.Collections.Generic;

class C
{
    void Duplicate(List<int> items)
    {
        for (int i = 0; i < items.Count; i++)
            items.Add(items[i]);
    }
}");
        }

        // A receiver that is re-evaluated per iteration may hand back a different collection
        // each time; the rewrite would call it once.
        [Fact]
        public Task AddOnMethodCallReceiver_DoesNotTrigger()
        {
            return VerifyAsync(@"
using System.Collections.Generic;

class C
{
    List<int> GetTarget() => new List<int>();

    void Copy(List<int> source)
    {
        foreach (var item in source)
            GetTarget().Add(item);
    }
}");
        }

        // Same member, different receiver: the loop copies as many elements as the left
        // instance has, from the right one. Comparing only the terminal member would call
        // those the same collection.
        [Fact]
        public Task IndexedForBoundedByAnotherInstancesProperty_DoesNotTrigger()
        {
            return VerifyAsync(@"
using System.Collections.Generic;

class Holder { public List<int> Items = new List<int>(); }

class C
{
    void Copy(Holder left, Holder right, List<int> target)
    {
        for (int i = 0; i < left.Items.Count; i++)
            target.Add(right.Items[i]);
    }
}");
        }

        // The same receiver chain on both sides is still a real copy.
        [Fact]
        public Task IndexedForOverSameInstanceField_Triggers()
        {
            return VerifyAsync(@"
using System.Collections.Generic;

class Holder { public List<int> Items = new List<int>(); }

class C
{
    void Copy(Holder holder, List<int> target)
    {
        {|UPA0029:for (int i = 0; i < holder.Items.Count; i++)
            target.Add(holder.Items[i]);|}
    }
}");
        }

        // A property getter can return a different collection on every access, and nothing in
        // the symbol distinguishes an auto-property from a computed one. Not reported.
        [Fact]
        public Task AddOnPropertyTarget_DoesNotTrigger()
        {
            return VerifyAsync(@"
using System.Collections.Generic;

class C
{
    List<int> Target => new List<int>();

    void Copy(List<int> source)
    {
        foreach (var item in source)
            Target.Add(item);
    }
}");
        }

        // Same for a property-backed source: the getter runs once per iteration today.
        [Fact]
        public Task ForEachOverPropertySource_DoesNotTrigger()
        {
            return VerifyAsync(@"
using System.Collections.Generic;

class Holder { public List<int> Items { get; } = new List<int>(); }

class C
{
    void Copy(Holder holder, List<int> target)
    {
        foreach (var item in holder.Items)
            target.Add(item);
    }
}");
        }

        // A field target reached through `this` is a stable reference and still reports.
        [Fact]
        public Task AddOnFieldTarget_Triggers()
        {
            return VerifyAsync(@"
using System.Collections.Generic;

class C
{
    readonly List<int> _target = new List<int>();

    void Copy(List<int> source)
    {
        {|UPA0029:foreach (var item in source)
            _target.Add(item);|}
    }
}");
        }

        // A custom AddRange is free to do something other than repeated Add calls, and the
        // element types need not line up — here the rewrite would not even compile.
        [Fact]
        public Task CustomTargetWithMismatchedAddRange_DoesNotTrigger()
        {
            return VerifyAsync(@"
using System.Collections.Generic;

class Custom
{
    public void Add(int value) { }
    public void AddRange(IEnumerable<string> values) { }
}

class C
{
    void Copy(List<int> source, Custom target)
    {
        foreach (var item in source)
            target.Add(item);
    }
}");
        }

        // Even a type-compatible custom AddRange is out of scope: only List<T> is known to
        // behave the same as the loop it replaces.
        [Fact]
        public Task CustomTargetWithMatchingAddRange_DoesNotTrigger()
        {
            return VerifyAsync(@"
using System.Collections.Generic;

class Custom
{
    public void Add(int value) { }
    public void AddRange(IEnumerable<int> values) { }
}

class C
{
    void Copy(List<int> source, Custom target)
    {
        foreach (var item in source)
            target.Add(item);
    }
}");
        }

        // A custom ICollection<T> can make CopyTo and GetEnumerator disagree, and AddRange
        // takes the CopyTo path. Only arrays and List<T> are known to behave the same.
        [Fact]
        public Task ForEachOverCustomCollection_DoesNotTrigger()
        {
            return VerifyAsync(@"
using System.Collections;
using System.Collections.Generic;

class Custom : ICollection<int>
{
    public int Count => 0;
    public bool IsReadOnly => false;
    public void Add(int item) { }
    public void Clear() { }
    public bool Contains(int item) => false;
    public void CopyTo(int[] array, int index) { }
    public bool Remove(int item) => false;
    public IEnumerator<int> GetEnumerator() => null;
    IEnumerator IEnumerable.GetEnumerator() => null;
}

class C
{
    void Copy(Custom source, List<int> target)
    {
        foreach (var item in source)
            target.Add(item);
    }
}");
        }

        // A loop that does not start at zero skips elements, so AddRange would change it.
        [Fact]
        public Task IndexedForStartingAtOne_DoesNotTrigger()
        {
            return VerifyAsync(@"
using System.Collections.Generic;

class C
{
    void Copy(List<int> source, List<int> target)
    {
        for (int i = 1; i < source.Count; i++)
            target.Add(source[i]);
    }
}");
        }
    }
}
