using System.Threading.Tasks;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Xunit;

namespace UnityPerformanceAnalyzers.Tests
{
    public class UPA0028ValueTypeCollectionKeyAnalyzerTests
    {
        private const string Types = @"
using System;
using System.Collections.Generic;

struct PlainStruct { public int X; }

struct PartialStruct : IEquatable<PartialStruct>
{
    public int X;
    public bool Equals(PartialStruct other) => X == other.X;
}

struct EquatableStruct : IEquatable<EquatableStruct>
{
    public int X;
    public bool Equals(EquatableStruct other) => X == other.X;
    public override bool Equals(object obj) => obj is EquatableStruct other && Equals(other);
    public override int GetHashCode() => X;
}

struct WrongArgumentStruct : IEquatable<PlainStruct>
{
    public int X;
    public bool Equals(PlainStruct other) => false;
    public override bool Equals(object obj) => false;
    public override int GetHashCode() => X;
}

enum MyEnum { A, B }

class MyClass { }

class MyComparer : IEqualityComparer<PlainStruct>
{
    public bool Equals(PlainStruct a, PlainStruct b) => true;
    public int GetHashCode(PlainStruct value) => 0;
}
";

        private static Task VerifyAsync(string source)
        {
            var test = new CSharpAnalyzerTest<UPA0028ValueTypeCollectionKeyAnalyzer, DefaultVerifier>
            {
                TestCode = Types + source,
                ReferenceAssemblies = ReferenceAssemblies.NetStandard.NetStandard20,
            };
            return test.RunAsync();
        }

        // UPA0028 test case 1
        [Fact]
        public Task PlainStructDictionaryDeclaration_Triggers()
        {
            return VerifyAsync(@"
class C
{
    Dictionary<PlainStruct, int> map = {|UPA0028:new Dictionary<PlainStruct, int>()|};
}");
        }

        // UPA0028 test case 2
        [Fact]
        public Task EquatableStructDictionary_DoesNotTrigger()
        {
            return VerifyAsync(@"
class C
{
    Dictionary<EquatableStruct, int> map = new Dictionary<EquatableStruct, int>();
}");
        }

        // UPA0028 test case 3
        [Fact]
        public Task StructWithoutGetHashCode_TriggersHashCodeVariant()
        {
            return VerifyAsync(@"
class C
{
    Dictionary<PartialStruct, int> map = {|UPA0028:new Dictionary<PartialStruct, int>()|};
}");
        }

        // UPA0028 test case 4 — passing a comparer is the fix, so neither the creation nor
        // the declaration it initializes reports.
        [Fact]
        public Task DictionaryConstructedWithComparer_DoesNotTrigger()
        {
            return VerifyAsync(@"
class C
{
    Dictionary<PlainStruct, int> map = new Dictionary<PlainStruct, int>(new MyComparer());
}");
        }

        // UPA0028 test case 5
        [Fact]
        public Task PlainStructHashSet_Triggers()
        {
            return VerifyAsync(@"
class C
{
    HashSet<PlainStruct> set = {|UPA0028:new HashSet<PlainStruct>()|};
}");
        }

        // UPA0028 test case 6 — a List is only a problem when something searches it.
        [Fact]
        public Task PlainStructListWithoutSearch_DoesNotTrigger()
        {
            return VerifyAsync(@"
class C
{
    List<PlainStruct> values;

    void Use() { values.Add(default); }
}");
        }

        // UPA0028 test case 7
        [Fact]
        public Task PlainStructListContains_TriggersAtTheCall()
        {
            return VerifyAsync(@"
class C
{
    List<PlainStruct> values;

    void Use() { var found = {|UPA0028:values.Contains(default)|}; }
}");
        }

        // UPA0028 test case 8 — enums are the other rule's territory, and measurement says
        // they do not box at all.
        [Fact]
        public Task EnumDictionary_DoesNotTrigger()
        {
            return VerifyAsync(@"
class C
{
    Dictionary<MyEnum, int> map = new Dictionary<MyEnum, int>();
}");
        }

        // UPA0028 test case 9
        [Fact]
        public Task ClassDictionary_DoesNotTrigger()
        {
            return VerifyAsync(@"
class C
{
    Dictionary<MyClass, int> map = new Dictionary<MyClass, int>();
}");
        }

        // UPA0028 test case 10 — sorted collections use IComparer, a different question.
        [Fact]
        public Task SortedDictionary_DoesNotTrigger()
        {
            return VerifyAsync(@"
class C
{
    SortedDictionary<PlainStruct, int> map = new SortedDictionary<PlainStruct, int>();
}");
        }

        // UPA0028 test case 11 — IEquatable of some other type does not help the comparer.
        [Fact]
        public Task StructImplementingEquatableOfAnotherType_Triggers()
        {
            return VerifyAsync(@"
class C
{
    Dictionary<WrongArgumentStruct, int> map = {|UPA0028:new Dictionary<WrongArgumentStruct, int>()|};
}");
        }

        // A field built elsewhere may well use a custom comparer; the declaration cannot say.
        [Fact]
        public Task DeclarationWithoutCreation_DoesNotTrigger()
        {
            return VerifyAsync(@"
class C
{
    Dictionary<PlainStruct, int> map;

    C(Dictionary<PlainStruct, int> configured) { map = configured; }

    void Use(Dictionary<PlainStruct, int> other) { }
}");
        }

        // Naming the default comparer does not make it a different comparer.
        [Fact]
        public Task DictionaryConstructedWithExplicitDefaultComparer_Triggers()
        {
            return VerifyAsync(@"
class C
{
    Dictionary<PlainStruct, int> map =
        {|UPA0028:new Dictionary<PlainStruct, int>(EqualityComparer<PlainStruct>.Default)|};
}");
        }

        [Fact]
        public Task HashSetConstructedWithExplicitDefaultComparer_Triggers()
        {
            return VerifyAsync(@"
class C
{
    HashSet<PlainStruct> set = {|UPA0028:new HashSet<PlainStruct>(EqualityComparer<PlainStruct>.Default)|};
}");
        }

        // Target-typed new: the type name is in the declaration, not the creation.
        [Fact]
        public Task TargetTypedNew_Triggers()
        {
            return VerifyAsync(@"
class C
{
    Dictionary<PlainStruct, int> map = {|UPA0028:new()|};
}");
        }

        [Fact]
        public Task TargetTypedNewWithComparer_DoesNotTrigger()
        {
            return VerifyAsync(@"
class C
{
    Dictionary<PlainStruct, int> map = new(new MyComparer());
}");
        }

        // Nullable<T> has a dedicated comparer that defers to the underlying type, so a
        // nullable primitive key costs no more than the primitive itself.
        [Fact]
        public Task NullablePrimitiveKey_DoesNotTrigger()
        {
            return VerifyAsync(@"
class C
{
    Dictionary<int?, int> map = new Dictionary<int?, int>();
}");
        }

        [Fact]
        public Task NullableEquatableStructKey_DoesNotTrigger()
        {
            return VerifyAsync(@"
class C
{
    Dictionary<EquatableStruct?, int> map = new Dictionary<EquatableStruct?, int>();
}");
        }

        // The underlying type still decides: a nullable plain struct is as bad as the struct.
        [Fact]
        public Task NullablePlainStructKey_Triggers()
        {
            return VerifyAsync(@"
class C
{
    Dictionary<PlainStruct?, int> map = {|UPA0028:new Dictionary<PlainStruct?, int>()|};
}");
        }

        // A linear search compares and never hashes, so a missing GetHashCode costs nothing.
        [Fact]
        public Task EquatableStructWithoutHashCode_InListContains_DoesNotTrigger()
        {
            return VerifyAsync(@"
class C
{
    List<PartialStruct> values;

    void Use() { var found = values.Contains(default); }
}");
        }

        // UPA0028 test case 12 — whether a type parameter boxes depends on the instantiation.
        [Fact]
        public Task TypeParameterKey_DoesNotTrigger()
        {
            return VerifyAsync(@"
class C
{
    void Use<T>() where T : struct
    {
        var map = new Dictionary<T, int>();
    }
}");
        }

        // One collection, one diagnostic: the type is written twice here and reported once.
        [Fact]
        public Task DeclarationWithCreationInitializer_ReportsOnceAtTheCreation()
        {
            return VerifyAsync(@"
class C
{
    Dictionary<PlainStruct, int> map = {|UPA0028:new Dictionary<PlainStruct, int>()|};
}");
        }

        // Choosing the comparer overload is not the same as supplying a comparer: a null
        // argument leaves EqualityComparer<T>.Default in place, which is the whole problem.
        [Fact]
        public Task DictionaryConstructedWithNullComparer_Triggers()
        {
            return VerifyAsync(@"
class C
{
    Dictionary<PlainStruct, int> map =
        {|UPA0028:new Dictionary<PlainStruct, int>((IEqualityComparer<PlainStruct>)null)|};
}");
        }

        [Fact]
        public Task DictionaryConstructedWithDefaultComparer_Triggers()
        {
            return VerifyAsync(@"
class C
{
    Dictionary<PlainStruct, int> map =
        {|UPA0028:new Dictionary<PlainStruct, int>(default(IEqualityComparer<PlainStruct>))|};
}");
        }

        [Fact]
        public Task ArrayIndexOf_Triggers()
        {
            return VerifyAsync(@"
class C
{
    PlainStruct[] values;

    void Use() { var i = {|UPA0028:Array.IndexOf(values, default(PlainStruct))|}; }
}");
        }
    }
}
