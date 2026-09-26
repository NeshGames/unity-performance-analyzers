using System.Threading.Tasks;
using Xunit;

namespace UnityPerformanceAnalyzers.Tests
{
    /// <summary>
    /// Guards the boundary between UPA0001 and UPA0017. Both are hot-path rules about the
    /// <c>GetComponent</c> family, and both used to claim the array-returning
    /// <c>GetComponents*</c> overloads, so one call in <c>Update</c> carried two warnings with
    /// two different fixes. The array is the cost worth naming there, which makes those calls
    /// UPA0017's; the <c>List&lt;T&gt;</c> overloads allocate nothing and stay UPA0001's.
    /// Each rule's own tests run it alone and cannot see the overlap, so these run both.
    /// </summary>
    public class ComponentLookupOverlapTests
    {
        private static Task VerifyBothAsync(string source) =>
            RuleVerifier.VerifyTogetherAsync<UPA0001ComponentLookupAnalyzer, UPA0017GetComponentsArrayAnalyzer>(source);

        [Fact]
        public Task ArrayReturningOverloads_ReportOnlyUpa0017()
        {
            return VerifyBothAsync(@"
using UnityEngine;

class C : MonoBehaviour
{
    void Update()
    {
        var a = {|UPA0017:GetComponents<Rigidbody>()|};
        var b = {|UPA0017:GetComponentsInChildren<Rigidbody>()|};
        var c = {|UPA0017:gameObject.GetComponentsInParent<Rigidbody>()|};
    }
}");
        }

        // The List<T> overload is what UPA0017 tells people to switch to, and it still pays
        // for the native lookup every frame. Dropping it from UPA0001 as well would have
        // turned following one rule's advice into silence from both.
        [Fact]
        public Task ListOverloads_ReportOnlyUpa0001()
        {
            return VerifyBothAsync(@"
using System.Collections.Generic;
using UnityEngine;

class C : MonoBehaviour
{
    readonly List<Rigidbody> _bodies = new List<Rigidbody>();

    void Update()
    {
        {|UPA0001:GetComponents(_bodies)|};
        {|UPA0001:GetComponentsInChildren(_bodies)|};
        {|UPA0001:GetComponentsInParent(false, _bodies)|};
    }
}");
        }

        [Fact]
        public Task SingularLookup_ReportsOnlyUpa0001()
        {
            return VerifyBothAsync(@"
using UnityEngine;

class C : MonoBehaviour
{
    void Update()
    {
        var r = {|UPA0001:GetComponent<Rigidbody>()|};
    }
}");
        }
    }
}
