using System.Threading.Tasks;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Xunit;

namespace UnityPerformanceAnalyzers.Tests
{
    public class UPA0004InstantiatingAccessorAnalyzerTests
    {
        private static Task VerifyAsync(string source)
        {
            var test = new CSharpAnalyzerTest<UPA0004InstantiatingAccessorAnalyzer, DefaultVerifier>
            {
                TestCode = source,
                ReferenceAssemblies = ReferenceAssemblies.NetStandard.NetStandard20,
            };
            test.TestState.AdditionalReferences.Add(typeof(UnityEngine.MonoBehaviour).Assembly);
            return test.RunAsync();
        }

        // UPA0004 test case 1
        [Fact]
        public Task Material_InUpdate_Triggers()
        {
            return VerifyAsync(@"
using UnityEngine;

class C : MonoBehaviour
{
    Renderer _renderer = null!;

    void Update()
    {
        {|UPA0004:_renderer.material|}.color = default;
    }
}");
        }

        // UPA0004 test case 2
        [Fact]
        public Task SharedMaterial_InUpdate_DoesNotTrigger()
        {
            return VerifyAsync(@"
using UnityEngine;

class C : MonoBehaviour
{
    Renderer _renderer = null!;

    void Update()
    {
        _renderer.sharedMaterial.color = default;
    }
}");
        }

        // UPA0004 test case 3
        [Fact]
        public Task Material_InStart_DoesNotTrigger()
        {
            return VerifyAsync(@"
using UnityEngine;

class C : MonoBehaviour
{
    Renderer _renderer = null!;
    Material _mat = null!;

    void Start()
    {
        _mat = _renderer.material;
    }
}");
        }

        // UPA0004 test case 4
        [Fact]
        public Task Mesh_InUpdate_Triggers()
        {
            return VerifyAsync(@"
using UnityEngine;

class C : MonoBehaviour
{
    MeshFilter meshFilter = null!;

    void Update()
    {
        {|UPA0004:meshFilter.mesh|}.RecalculateBounds();
    }
}");
        }

        // UPA0004 test case 5 — inherited accessor resolves to Renderer
        [Fact]
        public Task Material_OnSkinnedMeshRenderer_InUpdate_Triggers()
        {
            return VerifyAsync(@"
using UnityEngine;

class C : MonoBehaviour
{
    SkinnedMeshRenderer skinnedRenderer = null!;

    void Update()
    {
        var m = {|UPA0004:skinnedRenderer.material|};
    }
}");
        }

        // UPA0004 test case 6 — materials allocates a fresh array per access
        [Fact]
        public Task MaterialsIndexer_InUpdate_Triggers()
        {
            return VerifyAsync(@"
using UnityEngine;

class C : MonoBehaviour
{
    Renderer _renderer = null!;

    void Update()
    {
        var m = {|UPA0004:_renderer.materials|}[0];
    }
}");
        }
    }
}
