using System.Threading.Tasks;
using Xunit;

namespace UnityPerformanceAnalyzers.Tests
{
    public class UPA0003StringPropertyAccessAnalyzerTests
    {
        private static Task VerifyAsync(string source, string? optionsFile = null) =>
            RuleVerifier.VerifyAsync<UPA0003StringPropertyAccessAnalyzer>(source, new RuleHarness
            {
                OptionsFile = optionsFile,
            });

        // UPA0003 test case 1
        [Fact]
        public Task MaterialSetFloat_StringLiteral_Triggers()
        {
            return VerifyAsync(@"
using UnityEngine;

class C
{
    Material mat = null!;

    void M()
    {
        {|UPA0003:mat.SetFloat(""_Alpha"", 1f)|};
    }
}");
        }

        // UPA0003 test case 2
        [Fact]
        public Task MaterialSetFloat_CachedId_DoesNotTrigger()
        {
            return VerifyAsync(@"
using UnityEngine;

static class ShaderProperty
{
    public static readonly int Alpha = Shader.PropertyToID(""_Alpha"");
}

class C
{
    Material mat = null!;

    void M()
    {
        mat.SetFloat(ShaderProperty.Alpha, 1f);
    }
}");
        }

        // UPA0003 test case 3
        [Fact]
        public Task AnimatorPlay_StringLiteral_Triggers()
        {
            return VerifyAsync(@"
using UnityEngine;

class C
{
    Animator animator = null!;

    void M()
    {
        {|UPA0003:animator.Play(""Idle"")|};
    }
}");
        }

        // UPA0003 test case 4
        [Fact]
        public Task AnimatorPlay_Hash_DoesNotTrigger()
        {
            return VerifyAsync(@"
using UnityEngine;

class C
{
    static readonly int Hash = Animator.StringToHash(""Idle"");
    Animator animator = null!;

    void M()
    {
        animator.Play(Hash);
    }
}");
        }

        // UPA0003 test case 5
        [Fact]
        public Task ShaderSetGlobalVector_StringLiteral_Triggers()
        {
            return VerifyAsync(@"
using UnityEngine;

class C
{
    void M(Vector4 v)
    {
        {|UPA0003:Shader.SetGlobalVector(""_X"", v)|};
    }
}");
        }

        // UPA0003 test case 6
        [Fact]
        public Task MaterialSetFloat_NonConstantVariable_DoesNotTrigger()
        {
            return VerifyAsync(@"
using UnityEngine;

class C
{
    Material mat = null!;

    void M(string propName)
    {
        mat.SetFloat(propName, 1f);
    }
}");
        }

        // UPA0003 test case 7
        [Fact]
        public Task MaterialSetFloat_CompileTimeConstant_Triggers()
        {
            return VerifyAsync(@"
using UnityEngine;

class C
{
    const string P = ""_A"";
    Material mat = null!;

    void M()
    {
        {|UPA0003:mat.SetFloat(P, 1f)|};
    }
}");
        }

        // UPA0003 test case 8
        [Fact]
        public Task MaterialPropertyBlockSetColor_StringLiteral_Triggers()
        {
            return VerifyAsync(@"
using UnityEngine;

class C
{
    MaterialPropertyBlock mpb = null!;

    void M(Color c)
    {
        {|UPA0003:mpb.SetColor(""_Color"", c)|};
    }
}");
        }

        // UPA0003 test case 9
        [Fact]
        public Task ShaderPropertyToID_DoesNotTrigger()
        {
            return VerifyAsync(@"
using UnityEngine;

class C
{
    void M()
    {
        var id = Shader.PropertyToID(""_A"");
    }
}");
        }

        // UPA0003 test case 10. The call sits in an ordinary method, not Start: one-shot
        // initialisation is excluded outright now, so a Start body would satisfy this test
        // whatever the option said - passing for a reason the test is not about.
        [Fact]
        public Task HotPathOnly_CallInOrdinaryMethod_DoesNotTrigger()
        {
            return VerifyAsync(@"
using UnityEngine;

class C : MonoBehaviour
{
    Material mat = null!;

    void Apply()
    {
        mat.SetFloat(""_Alpha"", 1f);
    }
}",
                optionsFile: "upa_shader_property_hot_path_only = true");
        }

        // Companion to case 10: with the option on, hot-path calls are still reported.
        [Fact]
        public Task HotPathOnly_CallInUpdate_Triggers()
        {
            return VerifyAsync(@"
using UnityEngine;

class C : MonoBehaviour
{
    Material mat = null!;

    void Update()
    {
        {|UPA0003:mat.SetFloat(""_Alpha"", 1f)|};
    }
}",
                optionsFile: "upa_shader_property_hot_path_only = true");
        }

        // -----------------------------------------------------------------------------------
        // One-shot initialisation is not worth a diagnostic.
        //
        // The claim - the string is resolved on every call - stays true in a constructor. It
        // just buys nothing there, and a diagnostic that buys nothing is what makes a team
        // switch the rule off. Eleven of fourteen findings on real game code were this.
        // -----------------------------------------------------------------------------------

        /// <summary>Awake runs once in an object's life.</summary>
        [Fact]
        public Task InAwake_DoesNotTrigger()
        {
            return VerifyAsync(@"
using UnityEngine;

class C : MonoBehaviour
{
    public Material mat;

    void Awake()
    {
        mat.SetFloat(""_A"", 1f);
    }
}");
        }

        /// <summary>Start runs once in an object's life.</summary>
        [Fact]
        public Task InStart_DoesNotTrigger()
        {
            return VerifyAsync(@"
using UnityEngine;

class C : MonoBehaviour
{
    public Material mat;

    void Start()
    {
        mat.SetFloat(""_A"", 1f);
    }
}");
        }

        /// <summary>
        /// OnEnable was in the exclusion set in the first draft, on the grounds
        /// that it runs once per activation like Awake. Awake runs once in an object's life;
        /// OnEnable runs on every reactivation, and pooling - which UPA0031 in this same package
        /// recommends - is precisely the pattern that reactivates objects constantly.
        /// </summary>
        [Fact]
        public Task InOnEnable_Triggers()
        {
            return VerifyAsync(@"
using UnityEngine;

class C : MonoBehaviour
{
    public Material mat;

    void OnEnable()
    {
        {|UPA0003:mat.SetFloat(""_A"", 1f)|};
    }
}");
        }

        /// <summary>the corpus Materials shape: a plain class, not a MonoBehaviour.</summary>
        [Fact]
        public Task InConstructor_DoesNotTrigger()
        {
            return VerifyAsync(@"
using UnityEngine;

class Materials
{
    private Material shadow;

    private Materials()
    {
        shadow.SetFloat(""_HorizontalSkew"", -0.33f);
    }
}");
        }

        /// <summary>
        /// The call has to sit in the initialiser itself - routing it through a
        /// helper makes the containing symbol that helper, and the test stops testing anything.
        /// </summary>
        [Fact]
        public Task InFieldInitializer_DoesNotTrigger()
        {
            return VerifyAsync(@"
using UnityEngine;

class C
{
    private static readonly Vector4 Cached = Shader.GetGlobalVector(""_A"");
}");
        }

        /// <summary>A method invoked from the inspector's context menu runs when a human clicks it.</summary>
        [Fact]
        public Task InContextMenuMethod_DoesNotTrigger()
        {
            return VerifyAsync(@"
using UnityEngine;

class C : MonoBehaviour
{
    public Material mat;

    [ContextMenu(""Reset Dissolve"")]
    private void ResetDissolve()
    {
        mat.SetFloat(""_Dissolve"", 0f);
    }
}");
        }

        /// <summary>teardown is not initialisation.</summary>
        [Fact]
        public Task InOnDestroy_Triggers()
        {
            return VerifyAsync(@"
using UnityEngine;

class C : MonoBehaviour
{
    public Material mat;

    void OnDestroy()
    {
        {|UPA0003:mat.SetFloat(""_A"", 1f)|};
    }
}");
        }

        /// <summary>
        /// OnDisable is teardown, not initialisation, and runs again on every deactivation.
        /// It stays reported for the same reason OnEnable and OnDestroy do.
        /// </summary>
        [Fact]
        public Task InOnDisable_Triggers()
        {
            return VerifyAsync(@"
using UnityEngine;

class C : MonoBehaviour
{
    public Material mat;

    void OnDisable()
    {
        {|UPA0003:mat.SetFloat(""_A"", 1f)|};
    }
}");
        }

        /// <summary>
        /// One of the two shapes that forbid narrowing this rule to hot paths:
        /// a coroutine loop runs every frame and no Unity-message classifier can see it.
        /// </summary>
        [Fact]
        public Task InCoroutineLoop_Triggers()
        {
            return VerifyAsync(@"
using System.Collections;
using UnityEngine;

class C : MonoBehaviour
{
    public MaterialPropertyBlock block;

    public IEnumerator Dissolve()
    {
        float t = 0f;
        while (t < 1f)
        {
            t += 0.016f;
            {|UPA0003:block.SetFloat(""_Dissolve"", t)|};
            yield return null;
        }
    }
}");
        }

        /// <summary>
        /// the other one: a custom state machine's own update, called every
        /// frame by something that is not Unity.
        /// </summary>
        [Fact]
        public Task InCustomUpdateOverride_Triggers()
        {
            return VerifyAsync(@"
using UnityEngine;

abstract class StateAction
{
    public abstract void OnUpdate();
}

class Flash : StateAction
{
    private Material material;

    public override void OnUpdate()
    {
        {|UPA0003:material.SetColor(""_MainColor"", default(Color))|};
    }
}");
        }

        /// <summary>the control. An ordinary method is still reported.</summary>
        [Fact]
        public Task InOrdinaryMethod_Triggers()
        {
            return VerifyAsync(@"
using UnityEngine;

class C : MonoBehaviour
{
    public Material mat;

    public void Apply()
    {
        {|UPA0003:mat.SetFloat(""_A"", 1f)|};
    }
}");
        }
    }
}
