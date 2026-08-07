using System.Threading.Tasks;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Xunit;

namespace UnityPerformanceAnalyzers.Tests
{
    public class UPA1000LeafClassNotSealedAnalyzerTests
    {
        private static Task VerifyAsync(string source)
        {
            var test = new CSharpAnalyzerTest<UPA1000LeafClassNotSealedAnalyzer, DefaultVerifier>
            {
                TestCode = source,
                ReferenceAssemblies = ReferenceAssemblies.NetStandard.NetStandard20,
            };
            test.TestState.AdditionalReferences.Add(typeof(UnityEngine.MonoBehaviour).Assembly);
            // UPA1000 is disabled by default; enable it the same way a preset would.
            test.TestState.AnalyzerConfigFiles.Add(("/.editorconfig", @"
root = true

[*.cs]
dotnet_diagnostic.UPA1000.severity = warning
"));
            return test.RunAsync();
        }

        // UPA1000 test case 1
        [Fact]
        public Task ConcreteLeafClass_Triggers()
        {
            return VerifyAsync(@"
class {|UPA1000:C|}
{
}");
        }

        // UPA1000 test case 2
        [Fact]
        public Task SealedClass_DoesNotTrigger()
        {
            return VerifyAsync(@"
sealed class C
{
}");
        }

        // UPA1000 test case 3 — base with a derived type is not a leaf; the leaf is
        [Fact]
        public Task BaseWithDerived_OnlyLeafTriggers()
        {
            return VerifyAsync(@"
class Base
{
}

class {|UPA1000:A|} : Base
{
}");
        }

        // UPA1000 test case 4
        [Fact]
        public Task AbstractClass_DoesNotTrigger()
        {
            return VerifyAsync(@"
abstract class C
{
}");
        }

        // UPA1000 test case 5
        [Fact]
        public Task StaticClass_DoesNotTrigger()
        {
            return VerifyAsync(@"
static class C
{
}");
        }

        // UPA1000 test case 6 — MonoBehaviour leaves can and should be sealed
        [Fact]
        public Task MonoBehaviourLeaf_Triggers()
        {
            return VerifyAsync(@"
using UnityEngine;

class {|UPA1000:Mover|} : MonoBehaviour
{
}");
        }

        // Sealing a class with its own virtual members is CS0549
        [Fact]
        public Task ClassDeclaringVirtualMember_DoesNotTrigger()
        {
            return VerifyAsync(@"
class C
{
    public virtual void M()
    {
    }
}");
        }
    }
}
