using System.Threading.Tasks;
using Xunit;

namespace UnityPerformanceAnalyzers.Tests
{
    public class UPA2032StringTweenIdAnalyzerTests
    {
        private static Task VerifyAsync(string source) =>
            RuleVerifier.VerifyAsync<UPA2032StringTweenIdAnalyzer>(source, new RuleHarness
            {
                Sources = { DoTweenTestSources.Stubs },
                PackageAssemblies = { UpaProfile.DOTweenAssemblyName },
                EnabledRules = { "UPA2032" },
            });

        // UPA2032 test case 1
        [Fact]
        public Task SetIdWithString_Triggers()
        {
            return VerifyAsync(@"
using DG.Tweening;
using UnityEngine;

class C : MonoBehaviour
{
    Vector3 target;

    void Start()
    {
        {|UPA2032:transform.DOMove(target, 1f).SetId(""walk"")|};
    }
}");
        }

        // UPA2032 test case 2
        [Fact]
        public Task SetIdWithInt_DoesNotTrigger()
        {
            return VerifyAsync(@"
using DG.Tweening;
using UnityEngine;

class C : MonoBehaviour
{
    Vector3 target;

    void Start()
    {
        transform.DOMove(target, 1f).SetId(42);
    }
}");
        }

        // UPA2032 test case 3
        [Fact]
        public Task DOTweenKillWithString_Triggers()
        {
            return VerifyAsync(@"
using UnityEngine;

class C : MonoBehaviour
{
    void OnDisable()
    {
        {|UPA2032:DG.Tweening.DOTween.Kill(""walk"")|};
    }
}");
        }

        // UPA2032 test case 4
        [Fact]
        public Task DOTweenKillWithObjectTarget_DoesNotTrigger()
        {
            return VerifyAsync(@"
using UnityEngine;

class C : MonoBehaviour
{
    void OnDisable()
    {
        DG.Tweening.DOTween.Kill(this);
    }
}");
        }
    }
}
