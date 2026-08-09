using System.Threading.Tasks;
using Xunit;

namespace UnityPerformanceAnalyzers.Tests
{
    public class UPA2031DiscardedInfiniteTweenAnalyzerTests
    {
        private static Task VerifyAsync(string source) =>
            RuleVerifier.VerifyAsync<UPA2031DiscardedInfiniteTweenAnalyzer>(source, new RuleHarness
            {
                Sources = { DoTweenTestSources.Stubs },
                PackageAssemblies = { UpaProfile.DOTweenAssemblyName },
                EnabledRules = { "UPA2031" },
            });

        // UPA2031 test case 1
        [Fact]
        public Task DiscardedInfiniteLoop_Triggers()
        {
            return VerifyAsync(@"
using DG.Tweening;
using UnityEngine;

class C : MonoBehaviour
{
    Vector3 spin;

    void Start()
    {
        {|UPA2031:transform.DORotate(spin, 1f).SetLoops(-1)|};
    }
}");
        }

        // UPA2031 test case 2
        [Fact]
        public Task InfiniteLoopWithSetLink_DoesNotTrigger()
        {
            return VerifyAsync(@"
using DG.Tweening;
using UnityEngine;

class C : MonoBehaviour
{
    Vector3 spin;

    void Start()
    {
        transform.DORotate(spin, 1f).SetLoops(-1).SetLink(gameObject);
    }
}");
        }

        // UPA2031 test case 3
        [Fact]
        public Task StoredInfiniteLoop_DoesNotTrigger()
        {
            return VerifyAsync(@"
using DG.Tweening;
using UnityEngine;

class C : MonoBehaviour
{
    Vector3 spin;
    Tween _tween;

    void Start()
    {
        _tween = transform.DORotate(spin, 1f).SetLoops(-1);
    }
}");
        }

        // UPA2031 test case 4
        [Fact]
        public Task FiniteLoop_DoesNotTrigger()
        {
            return VerifyAsync(@"
using DG.Tweening;
using UnityEngine;

class C : MonoBehaviour
{
    Vector3 spin;

    void Start()
    {
        transform.DORotate(spin, 1f).SetLoops(3);
    }
}");
        }

        // SetLink later in the chain still counts.
        [Fact]
        public Task SetLinkBeforeSetLoops_DoesNotTrigger()
        {
            return VerifyAsync(@"
using DG.Tweening;
using UnityEngine;

class C : MonoBehaviour
{
    Vector3 spin;

    void Start()
    {
        transform.DORotate(spin, 1f).SetLink(gameObject).SetLoops(-1);
    }
}");
        }
    }
}
