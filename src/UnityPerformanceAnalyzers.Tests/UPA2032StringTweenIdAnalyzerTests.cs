using System.Threading.Tasks;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Xunit;

namespace UnityPerformanceAnalyzers.Tests
{
    public class UPA2032StringTweenIdAnalyzerTests
    {
        private static Task VerifyAsync(string source)
        {
            var test = new CSharpAnalyzerTest<UPA2032StringTweenIdAnalyzer, DefaultVerifier>
            {
                TestCode = source,
                ReferenceAssemblies = ReferenceAssemblies.NetStandard.NetStandard20,
            };
            test.TestState.Sources.Add(DoTweenTestSources.Stubs);
            test.TestState.AdditionalReferences.Add(typeof(UnityEngine.MonoBehaviour).Assembly);
            test.TestState.AdditionalReferences.Add(
                TestMetadataReferences.EmptyAssembly(UpaProfile.DOTweenAssemblyName));
            // UPA2032 is disabled by default; enable it the same way a preset would.
            test.TestState.AnalyzerConfigFiles.Add(("/.editorconfig", @"
root = true

[*.cs]
dotnet_diagnostic.UPA2032.severity = warning
"));
            return test.RunAsync();
        }

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
