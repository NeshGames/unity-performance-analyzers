using System.Threading.Tasks;
using UnityPerformanceAnalyzers.CodeFixes;
using Xunit;

namespace UnityPerformanceAnalyzers.Tests
{
    public class UPA0021MagnitudeComparisonAnalyzerTests
    {
        private static Task VerifyAsync(string source) =>
            RuleVerifier.VerifyAsync<UPA0021MagnitudeComparisonAnalyzer>(source);

        private static Task VerifyFixAsync(string source, string fixedSource)
        {
            return RuleVerifier.VerifyCodeFixAsync<
                UPA0021MagnitudeComparisonAnalyzer,
                UPA0021MagnitudeComparisonCodeFixProvider>(source, fixedSource);
        }

        // UPA0021 test case 1
        [Fact]
        public Task MagnitudeAgainstLiteral_CodeFix_SquaresBothSides()
        {
            return VerifyFixAsync(@"
using UnityEngine;

class C : MonoBehaviour
{
    Vector3 v;

    void Check()
    {
        if ({|UPA0021:v.magnitude < 5f|})
        {
        }
    }
}", @"
using UnityEngine;

class C : MonoBehaviour
{
    Vector3 v;

    void Check()
    {
        if (v.sqrMagnitude < 25f)
        {
        }
    }
}");
        }

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
        public Task MagnitudeAgainstNegativeLiteral_Triggers_WithoutFix()
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

        [Fact]
        public Task DistanceAgainstLiteral_CodeFix_RewritesToSqrMagnitudeOfDifference()
        {
            return VerifyFixAsync(@"
using UnityEngine;

class C : MonoBehaviour
{
    Vector3 a;
    Vector3 b;

    void Check()
    {
        if ({|UPA0021:Vector3.Distance(a, b) <= 5f|})
        {
        }
    }
}", @"
using UnityEngine;

class C : MonoBehaviour
{
    Vector3 a;
    Vector3 b;

    void Check()
    {
        if ((a - b).sqrMagnitude <= 25f)
        {
        }
    }
}");
        }

        [Fact]
        public Task LiteralOnLeft_CodeFix_SquaresBothSides()
        {
            return VerifyFixAsync(@"
using UnityEngine;

class C : MonoBehaviour
{
    Vector2 v;

    void Check()
    {
        if ({|UPA0021:5f > v.magnitude|})
        {
        }
    }
}", @"
using UnityEngine;

class C : MonoBehaviour
{
    Vector2 v;

    void Check()
    {
        if (25f > v.sqrMagnitude)
        {
        }
    }
}");
        }
    }
}
