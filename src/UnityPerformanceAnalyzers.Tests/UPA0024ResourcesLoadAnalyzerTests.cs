using System.Threading.Tasks;
using Xunit;

namespace UnityPerformanceAnalyzers.Tests
{
    public class UPA0024ResourcesLoadAnalyzerTests
    {
        private static Task VerifyAsync(string source) =>
            RuleVerifier.VerifyAsync<UPA0024ResourcesLoadAnalyzer>(source, new RuleHarness
            {
                EnabledRules = { "UPA0024" },
            });

        // UPA0024 test case 1
        [Fact]
        public Task ResourcesLoad_InUpdate_Triggers()
        {
            return VerifyAsync(@"
using UnityEngine;

class Sprite : Object { }

class C : MonoBehaviour
{
    void Update()
    {
        var icon = {|UPA0024:Resources.Load<Sprite>(""icon"")|};
    }
}");
        }

        // UPA0024 test case 2
        [Fact]
        public Task ResourcesLoad_InStart_DoesNotTrigger()
        {
            return VerifyAsync(@"
using UnityEngine;

class C : MonoBehaviour
{
    void Start()
    {
        var icon = Resources.Load(""icon"");
    }
}");
        }

        // UPA0024 test case 3
        [Fact]
        public Task UnloadUnusedAssets_InUpdate_DoesNotTrigger()
        {
            return VerifyAsync(@"
using UnityEngine;

class C : MonoBehaviour
{
    void Update()
    {
        Resources.UnloadUnusedAssets();
    }
}");
        }

        [Fact]
        public Task LoadAllAndLoadAsync_InUpdate_Trigger()
        {
            return VerifyAsync(@"
using UnityEngine;

class C : MonoBehaviour
{
    void Update()
    {
        var all = {|UPA0024:Resources.LoadAll(""icons"")|};
        var req = {|UPA0024:Resources.LoadAsync(""icon"")|};
    }
}");
        }
    }
}
