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

        // Same text on both sides asserts the diagnostic is reported and no fix is offered.
        private static Task VerifyFixAsync(string source, string fixedSource) =>
            RuleVerifier.VerifyCodeFixAsync<
                UPA2031DiscardedInfiniteTweenAnalyzer,
                CodeFixes.UPA2031SetLinkCodeFixProvider>(source, fixedSource, new RuleHarness
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

        // UPA2031 test case 5 - pure addition: the chain's value was already unused
        [Fact]
        public Task DiscardedInfiniteLoop_CodeFix_AppendsSetLink()
        {
            return VerifyFixAsync(@"
using DG.Tweening;
using UnityEngine;

class C : MonoBehaviour
{
    Vector3 spin;

    void Start()
    {
        {|UPA2031:transform.DORotate(spin, 1f).SetLoops(-1)|};
    }
}", @"
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

        // UPA2031 test case 6 - no gameObject outside a Component, so no fix to offer
        [Fact]
        public Task OutsideComponent_Triggers_WithoutFix()
        {
            const string Source = @"
using DG.Tweening;
using UnityEngine;

class Spinner
{
    Transform target;
    Vector3 spin;

    public void Begin()
    {
        {|UPA2031:target.DORotate(spin, 1f).SetLoops(-1)|};
    }
}";
            return VerifyFixAsync(Source, Source);
        }

        // UPA2031 test case 7 - gameObject is an instance member, so a static method cannot
        // reach it. The rewrite would not compile, so it is not offered.
        [Fact]
        public Task StaticMethod_Triggers_WithoutFix()
        {
            const string Source = @"
using DG.Tweening;
using UnityEngine;

class C : MonoBehaviour
{
    static Transform target;
    static Vector3 spin;

    static void Begin()
    {
        {|UPA2031:target.DORotate(spin, 1f).SetLoops(-1)|};
    }
}";
            return VerifyFixAsync(Source, Source);
        }

        // UPA2031 test case 8 - a local named gameObject shadows the property, so the emitted
        // identifier would bind to something else or not compile
        [Fact]
        public Task ShadowedGameObject_Triggers_WithoutFix()
        {
            const string Source = @"
using DG.Tweening;
using UnityEngine;

class C : MonoBehaviour
{
    Vector3 spin;

    void Start()
    {
        object gameObject = new object();
        {|UPA2031:transform.DORotate(spin, 1f).SetLoops(-1)|};
        _ = gameObject;
    }
}";
            return VerifyFixAsync(Source, Source);
        }

        // UPA2031 test case 9 - a SetLink inside a callback binds a different tween, and the
        // discarded infinite one is still unlinked
        [Fact]
        public Task SetLinkInsideCallback_StillTriggers()
        {
            return VerifyAsync(@"
using DG.Tweening;
using UnityEngine;

class C : MonoBehaviour
{
    Tween other;

    void Start()
    {
        {|UPA2031:DOTween.Sequence().AppendCallback(() => other.SetLink(gameObject)).SetLoops(-1)|};
    }
}");
        }

        // UPA2031 test case 10 - parentheses are how the chain is written, not where it ends
        [Fact]
        public Task ParenthesizedChainWithSetLink_DoesNotTrigger()
        {
            return VerifyAsync(@"
using DG.Tweening;
using UnityEngine;

class C : MonoBehaviour
{
    Vector3 spin;

    void Start()
    {
        (transform.DORotate(spin, 1f).SetLink(gameObject)).SetLoops(-1);
    }
}");
        }
    }
}
