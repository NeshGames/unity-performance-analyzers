using System.Threading.Tasks;
using Xunit;

namespace UnityPerformanceAnalyzers.Tests
{
    public class UPA0017GetComponentsArrayAnalyzerTests
    {
        private static Task VerifyAsync(string source) =>
            RuleVerifier.VerifyAsync<UPA0017GetComponentsArrayAnalyzer>(source);

        // UPA0017 test case 1
        [Fact]
        public Task ArrayOverload_InUpdate_Triggers()
        {
            return VerifyAsync(@"
using UnityEngine;

class C : MonoBehaviour
{
    void Update()
    {
        var colliders = {|UPA0017:GetComponents<Rigidbody>()|};
    }
}");
        }

        // UPA0017 test case 2
        [Fact]
        public Task ListOverload_InUpdate_DoesNotTrigger()
        {
            return VerifyAsync(@"
using System.Collections.Generic;
using UnityEngine;

class C : MonoBehaviour
{
    List<Rigidbody> cached = new List<Rigidbody>();

    void Update()
    {
        GetComponents(cached);
    }
}");
        }

        // UPA0017 test case 3
        [Fact]
        public Task ArrayOverload_InStart_DoesNotTrigger()
        {
            return VerifyAsync(@"
using UnityEngine;

class C : MonoBehaviour
{
    void Start()
    {
        var colliders = GetComponents<Rigidbody>();
    }
}");
        }

        // UPA0017 test case 4 — singular lookup is UPA0001's territory
        [Fact]
        public Task SingularGetComponent_InUpdate_DoesNotTrigger()
        {
            return VerifyAsync(@"
using UnityEngine;

class C : MonoBehaviour
{
    void Update()
    {
        var r = GetComponent<Rigidbody>();
    }
}");
        }

        [Fact]
        public Task GetComponentsInChildren_ViaGameObject_InUpdate_Triggers()
        {
            return VerifyAsync(@"
using UnityEngine;

class C : MonoBehaviour
{
    void Update()
    {
        var all = {|UPA0017:gameObject.GetComponentsInChildren<Rigidbody>()|};
    }
}");
        }
    }
}
