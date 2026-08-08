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

        // UPA0018 test case 5 (v0.6 deny-list extension)
        [Fact]
        public Task GetCurrentAnimatorClipInfoArrayOverload_InUpdate_Triggers()
        {
            return VerifyAsync(@"
using UnityEngine;

class C : MonoBehaviour
{
    Animator animator;

    void Update()
    {
        var clips = {|UPA0018:animator.GetCurrentAnimatorClipInfo(0)|};
    }
}");
        }

        // UPA0018 test case 6 — the replacement must not report itself.
        [Fact]
        public Task GetCurrentAnimatorClipInfoListOverload_InUpdate_DoesNotTrigger()
        {
            return VerifyAsync(@"
using System.Collections.Generic;
using UnityEngine;

class C : MonoBehaviour
{
    Animator animator;
    List<AnimatorClipInfo> cached = new List<AnimatorClipInfo>();

    void Update()
    {
        animator.GetCurrentAnimatorClipInfo(0, cached);
    }
}");
        }

        // UPA0018 test case 7
        [Fact]
        public Task TextureGetPixels_InUpdate_Triggers()
        {
            return VerifyAsync(@"
using UnityEngine;

class C : MonoBehaviour
{
    Texture2D texture;

    void Update()
    {
        var pixels = {|UPA0018:texture.GetPixels()|};
    }
}");
        }

        // UPA0018 test case 8 — the generic overload is the advice, not a violation.
        [Fact]
        public Task TextureGetRawTextureDataGeneric_InUpdate_DoesNotTrigger()
        {
            return VerifyAsync(@"
using UnityEngine;

class C : MonoBehaviour
{
    Texture2D texture;

    void Update()
    {
        var data = texture.GetRawTextureData<byte>();
    }
}");
        }

        // UPA0018 test case 9
        [Fact]
        public Task TextureGetRawTextureDataNonGeneric_InUpdate_Triggers()
        {
            return VerifyAsync(@"
using UnityEngine;

class C : MonoBehaviour
{
    Texture2D texture;

    void Update()
    {
        var data = {|UPA0018:texture.GetRawTextureData()|};
    }
}");
        }

        // UPA0018 test case 10
        [Fact]
        public Task TextureGetPixels_InStart_DoesNotTrigger()
        {
            return VerifyAsync(@"
using UnityEngine;

class C : MonoBehaviour
{
    Texture2D texture;

    void Start()
    {
        var pixels = texture.GetPixels();
    }
}");
        }

        // Also covers UPA0030's boundary: a BCL allocator on the same hot path is that
        // rule's business, and must not surface here.
        [Fact]
        public Task StringSplit_InUpdate_DoesNotTrigger()
        {
            return VerifyAsync(@"
using UnityEngine;

class C : MonoBehaviour
{
    string source;

    void Update()
    {
        var parts = source.Split(',');
    }
}");
        }
    }
}
