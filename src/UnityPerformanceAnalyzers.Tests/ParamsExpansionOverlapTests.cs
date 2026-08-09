using System.Threading.Tasks;
using Xunit;

namespace UnityPerformanceAnalyzers.Tests
{
    /// <summary>
    /// Guards the boundary between UPA0006 and UPA0027. A params expansion carrying value-type
    /// arguments is one cost — an array plus the boxing of what goes into it — and UPA0027
    /// reports it as one diagnostic that names the call. UPA0006 has to stay silent on both
    /// halves, or a single line grows two warnings that say the same thing twice.
    /// </summary>
    public class ParamsExpansionOverlapTests
    {
        private static Task VerifyAllocationRuleAsync(string source) =>
            RuleVerifier.VerifyAsync<UPA0006HotPathAllocationAnalyzer>(source);

        [Fact]
        public Task DebugLogFormatWithValueTypeArgument_DoesNotReportAllocationRule()
        {
            return VerifyAllocationRuleAsync(@"
using UnityEngine;

class C : MonoBehaviour
{
    int x;

    void Update()
    {
        Debug.LogFormat(""{0}"", x);
    }
}");
        }

        [Fact]
        public Task StringFormatWithSeveralValueTypeArguments_DoesNotReportAllocationRule()
        {
            return VerifyAllocationRuleAsync(@"
using UnityEngine;

class C : MonoBehaviour
{
    int i, j, k, l;

    void Update()
    {
        var s = string.Format(""{0}{1}{2}{3}"", i, j, k, l);
    }
}");
        }

        // Boxing outside a params expansion is still UPA0006's: the exclusion is scoped to the
        // synthesized array, not to boxing in general.
        [Fact]
        public Task ExplicitBoxingConversion_StillReportsAllocationRule()
        {
            return VerifyAllocationRuleAsync(@"
using UnityEngine;

class C : MonoBehaviour
{
    int x;

    void Update()
    {
        object boxed = {|UPA0006:x|};
    }
}");
        }
    }
}
