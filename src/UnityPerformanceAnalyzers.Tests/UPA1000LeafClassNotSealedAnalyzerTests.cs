using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Xunit;

namespace UnityPerformanceAnalyzers.Tests
{
    // UPA1000 is deprecated: eight repeats against a four-derived-class probe put sealed at
    // 2.70 ns against 3.00 ns unsealed, a 0.30 ns difference inside a 1.28 ns spread with the
    // ordering reversing once. The premise is not refuted, it is unresolvable by measurement,
    // and CLAUDE.md 2.2b makes measurement the threshold for shipping a performance rule. The
    // number stays registered because 2.4 does not recycle ids, so what these tests pin is
    // that it says nothing unless a project asks it to.
    public class UPA1000LeafClassNotSealedAnalyzerTests
    {
        // Enabled the way a project that still wants it would.
        private static Task VerifyAsync(string source) =>
            RuleVerifier.VerifyAsync<UPA1000LeafClassNotSealedAnalyzer>(source, new RuleHarness
            {
                EnabledRules = { "UPA1000" },
            });

        // UPA1000 test case 1 is not written here, for the reason UPA0022's is not:
        // Microsoft.CodeAnalysis.Testing force-enables every diagnostic of the analyzers a
        // test adds, so "off by default" cannot be expressed through this verifier. The
        // descriptor is what Roslyn, upa-cli and the presets actually read, and case 8 pins it.

        // UPA1000 test case 2
        [Fact]
        public Task ConcreteLeafClass_Triggers()
        {
            return VerifyAsync(@"
class {|UPA1000:C|}
{
}");
        }

        // UPA1000 test case 3
        [Fact]
        public Task SealedClass_DoesNotTrigger()
        {
            return VerifyAsync(@"
sealed class C
{
}");
        }

        // UPA1000 test case 4 — base with a derived type is not a leaf; the leaf is
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

        // UPA1000 test case 5
        [Fact]
        public Task AbstractClass_DoesNotTrigger()
        {
            return VerifyAsync(@"
abstract class C
{
}");
        }

        // UPA1000 test case 6
        [Fact]
        public Task StaticClass_DoesNotTrigger()
        {
            return VerifyAsync(@"
static class C
{
}");
        }

        // UPA1000 test case 7 — a MonoBehaviour leaf is still a leaf when asked
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

        // UPA1000 test case 8 — the descriptor is what decides this outside the verifier
        [Fact]
        public void Descriptor_IsNotEnabledByDefault()
        {
            var descriptor = new UPA1000LeafClassNotSealedAnalyzer()
                .SupportedDiagnostics
                .Single(d => d.Id == UPA1000LeafClassNotSealedAnalyzer.DiagnosticId);

            Assert.False(descriptor.IsEnabledByDefault);
        }

        // UPA1000 test case 9 — a fix was planned for v1.0 and the plan is off. Offering to
        // apply advice whose payoff cannot be measured spends the reader's attention twice.
        [Fact]
        public void NoCodeFixProviderExists()
        {
            var providers = typeof(CodeFixes.UPA0019BoxedYieldCodeFixProvider).Assembly
                .GetTypes()
                .Where(t => t.Name.Contains("UPA1000"))
                .ToArray();

            Assert.Empty(providers);
        }
    }
}
