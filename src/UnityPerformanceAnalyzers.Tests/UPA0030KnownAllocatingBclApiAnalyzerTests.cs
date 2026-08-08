using System.Threading.Tasks;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Xunit;

namespace UnityPerformanceAnalyzers.Tests
{
    public class UPA0030KnownAllocatingBclApiAnalyzerTests
    {
        private static Task VerifyAsync(string source)
        {
            var test = new CSharpAnalyzerTest<UPA0030KnownAllocatingBclApiAnalyzer, DefaultVerifier>
            {
                TestCode = source,
                ReferenceAssemblies = ReferenceAssemblies.NetStandard.NetStandard20,
            };
            test.TestState.AdditionalReferences.Add(typeof(UnityEngine.MonoBehaviour).Assembly);
            return test.RunAsync();
        }

        // UPA0030 test case 1
        [Fact]
        public Task StringSplit_InUpdate_Triggers()
        {
            return VerifyAsync(@"
using UnityEngine;

class C : MonoBehaviour
{
    string source;

    void Update()
    {
        var parts = {|UPA0030:source.Split(',')|};
    }
}");
        }

        // UPA0030 test case 2
        [Fact]
        public Task StringSubstring_InUpdate_Triggers()
        {
            return VerifyAsync(@"
using UnityEngine;

class C : MonoBehaviour
{
    string source;

    void Update()
    {
        var head = {|UPA0030:source.Substring(0, 3)|};
    }
}");
        }

        // UPA0030 test case 3
        [Fact]
        public Task StringToLowerInvariant_InUpdate_Triggers()
        {
            return VerifyAsync(@"
using UnityEngine;

class C : MonoBehaviour
{
    string source;

    void Update()
    {
        var lower = {|UPA0030:source.ToLowerInvariant()|};
    }
}");
        }

        // UPA0030 test case 4
        [Fact]
        public Task EnumGetValues_InUpdate_Triggers()
        {
            return VerifyAsync(@"
using System;
using UnityEngine;

enum State { Idle, Running }

class C : MonoBehaviour
{
    void Update()
    {
        var values = {|UPA0030:Enum.GetValues(typeof(State))|};
    }
}");
        }

        // UPA0030 test case 5
        [Fact]
        public Task StringSplit_InStart_DoesNotTrigger()
        {
            return VerifyAsync(@"
using UnityEngine;

class C : MonoBehaviour
{
    string source;

    void Start()
    {
        var parts = source.Split(',');
    }
}");
        }

        // UPA0030 test case 6 — declaring-type comparison, not name matching.
        [Fact]
        public Task UserDefinedSplitExtension_InUpdate_DoesNotTrigger()
        {
            return VerifyAsync(@"
using UnityEngine;

static class Extensions
{
    public static int Split(this string value, char separator, int limit) => 0;
}

class C : MonoBehaviour
{
    string source;

    void Update()
    {
        var count = source.Split(',', 2);
    }
}");
        }

        // UPA0030 test case 7 — HasFlag belongs to UPA0022; the dedup has to hold.
        [Fact]
        public Task EnumHasFlag_InUpdate_DoesNotTrigger()
        {
            return VerifyAsync(@"
using System;
using UnityEngine;

[Flags]
enum State { None = 0, Dead = 1 }

class C : MonoBehaviour
{
    State state;

    void Update()
    {
        var dead = state.HasFlag(State.Dead);
    }
}");
        }

        // UPA0030 test case 8 — LINQ materializers belong to UPA0013.
        [Fact]
        public Task LinqToArray_InUpdate_DoesNotTrigger()
        {
            return VerifyAsync(@"
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

class C : MonoBehaviour
{
    List<int> values;

    void Update()
    {
        var array = values.ToArray();
    }
}");
        }

        // UPA0030 test case 9 — Physics queries belong to the Unity analyzers.
        [Fact]
        public Task RaycastAll_InUpdate_DoesNotTrigger()
        {
            return VerifyAsync(@"
using UnityEngine;

class C : MonoBehaviour
{
    Vector3 origin, direction;

    void Update()
    {
        var hits = Physics.RaycastAll(origin, direction);
    }
}");
        }

        // UPA0030 test case 10 — Unity members belong to UPA0018.
        [Fact]
        public Task TextureGetPixels_InUpdate_DoesNotTrigger()
        {
            return VerifyAsync(@"
using UnityEngine;

class C : MonoBehaviour
{
    Texture2D texture;

    void Update()
    {
        var pixels = texture.GetPixels();
    }
}");
        }

        // Documented no-op: Substring(0) returns the receiver, so there is nothing to report.
        [Fact]
        public Task StringSubstringFromZero_InUpdate_DoesNotTrigger()
        {
            return VerifyAsync(@"
using UnityEngine;

class C : MonoBehaviour
{
    string source;

    void Update()
    {
        var same = source.Substring(0);
    }
}");
        }

        // A non-constant start index could be anything, so the report stands.
        [Fact]
        public Task StringSubstringFromVariable_InUpdate_Triggers()
        {
            return VerifyAsync(@"
using UnityEngine;

class C : MonoBehaviour
{
    string source;
    int start;

    void Update()
    {
        var tail = {|UPA0030:source.Substring(start)|};
    }
}");
        }

        // UPA0030 test case 11 — the trim variants are listed too.
        [Fact]
        public Task StringTrim_InUpdate_Triggers()
        {
            return VerifyAsync(@"
using UnityEngine;

class C : MonoBehaviour
{
    string source;

    void Update()
    {
        var trimmed = {|UPA0030:source.Trim()|};
    }
}");
        }
    }
}
