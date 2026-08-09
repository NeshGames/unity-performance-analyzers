using System.Threading.Tasks;
using Xunit;

namespace UnityPerformanceAnalyzers.Tests
{
    public class UPA0015CameraMainAnalyzerTests
    {
        private static Task VerifyAsync(string source) =>
            RuleVerifier.VerifyAsync<UPA0015CameraMainAnalyzer>(source);

        // UPA0015 test case 1
        [Fact]
        public Task CameraMain_InUpdate_Triggers()
        {
            return VerifyAsync(@"
using UnityEngine;

class C : MonoBehaviour
{
    void Update()
    {
        var t = {|UPA0015:Camera.main|}.transform;
    }
}");
        }

        // UPA0015 test case 2
        [Fact]
        public Task CameraMain_InAwake_DoesNotTrigger()
        {
            return VerifyAsync(@"
using UnityEngine;

class C : MonoBehaviour
{
    Camera cached;

    void Awake()
    {
        cached = Camera.main;
    }
}");
        }

        // UPA0015 test case 3
        [Fact]
        public Task CustomMainProperty_InUpdate_DoesNotTrigger()
        {
            return VerifyAsync(@"
using UnityEngine;

static class CameraRig
{
    public static Camera main => null;
}

class C : MonoBehaviour
{
    void Update()
    {
        var c = CameraRig.main;
    }
}");
        }
    }
}
