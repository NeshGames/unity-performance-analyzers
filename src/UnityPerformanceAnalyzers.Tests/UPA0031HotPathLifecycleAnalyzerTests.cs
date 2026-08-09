using System.Threading.Tasks;
using Xunit;

namespace UnityPerformanceAnalyzers.Tests
{
    public class UPA0031HotPathLifecycleAnalyzerTests
    {
        private static Task VerifyAsync(string source) =>
            RuleVerifier.VerifyAsync<UPA0031HotPathLifecycleAnalyzer>(source);

        // UPA0031 test case 1 - the form that matters. Inherited from UnityEngine.Object, so
        // there is no receiver to match on at all.
        [Fact]
        public Task Instantiate_NoReceiver_InUpdate_Triggers()
        {
            return VerifyAsync(@"
using UnityEngine;

class C : MonoBehaviour
{
    public GameObject prefab;

    void Update()
    {
        {|UPA0031:Instantiate(prefab)|};
    }
}");
        }

        // UPA0031 test case 2 - the generic overload resolves to the same declaring type only
        // through OriginalDefinition.
        [Fact]
        public Task Instantiate_Generic_InUpdate_Triggers()
        {
            return VerifyAsync(@"
using UnityEngine;

class C : MonoBehaviour
{
    public GameObject prefab;

    void Update()
    {
        var copy = {|UPA0031:Instantiate<GameObject>(prefab)|};
    }
}");
        }

        // UPA0031 test case 3 - fully qualified, multi-argument overload.
        [Fact]
        public Task Instantiate_QualifiedMultiArgument_InUpdate_Triggers()
        {
            return VerifyAsync(@"
using UnityEngine;

class C : MonoBehaviour
{
    public GameObject prefab;

    void Update()
    {
        {|UPA0031:Object.Instantiate(prefab, transform)|};
    }
}");
        }

        // UPA0031 test case 4 - the destroy half.
        [Fact]
        public Task Destroy_InUpdate_Triggers()
        {
            return VerifyAsync(@"
using UnityEngine;

class C : MonoBehaviour
{
    public GameObject spawned;

    void Update()
    {
        {|UPA0031:Destroy(spawned)|};
    }
}");
        }

        // UPA0031 test case 5 - invariant 3. Building objects while a scene loads is normal.
        [Fact]
        public Task Instantiate_InStart_DoesNotTrigger()
        {
            return VerifyAsync(@"
using UnityEngine;

class C : MonoBehaviour
{
    public GameObject prefab;

    void Start()
    {
        Instantiate(prefab);
    }
}");
        }

        // UPA0031 test case 6 - invariant 5. Same name, different declaring type.
        [Fact]
        public Task UserDefinedInstantiate_DoesNotTrigger()
        {
            return VerifyAsync(@"
using UnityEngine;

class Spawner
{
    public void Instantiate(GameObject g) { }

    public void Destroy(GameObject g) { }
}

class C : MonoBehaviour
{
    public GameObject prefab;
    Spawner spawner = new Spawner();

    void Update()
    {
        spawner.Instantiate(prefab);
        spawner.Destroy(prefab);
    }
}");
        }

        // UPA0031 test case 7 - invariant 4, recorded as a decision rather than a fact.
        // Destroy(null) is a legal no-op at runtime and cannot be seen statically; narrowing
        // for it would drop every report whose argument merely might be null.
        [Fact]
        public Task Destroy_PossiblyNullArgument_StillTriggers()
        {
            return VerifyAsync(@"
using UnityEngine;

class C : MonoBehaviour
{
    public GameObject maybeNull;

    void Update()
    {
        {|UPA0031:Destroy(maybeNull, 2f)|};
    }
}");
        }

        // UPA0031 test case 8 - teardown is not a per-frame path.
        [Fact]
        public Task Destroy_InOnDestroy_DoesNotTrigger()
        {
            return VerifyAsync(@"
using UnityEngine;

class C : MonoBehaviour
{
    public GameObject spawned;

    void OnDestroy()
    {
        Destroy(spawned);
    }
}");
        }
    }
}
