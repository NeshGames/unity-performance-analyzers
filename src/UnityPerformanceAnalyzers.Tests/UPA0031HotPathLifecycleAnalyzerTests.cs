using System.Linq;
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

        /// <summary>
        /// The message must not claim a frequency the rule cannot observe. It matches a
        /// syntactic position; saying "every frame" describes execution, which needs flow
        /// analysis this rule does not do. Measured on three real games: five findings, and
        /// every one sat behind a one-shot guard, so the old wording was wrong every time.
        /// </summary>
        [Theory]
        [InlineData("UPA0031MessageFormat")]
        [InlineData("UPA0031MessageFormatDestroy")]
        public void Message_DoesNotAssertAFrequencyTheRuleCannotObserve(string key)
        {
            var message = LocalizedText(key);

            Assert.DoesNotContain("every frame", message, System.StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("每幀", LocalizedText(key, "zh-Hant"));
        }

        /// <summary>messageFormat is two sentences - the problem, then what to do.</summary>
        [Theory]
        [InlineData("UPA0031MessageFormat")]
        [InlineData("UPA0031MessageFormatDestroy")]
        public void Message_IsTwoSentences(string key)
        {
            var sentences = LocalizedText(key)
                .Split('.', System.StringSplitOptions.RemoveEmptyEntries)
                .Where(part => !string.IsNullOrWhiteSpace(part))
                .Count();

            Assert.Equal(2, sentences);
        }

        /// <summary>
        /// Reads the shipped .resx directly rather than through the generated accessor, so the
        /// assertion is about what users receive and not about a constant that happens to agree.
        /// </summary>
        private static string LocalizedText(string key, string culture = "")
        {
            var suffix = culture.Length == 0 ? string.Empty : "." + culture;
            // Anchored on the assembly location: other tests here change the working directory
            // and xUnit runs collections in parallel.
            var dir = new System.IO.DirectoryInfo(System.AppContext.BaseDirectory);
            while (dir is not null && !System.IO.Directory.Exists(System.IO.Path.Combine(dir.FullName, "package")))
            {
                dir = dir.Parent;
            }

            Assert.NotNull(dir);
            var path = System.IO.Path.Combine(
                dir!.FullName,
                "src", "UnityPerformanceAnalyzers", "Resources", "Strings" + suffix + ".resx");
            var doc = System.Xml.Linq.XDocument.Load(path);
            var entry = doc.Root!
                .Elements("data")
                .Single(element => (string?)element.Attribute("name") == key);
            return entry.Element("value")!.Value;
        }
    }
}
