using System.Threading.Tasks;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Xunit;

namespace UnityPerformanceAnalyzers.Tests
{
    public class UPA0018AllocatingArrayApiAnalyzerTests
    {
        private static Task VerifyAsync(string source)
        {
            var test = new CSharpAnalyzerTest<UPA0018AllocatingArrayApiAnalyzer, DefaultVerifier>
            {
                TestCode = source,
                ReferenceAssemblies = ReferenceAssemblies.NetStandard.NetStandard20,
            };
            test.TestState.AdditionalReferences.Add(typeof(UnityEngine.MonoBehaviour).Assembly);
            return test.RunAsync();
        }

        // UPA0018 test case 1
        [Fact]
        public Task InputTouches_InUpdate_Triggers()
        {
            return VerifyAsync(@"
using UnityEngine;

class C : MonoBehaviour
{
    void Update()
    {
        var n = {|UPA0018:Input.touches|}.Length;
    }
}");
        }

        // UPA0018 test case 2
        [Fact]
        public Task InputTouchCount_InUpdate_DoesNotTrigger()
        {
            return VerifyAsync(@"
using UnityEngine;

class C : MonoBehaviour
{
    void Update()
    {
        var n = Input.touchCount;
    }
}");
        }

        // UPA0018 test case 3
        [Fact]
        public Task AnimatorParameters_InStart_DoesNotTrigger()
        {
            return VerifyAsync(@"
using UnityEngine;

class C : MonoBehaviour
{
    Animator animator;

    void Start()
    {
        var ps = animator.parameters;
    }
}");
        }

        // UPA0018 test case 4
        [Fact]
        public Task RendererSharedMaterials_InUpdate_Triggers()
        {
            return VerifyAsync(@"
using UnityEngine;

class C : MonoBehaviour
{
    Renderer target;

    void Update()
    {
        var mats = {|UPA0018:target.sharedMaterials|};
    }
}");
        }

        [Fact]
        public Task CameraAllCameras_InUpdate_Triggers()
        {
            return VerifyAsync(@"
using UnityEngine;

class C : MonoBehaviour
{
    void Update()
    {
        var all = {|UPA0018:Camera.allCameras|};
    }
}");
        }

        // Writing does not allocate — only reads report.
        [Fact]
        public Task SharedMaterialsAssignment_InUpdate_DoesNotTrigger()
        {
            return VerifyAsync(@"
using UnityEngine;

class C : MonoBehaviour
{
    Renderer target;
    Material[] cached;

    void Update()
    {
        target.sharedMaterials = cached;
    }
}");
        }
    }
}
