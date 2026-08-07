using System.Threading.Tasks;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Xunit;

namespace UnityPerformanceAnalyzers.Tests
{
    public class UPA0001ComponentLookupAnalyzerTests
    {
        private static Task VerifyAsync(string source)
        {
            var test = new CSharpAnalyzerTest<UPA0001ComponentLookupAnalyzer, DefaultVerifier>
            {
                TestCode = source,
                ReferenceAssemblies = ReferenceAssemblies.NetStandard.NetStandard20,
            };
            test.TestState.AdditionalReferences.Add(typeof(UnityEngine.MonoBehaviour).Assembly);
            return test.RunAsync();
        }

        // UPA0001 test case 1
        [Fact]
        public Task GetComponent_InUpdate_Triggers()
        {
            return VerifyAsync(@"
using UnityEngine;

class C : MonoBehaviour
{
    void Update()
    {
        var r = {|UPA0001:GetComponent<Rigidbody>()|};
    }
}");
        }

        // UPA0001 test case 2
        [Fact]
        public Task GetComponent_InAwake_DoesNotTrigger()
        {
            return VerifyAsync(@"
using UnityEngine;

class C : MonoBehaviour
{
    void Awake()
    {
        var r = GetComponent<Rigidbody>();
    }
}");
        }

        // UPA0001 test case 3
        [Fact]
        public Task TryGetComponent_InUpdate_Triggers()
        {
            return VerifyAsync(@"
using UnityEngine;

class C : MonoBehaviour
{
    void Update()
    {
        if ({|UPA0001:TryGetComponent<Rigidbody>(out var rb)|})
        {
        }
    }
}");
        }

        // UPA0001 test case 4
        [Fact]
        public Task GetComponentInChildren_ViaGameObject_InUpdate_Triggers()
        {
            return VerifyAsync(@"
using UnityEngine;

class C : MonoBehaviour
{
    Component other = null!;

    void Update()
    {
        var r = {|UPA0001:other.gameObject.GetComponentInChildren<Rigidbody>()|};
    }
}");
        }

        // UPA0001 test case 5
        [Fact]
        public Task Update_OnNonMonoBehaviour_DoesNotTrigger()
        {
            return VerifyAsync(@"
using UnityEngine;

class C
{
    Component component = null!;

    void Update()
    {
        var r = component.GetComponent<Rigidbody>();
    }
}");
        }

        // UPA0001 test case 6
        [Fact]
        public Task GetComponent_InHotPathAttributedMethod_Triggers()
        {
            return VerifyAsync(@"
using UnityEngine;

class HotPathAttribute : System.Attribute { }

class C : MonoBehaviour
{
    [HotPath]
    void Tick()
    {
        var r = {|UPA0001:GetComponent<Rigidbody>()|};
    }
}");
        }

        // UPA0001 test case 7
        [Fact]
        public Task GetComponent_InLambdaInsideUpdate_Triggers()
        {
            return VerifyAsync(@"
using System;
using UnityEngine;

class C : MonoBehaviour
{
    void Update()
    {
        Action a = () =>
        {
            var r = {|UPA0001:GetComponent<Rigidbody>()|};
        };
    }
}");
        }

        // UPA0001 test case 8 — deliberate miss: no cross-method analysis
        [Fact]
        public Task GetComponent_InHelperCalledFromUpdate_DoesNotTrigger()
        {
            return VerifyAsync(@"
using UnityEngine;

class C : MonoBehaviour
{
    void Update()
    {
        Helper();
    }

    void Helper()
    {
        var r = GetComponent<Rigidbody>();
    }
}");
        }

        // UPA0001 test case 9
        [Fact]
        public Task NonGenericGetComponent_InUpdate_Triggers()
        {
            return VerifyAsync(@"
using UnityEngine;

class C : MonoBehaviour
{
    void Update()
    {
        var r = {|UPA0001:GetComponent(typeof(Rigidbody))|};
    }
}");
        }
    }
}
