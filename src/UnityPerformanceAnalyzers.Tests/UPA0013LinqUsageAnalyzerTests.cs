using System.Threading.Tasks;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Xunit;

namespace UnityPerformanceAnalyzers.Tests
{
    public class UPA0013LinqUsageAnalyzerTests
    {
        private static Task VerifyAsync(string source)
        {
            var test = new CSharpAnalyzerTest<UPA0013LinqUsageAnalyzer, DefaultVerifier>
            {
                TestCode = source,
                ReferenceAssemblies = ReferenceAssemblies.NetStandard.NetStandard20,
            };
            test.TestState.AdditionalReferences.Add(typeof(UnityEngine.MonoBehaviour).Assembly);
            // UPA0013 is disabled by default; enable it the same way a preset would.
            test.TestState.AnalyzerConfigFiles.Add(("/.editorconfig", @"
root = true

[*.cs]
dotnet_diagnostic.UPA0013.severity = warning
"));
            return test.RunAsync();
        }

        // UPA0013 test case 1 — both calls in the chain report, on the method names
        [Fact]
        public Task WhereToList_InUpdate_TriggersTwice()
        {
            return VerifyAsync(@"
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

class C : MonoBehaviour
{
    List<int> list = new List<int>();

    void Update()
    {
        var result = list.{|UPA0013:Where|}(x => x > 0).{|UPA0013:ToList|}();
        _ = result;
    }
}");
        }

        // UPA0013 test case 2
        [Fact]
        public Task EquivalentForLoop_DoesNotTrigger()
        {
            return VerifyAsync(@"
using System.Collections.Generic;
using UnityEngine;

class C : MonoBehaviour
{
    List<int> list = new List<int>();
    List<int> result = new List<int>();

    void Update()
    {
        result.Clear();
        for (var i = 0; i < list.Count; i++)
        {
            if (list[i] > 0)
            {
                result.Add(list[i]);
            }
        }
    }
}");
        }

        // UPA0013 test case 3 — same-named extension on a user type is excluded
        [Fact]
        public Task CustomWhereExtension_InUpdate_DoesNotTrigger()
        {
            return VerifyAsync(@"
using System;
using UnityEngine;

class Query { }

static class QueryExtensions
{
    public static Query Where(this Query query, Func<int, bool> predicate) => query;
}

class C : MonoBehaviour
{
    Query query = new Query();

    void Update()
    {
        var result = query.Where(x => x > 0);
        _ = result;
    }
}");
        }

        // UPA0013 test case 4 — query syntax compiles into the same Enumerable methods
        [Fact]
        public Task QuerySyntax_InUpdate_Triggers()
        {
            return VerifyAsync(@"
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

class C : MonoBehaviour
{
    List<int> list = new List<int>();

    void Update()
    {
        var result = from x in list {|UPA0013:select x|};
        _ = result;
    }
}");
        }

        // UPA0013 test case 5 — one-shot/initialization LINQ is acceptable
        [Fact]
        public Task Linq_InStart_DoesNotTrigger()
        {
            return VerifyAsync(@"
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

class C : MonoBehaviour
{
    List<int> list = new List<int>();

    void Start()
    {
        var result = list.Where(x => x > 0).ToList();
        _ = result;
    }
}");
        }

        // isEnabledByDefault: false — asserted on the descriptor because the
        // testing framework force-enables disabled-by-default rules when running analyzers.
        [Fact]
        public void Descriptor_IsDisabledByDefault()
        {
            var descriptor = Assert.Single(new UPA0013LinqUsageAnalyzer().SupportedDiagnostics);
            Assert.False(descriptor.IsEnabledByDefault);
        }
    }
}
