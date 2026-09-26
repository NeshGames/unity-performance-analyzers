using System.Threading.Tasks;
using Xunit;

namespace UnityPerformanceAnalyzers.Tests
{
    public class UPA0021MagnitudeComparisonAnalyzerTests
    {
        private static Task VerifyAsync(string source) =>
            RuleVerifier.VerifyAsync<UPA0021MagnitudeComparisonAnalyzer>(source);

        // UPA0021 test case 2 — triggers, but no fix offered (threshold is not a literal)
        [Fact]
        public Task DistanceAgainstVariable_Triggers()
        {
            return VerifyAsync(@"
using UnityEngine;

class C : MonoBehaviour
{
    Vector3 a;
    Vector3 b;
    float range;

    void Check()
    {
        if ({|UPA0021:Vector3.Distance(a, b) > range|})
        {
        }
    }
}");
        }

        // UPA0021 test case 3
        [Fact]
        public Task MagnitudeOnBothSides_DoesNotTrigger()
        {
            return VerifyAsync(@"
using UnityEngine;

class C : MonoBehaviour
{
    Vector3 a;
    Vector3 b;

    void Check()
    {
        if (a.magnitude < b.magnitude)
        {
        }
    }
}");
        }

        // UPA0021 test case 4
        [Fact]
        public Task MagnitudeOutsideComparison_DoesNotTrigger()
        {
            return VerifyAsync(@"
using UnityEngine;

class C : MonoBehaviour
{
    Vector3 v;

    void Check()
    {
        float d = v.magnitude;
        _ = d;
    }
}");
        }

        // UPA0021 test case 5 — negative threshold parses as unary minus: report, no fix
        [Fact]
        public Task MagnitudeAgainstNegativeLiteral_Triggers()
        {
            return VerifyAsync(@"
using UnityEngine;

class C : MonoBehaviour
{
    Vector3 v;

    void Check()
    {
        if ({|UPA0021:v.magnitude < -1f|})
        {
        }
    }
}");
        }
    }
}
