using System.Threading.Tasks;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Xunit;

namespace UnityPerformanceAnalyzers.Tests
{
    public class UPA0016SendMessageAnalyzerTests
    {
        private static Task VerifyAsync(string source)
        {
            var test = new CSharpAnalyzerTest<UPA0016SendMessageAnalyzer, DefaultVerifier>
            {
                TestCode = source,
                ReferenceAssemblies = ReferenceAssemblies.NetStandard.NetStandard20,
            };
            test.TestState.AdditionalReferences.Add(typeof(UnityEngine.MonoBehaviour).Assembly);
            return test.RunAsync();
        }

        // UPA0016 test case 1 — reported outside hot paths too (global rule)
        [Fact]
        public Task SendMessage_InOrdinaryMethod_Triggers()
        {
            return VerifyAsync(@"
using UnityEngine;

class C : MonoBehaviour
{
    void OnHit()
    {
        {|UPA0016:gameObject.SendMessage(""Hit"")|};
    }
}");
        }

        // UPA0016 test case 2
        [Fact]
        public Task BroadcastMessage_AndSendMessageUpwards_Trigger()
        {
            return VerifyAsync(@"
using UnityEngine;

class C : MonoBehaviour
{
    void OnHit()
    {
        {|UPA0016:BroadcastMessage(""Hit"")|};
        {|UPA0016:SendMessageUpwards(""Hit"", 1)|};
    }
}");
        }

        // UPA0016 test case 3
        [Fact]
        public Task CustomSendMessage_DoesNotTrigger()
        {
            return VerifyAsync(@"
class Messenger
{
    public void SendMessage(string name) { }
}

class C
{
    void OnHit()
    {
        new Messenger().SendMessage(""Hit"");
    }
}");
        }
    }
}
