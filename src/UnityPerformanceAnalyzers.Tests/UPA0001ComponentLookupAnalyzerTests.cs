using System.Threading.Tasks;
using Xunit;

namespace UnityPerformanceAnalyzers.Tests
{
    public class UPA0001ComponentLookupAnalyzerTests
    {
        private static Task VerifyAsync(string source) =>
            RuleVerifier.VerifyAsync<UPA0001ComponentLookupAnalyzer>(source);

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
        // Invariant 5. UPA0016 had this case and UPA0001 did not: a method named GetComponent
        // on a type of the project's own is not the one this rule is about, and binding on the
        // name alone would report the code that has already avoided the cost.
        [Fact]
        public Task UserDefinedLookupMethods_InUpdate_DoNotTrigger()
        {
            return VerifyAsync(@"
using UnityEngine;

class Registry
{
    public T GetComponent<T>() => default!;

    public bool TryGetComponent<T>(out T component)
    {
        component = default!;
        return false;
    }
}

class C : MonoBehaviour
{
    Registry registry = new Registry();

    void Update()
    {
        var r = registry.GetComponent<Transform>();
        registry.TryGetComponent<Transform>(out var t);
    }
}");
        }

    }
}
