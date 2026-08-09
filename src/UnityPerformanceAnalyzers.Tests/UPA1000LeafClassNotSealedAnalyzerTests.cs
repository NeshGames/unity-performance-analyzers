using System.Threading.Tasks;
using Xunit;

namespace UnityPerformanceAnalyzers.Tests
{
    public class UPA1000LeafClassNotSealedAnalyzerTests
    {
        private static Task VerifyAsync(string source) =>
            RuleVerifier.VerifyAsync<UPA1000LeafClassNotSealedAnalyzer>(source, new RuleHarness
            {
                EnabledRules = { "UPA1000" },
            });

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
