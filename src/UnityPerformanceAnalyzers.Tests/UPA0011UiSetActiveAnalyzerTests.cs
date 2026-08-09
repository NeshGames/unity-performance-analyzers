using System.Threading.Tasks;
using Xunit;

namespace UnityPerformanceAnalyzers.Tests
{
    public class UPA0011UiSetActiveAnalyzerTests
    {
        private static Task VerifyAsync(string source) =>
            RuleVerifier.VerifyAsync<UPA0011UiSetActiveAnalyzer>(source, new RuleHarness
            {
                EnabledRules = { "UPA0011" },
            });

        // UPA0011 test case 1
        [Fact]
        public Task UiGraphic_GameObjectSetActive_Triggers()
        {
            return VerifyAsync(@"
using UnityEngine;
using UnityEngine.UI;

class C : MonoBehaviour
{
    Image image = null!;

    void Hide()
    {
        {|UPA0011:image.gameObject.SetActive(false)|};
    }
}");
        }

        // UPA0011 test case 2 — plain GameObject receiver cannot be judged
        [Fact]
        public Task OwnGameObject_SetActive_DoesNotTrigger()
        {
            return VerifyAsync(@"
using UnityEngine;

class C : MonoBehaviour
{
    void Hide()
    {
        gameObject.SetActive(false);
    }
}");
        }

        // UPA0011 test case 3 — non-UI component receiver
        [Fact]
        public Task NonUiComponent_GameObjectSetActive_DoesNotTrigger()
        {
            return VerifyAsync(@"
using UnityEngine;

class C : MonoBehaviour
{
    Rigidbody rigidbody = null!;

    void Hide()
    {
        rigidbody.gameObject.SetActive(false);
    }
}");
        }

        // UPA0011 test case 4
        [Fact]
        public Task TmpText_GameObjectSetActive_Triggers()
        {
            return VerifyAsync(@"
using TMPro;
using UnityEngine;

class C : MonoBehaviour
{
    TextMeshProUGUI label = null!;

    void Hide()
    {
        {|UPA0011:label.gameObject.SetActive(false)|};
    }
}");
        }
    }
}
