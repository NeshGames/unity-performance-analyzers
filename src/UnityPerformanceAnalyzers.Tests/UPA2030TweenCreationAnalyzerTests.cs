using System.Threading.Tasks;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Xunit;

namespace UnityPerformanceAnalyzers.Tests
{
    public class UPA2030TweenCreationAnalyzerTests
    {
        private static Task VerifyAsync(string source, bool referenceDOTween = true)
        {
            var test = new CSharpAnalyzerTest<UPA2030TweenCreationAnalyzer, DefaultVerifier>
            {
                TestCode = source,
                ReferenceAssemblies = ReferenceAssemblies.NetStandard.NetStandard20,
            };
            test.TestState.Sources.Add(DoTweenTestSources.Stubs);
            test.TestState.AdditionalReferences.Add(typeof(UnityEngine.MonoBehaviour).Assembly);
            if (referenceDOTween)
            {
                test.TestState.AdditionalReferences.Add(
                    TestMetadataReferences.EmptyAssembly(UpaProfile.DOTweenAssemblyName));
            }

            // UPA2030 is disabled by default; enable it the same way a preset would.
            test.TestState.AnalyzerConfigFiles.Add(("/.editorconfig", @"
root = true

[*.cs]
dotnet_diagnostic.UPA2030.severity = warning
"));
            return test.RunAsync();
        }

        // UPA2030 test case 1
        [Fact]
        public Task DOMove_InUpdate_Triggers()
        {
            return VerifyAsync(@"
using DG.Tweening;
using UnityEngine;

class C : MonoBehaviour
{
    Vector3 target;

    void Update()
    {
        {|UPA2030:transform.DOMove(target, 1f)|};
    }
}");
        }

        // UPA2030 test case 2
        [Fact]
        public Task DOTweenSequence_InUpdate_Triggers()
        {
            return VerifyAsync(@"
using UnityEngine;

class C : MonoBehaviour
{
    void Update()
    {
        var s = {|UPA2030:DG.Tweening.DOTween.Sequence()|};
    }
}");
        }

        // UPA2030 test case 3
        [Fact]
        public Task DOMove_InStart_DoesNotTrigger()
        {
            return VerifyAsync(@"
using DG.Tweening;
using UnityEngine;

class C : MonoBehaviour
{
    Vector3 target;

    void Start()
    {
        transform.DOMove(target, 1f);
    }
}");
        }

        // UPA2030 test case 4 — without the DOTween assembly the rule is not registered
        [Fact]
        public Task DOMove_WithoutDOTweenAssembly_DoesNotTrigger()
        {
            return VerifyAsync(@"
using DG.Tweening;
using UnityEngine;

class C : MonoBehaviour
{
    Vector3 target;

    void Update()
    {
        transform.DOMove(target, 1f);
    }
}", referenceDOTween: false);
        }

        // Chain configuration calls report once, at the creation root.
        [Fact]
        public Task ChainedConfiguration_InUpdate_ReportsOnlyCreationRoot()
        {
            return VerifyAsync(@"
using DG.Tweening;
using UnityEngine;

class C : MonoBehaviour
{
    Vector3 target;

    void Update()
    {
        {|UPA2030:transform.DOMove(target, 1f)|}.SetEase(1).SetAutoKill(false);
    }
}");
        }
    }
}
