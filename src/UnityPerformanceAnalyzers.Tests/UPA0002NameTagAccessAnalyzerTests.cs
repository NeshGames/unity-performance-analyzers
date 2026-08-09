using System.Threading.Tasks;
using Xunit;

namespace UnityPerformanceAnalyzers.Tests
{
    public class UPA0002NameTagAccessAnalyzerTests
    {
        private static Task VerifyAsync(string source) =>
            RuleVerifier.VerifyAsync<UPA0002NameTagAccessAnalyzer>(source);

        // UPA0002 test case 1
        [Fact]
        public Task NameRead_InUpdate_Triggers()
        {
            return VerifyAsync(@"
using UnityEngine;

class C : MonoBehaviour
{
    void Update()
    {
        var n = {|UPA0002:gameObject.name|};
    }
}");
        }

        // UPA0002 test case 2 — string equality is UNT0002's territory
        [Fact]
        public Task TagComparedToString_InUpdate_DoesNotTrigger()
        {
            return VerifyAsync(@"
using UnityEngine;

class C : MonoBehaviour
{
    void Update()
    {
        if (gameObject.tag == ""Enemy"")
        {
        }
    }
}");
        }

        // UPA0002 test case 3
        [Fact]
        public Task CompareTag_InUpdate_DoesNotTrigger()
        {
            return VerifyAsync(@"
using UnityEngine;

class C : MonoBehaviour
{
    void Update()
    {
        if (gameObject.CompareTag(""Enemy""))
        {
        }
    }
}");
        }

        // UPA0002 test case 4 — setter does not allocate a returned string
        [Fact]
        public Task NameAssignment_InUpdate_DoesNotTrigger()
        {
            return VerifyAsync(@"
using UnityEngine;

class C : MonoBehaviour
{
    void Update()
    {
        gameObject.name = ""x"";
    }
}");
        }

        // UPA0002 test case 5
        [Fact]
        public Task NameRead_InStart_DoesNotTrigger()
        {
            return VerifyAsync(@"
using UnityEngine;

class C : MonoBehaviour
{
    void Start()
    {
        var n = name;
    }
}");
        }

        // UPA0002 test case 6
        [Fact]
        public Task TransformNameRead_AsArgument_InUpdate_Triggers()
        {
            return VerifyAsync(@"
using UnityEngine;

class C : MonoBehaviour
{
    void Update()
    {
        Debug.Log({|UPA0002:transform.name|});
    }
}");
        }

        // UPA0002 test case 7 — read escapes the comparison expression
        [Fact]
        public Task TagReadIntoLocal_ThenCompared_InUpdate_Triggers()
        {
            return VerifyAsync(@"
using UnityEngine;

class C : MonoBehaviour
{
    void Update()
    {
        var t = {|UPA0002:tag|};
        if (t == ""A"")
        {
        }
    }
}");
        }
    }
}
