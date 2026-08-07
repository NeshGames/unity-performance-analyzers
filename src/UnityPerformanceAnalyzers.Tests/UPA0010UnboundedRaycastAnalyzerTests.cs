using System.Threading.Tasks;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Xunit;

namespace UnityPerformanceAnalyzers.Tests
{
    public class UPA0010UnboundedRaycastAnalyzerTests
    {
        private static Task VerifyAsync(string source)
        {
            var test = new CSharpAnalyzerTest<UPA0010UnboundedRaycastAnalyzer, DefaultVerifier>
            {
                TestCode = source,
                ReferenceAssemblies = ReferenceAssemblies.NetStandard.NetStandard20,
            };
            test.TestState.AdditionalReferences.Add(typeof(UnityEngine.MonoBehaviour).Assembly);
            return test.RunAsync();
        }

        // UPA0010 test case 1 — neither maxDistance nor layerMask
        [Fact]
        public Task Raycast_NoDistanceNoMask_Triggers()
        {
            return VerifyAsync(@"
using UnityEngine;

class C
{
    void M(Vector3 origin, Vector3 dir)
    {
        {|UPA0010:Physics.Raycast(origin, dir, out var hit)|};
    }
}");
        }

        // UPA0010 test case 2 — maxDistance present, layerMask missing
        [Fact]
        public Task Raycast_DistanceOnly_Triggers()
        {
            return VerifyAsync(@"
using UnityEngine;

class C
{
    void M(Vector3 origin, Vector3 dir)
    {
        {|UPA0010:Physics.Raycast(origin, dir, out var hit, 10f)|};
    }
}");
        }

        // UPA0010 test case 3 — fully bounded
        [Fact]
        public Task Raycast_DistanceAndMask_DoesNotTrigger()
        {
            return VerifyAsync(@"
using UnityEngine;

class C
{
    void M(Vector3 origin, Vector3 dir, int mask)
    {
        Physics.Raycast(origin, dir, out var hit, 10f, mask);
    }
}");
        }

        // UPA0010 test case 4
        [Fact]
        public Task RaycastAll_NoDistanceNoMask_Triggers()
        {
            return VerifyAsync(@"
using UnityEngine;

class C
{
    void M(Vector3 origin, Vector3 dir)
    {
        var hits = {|UPA0010:Physics.RaycastAll(origin, dir)|};
    }
}");
        }

        // UPA0010 test case 5 — unrelated type with the same method name
        [Fact]
        public Task CustomRaycast_DoesNotTrigger()
        {
            return VerifyAsync(@"
static class MyPhysics
{
    public static bool Raycast(int x) => false;
}

class C
{
    void M()
    {
        MyPhysics.Raycast(1);
    }
}");
        }

        // Ray-based overloads are covered by the same parameter-name matching
        [Fact]
        public Task Raycast_RayOverloadWithoutBounds_Triggers()
        {
            return VerifyAsync(@"
using UnityEngine;

class C
{
    void M(Ray ray)
    {
        {|UPA0010:Physics.Raycast(ray, out var hit)|};
    }
}");
        }
    }
}
