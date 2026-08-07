using System.Threading.Tasks;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Xunit;

namespace UnityPerformanceAnalyzers.Tests
{
    public class UPA0024ResourcesLoadAnalyzerTests
    {
        private static Task VerifyAsync(string source)
        {
            var test = new CSharpAnalyzerTest<UPA0024ResourcesLoadAnalyzer, DefaultVerifier>
            {
                TestCode = source,
                ReferenceAssemblies = ReferenceAssemblies.NetStandard.NetStandard20,
            };
            test.TestState.AdditionalReferences.Add(typeof(UnityEngine.MonoBehaviour).Assembly);
            // UPA0024 is disabled by default; enable it the same way a preset would.
            test.TestState.AnalyzerConfigFiles.Add(("/.editorconfig", @"
root = true

[*.cs]
dotnet_diagnostic.UPA0024.severity = warning
"));
            return test.RunAsync();
        }

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
