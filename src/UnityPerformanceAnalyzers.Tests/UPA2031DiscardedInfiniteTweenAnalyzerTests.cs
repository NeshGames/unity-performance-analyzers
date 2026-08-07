using System.Threading.Tasks;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Xunit;

namespace UnityPerformanceAnalyzers.Tests
{
    public class UPA2031DiscardedInfiniteTweenAnalyzerTests
    {
        private static Task VerifyAsync(string source)
        {
            var test = new CSharpAnalyzerTest<UPA2031DiscardedInfiniteTweenAnalyzer, DefaultVerifier>
            {
                TestCode = source,
                ReferenceAssemblies = ReferenceAssemblies.NetStandard.NetStandard20,
            };
            test.TestState.Sources.Add(DoTweenTestSources.Stubs);
            test.TestState.AdditionalReferences.Add(typeof(UnityEngine.MonoBehaviour).Assembly);
            test.TestState.AdditionalReferences.Add(
                TestMetadataReferences.EmptyAssembly(UpaProfile.DOTweenAssemblyName));
            // UPA2031 is disabled by default; enable it the same way a preset would.
            test.TestState.AnalyzerConfigFiles.Add(("/.editorconfig", @"
root = true

[*.cs]
dotnet_diagnostic.UPA2031.severity = warning
"));
            return test.RunAsync();
        }

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
