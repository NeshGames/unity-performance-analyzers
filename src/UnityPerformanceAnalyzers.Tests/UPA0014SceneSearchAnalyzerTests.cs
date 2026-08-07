using System.Threading.Tasks;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Xunit;

namespace UnityPerformanceAnalyzers.Tests
{
    public class UPA0014SceneSearchAnalyzerTests
    {
        private static Task VerifyAsync(string source)
        {
            var test = new CSharpAnalyzerTest<UPA0014SceneSearchAnalyzer, DefaultVerifier>
            {
                TestCode = source,
                ReferenceAssemblies = ReferenceAssemblies.NetStandard.NetStandard20,
            };
            test.TestState.AdditionalReferences.Add(typeof(UnityEngine.MonoBehaviour).Assembly);
            return test.RunAsync();
        }

        // UPA0014 test case 1
        [Fact]
        public Task GameObjectFind_InUpdate_Triggers()
        {
            return VerifyAsync(@"
using UnityEngine;

class C : MonoBehaviour
{
    void Update()
    {
        var player = {|UPA0014:GameObject.Find(""Player"")|};
    }
}");
        }

        // UPA0014 test case 2
        [Fact]
        public Task FindFirstObjectByType_InUpdate_Triggers()
        {
            return VerifyAsync(@"
using UnityEngine;

class C : MonoBehaviour
{
    void Update()
    {
        var body = {|UPA0014:Object.FindFirstObjectByType<Rigidbody>()|};
    }
}");
        }

        // UPA0014 test case 3
        [Fact]
        public Task GameObjectFind_InStart_DoesNotTrigger()
        {
            return VerifyAsync(@"
using UnityEngine;

class C : MonoBehaviour
{
    void Start()
    {
        var player = GameObject.Find(""Player"");
    }
}");
        }

        // UPA0014 test case 4
        [Fact]
        public Task CustomStaticFind_InUpdate_DoesNotTrigger()
        {
            return VerifyAsync(@"
using UnityEngine;

static class Registry
{
    public static object Find(string name) => null;
}

class C : MonoBehaviour
{
    void Update()
    {
        var entry = Registry.Find(""Player"");
    }
}");
        }

        [Fact]
        public Task FindGameObjectsWithTag_InUpdate_Triggers()
        {
            return VerifyAsync(@"
using UnityEngine;

class C : MonoBehaviour
{
    void Update()
    {
        var enemies = {|UPA0014:GameObject.FindGameObjectsWithTag(""Enemy"")|};
    }
}");
        }

        [Fact]
        public Task FindObjectsByType_InUpdate_Triggers()
        {
            return VerifyAsync(@"
using UnityEngine;

class C : MonoBehaviour
{
    void Update()
    {
        var bodies = {|UPA0014:Object.FindObjectsByType<Rigidbody>(FindObjectsSortMode.None)|};
    }
}");
        }
    }
}
