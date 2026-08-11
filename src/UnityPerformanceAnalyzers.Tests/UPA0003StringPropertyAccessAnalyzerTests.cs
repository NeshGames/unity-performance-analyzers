using System.Threading.Tasks;
using Xunit;

namespace UnityPerformanceAnalyzers.Tests
{
    public class UPA0003StringPropertyAccessAnalyzerTests
    {
        private static Task VerifyAsync(string source, string? extraConfig = null) =>
            RuleVerifier.VerifyAsync<UPA0003StringPropertyAccessAnalyzer>(source, new RuleHarness
            {
                EditorConfig = extraConfig,
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
                extraConfig: "upa_shader_property_hot_path_only = true");
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
                extraConfig: "upa_shader_property_hot_path_only = true");
        }

        // UPA0003 test case 11 — the option works through the options file, which is the only
        // channel Unity passes to the compiler
        [Fact]
        public Task HotPathOnly_ViaOptionsFile_DoesNotTrigger()
        {
            return RuleVerifier.VerifyAsync<UPA0003StringPropertyAccessAnalyzer>(@"
using UnityEngine;

class C : MonoBehaviour
{
    public Material mat;

    void Apply()
    {
        mat.SetFloat(""_Alpha"", 1f);
    }
}",
                new RuleHarness { OptionsFile = "upa_shader_property_hot_path_only = true" });
        }

        // UPA0003 test case 12 — an .editorconfig section applies to the files it globs. Both
        // input orders, because reading the option once for the compilation gave every file
        // whatever the first one happened to say.
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public Task PerFileSections_ApplyToTheirOwnFile_RegardlessOfOrder(bool reversed)
        {
            var strict = ("/Strict.cs", @"
using UnityEngine;

class Strict : MonoBehaviour
{
    public Material mat;

    void Apply()
    {
        {|UPA0003:mat.SetFloat(""_Alpha"", 1f)|};
    }
}");
            var relaxed = ("/Relaxed.cs", @"
using UnityEngine;

class Relaxed : MonoBehaviour
{
    public Material mat;

    void Apply()
    {
        mat.SetFloat(""_Alpha"", 1f);
    }
}");

            var harness = new RuleHarness
            {
                RawEditorConfig = @"
root = true

[*Strict.cs]
upa_shader_property_hot_path_only = false

[*Relaxed.cs]
upa_shader_property_hot_path_only = true
",
            };
            harness.NamedSources.Add(reversed ? relaxed : strict);
            harness.NamedSources.Add(reversed ? strict : relaxed);

            return RuleVerifier.VerifyAsync<UPA0003StringPropertyAccessAnalyzer>("class Empty { }", harness);
        }

        // Same text on both sides asserts the diagnostic is reported and no fix is offered.
        private static Task VerifyFixAsync(string source, string fixedSource) =>
            RuleVerifier.VerifyCodeFixAsync<
                UPA0003StringPropertyAccessAnalyzer,
                CodeFixes.UPA0003CachePropertyIdCodeFixProvider>(source, fixedSource);

        // UPA0003 code fix case F1
        [Fact]
        public Task Fix_MaterialSetFloat_CachesTheIdOnTheContainingType()
        {
            return VerifyFixAsync(
                @"
using UnityEngine;

class C
{
    Material mat = null!;

    void M()
    {
        {|UPA0003:mat.SetFloat(""_Alpha"", 1f)|};
    }
}",
                @"
using UnityEngine;

class C
{
    private static readonly int Alpha = Shader.PropertyToID(""_Alpha"");

    Material mat = null!;

    void M()
    {
        mat.SetFloat(Alpha, 1f);
    }
}");
        }

        // UPA0003 code fix case F2 — Animator hashes state names through a different function,
        // and using the shader one would compile while hashing into the wrong table.
        [Fact]
        public Task Fix_AnimatorPlay_UsesStringToHash()
        {
            return VerifyFixAsync(
                @"
using UnityEngine;

class C
{
    Animator animator = null!;

    void M()
    {
        {|UPA0003:animator.Play(""Idle"")|};
    }
}",
                @"
using UnityEngine;

class C
{
    private static readonly int Idle = Animator.StringToHash(""Idle"");

    Animator animator = null!;

    void M()
    {
        animator.Play(Idle);
    }
}");
        }

        // UPA0003 code fix case F3 — the corpus shape: one file, one name, many calls.
        [Fact]
        public Task Fix_RepeatedLiteral_ProducesOneField()
        {
            return VerifyFixAsync(
                @"
using UnityEngine;

class C
{
    Material mat = null!;

    void M()
    {
        {|UPA0003:mat.SetFloat(""_Alpha"", 1f)|};
        {|UPA0003:mat.SetFloat(""_Alpha"", 2f)|};
    }
}",
                @"
using UnityEngine;

class C
{
    private static readonly int Alpha = Shader.PropertyToID(""_Alpha"");

    Material mat = null!;

    void M()
    {
        mat.SetFloat(Alpha, 1f);
        mat.SetFloat(Alpha, 2f);
    }
}");
        }

        // UPA0003 code fix case F4 — reported, but not fixed: replacing P with a new field
        // would declare the same name twice, and that constant may be the project's own way
        // of keeping the name in one place.
        [Fact]
        public Task Fix_ConstantThatIsNotALiteral_OffersNothing()
        {
            const string Source = @"
using UnityEngine;

class C
{
    const string P = ""_A"";
    Material mat = null!;

    void M()
    {
        {|UPA0003:mat.SetFloat(P, 1f)|};
    }
}";
            return VerifyFixAsync(Source, Source);
        }

        // UPA0003 code fix case F5
        [Fact]
        public Task Fix_NameAlreadyTaken_PicksAnotherOne()
        {
            return VerifyFixAsync(
                @"
using UnityEngine;

class C
{
    private const int MainTex = 1;
    Material mat = null!;

    void M()
    {
        {|UPA0003:mat.SetFloat(""_MainTex"", 1f)|};
    }
}",
                @"
using UnityEngine;

class C
{
    private static readonly int MainTexId = Shader.PropertyToID(""_MainTex"");

    private const int MainTex = 1;
    Material mat = null!;

    void M()
    {
        mat.SetFloat(MainTexId, 1f);
    }
}");
        }

        // UPA0003 code fix case F6 — the rewrite names Shader, so the file has to import it.
        [Fact]
        public Task Fix_WithoutTheImport_AddsIt()
        {
            return VerifyFixAsync(
                @"
class C
{
    UnityEngine.Material mat = null!;

    void M()
    {
        {|UPA0003:mat.SetFloat(""_Alpha"", 1f)|};
    }
}",
                @"using UnityEngine;

class C
{
    private static readonly int Alpha = Shader.PropertyToID(""_Alpha"");

    UnityEngine.Material mat = null!;

    void M()
    {
        mat.SetFloat(Alpha, 1f);
    }
}");
        }

        // Line endings are built here rather than taken from this file's own, because that is
        // how the defect hid: the inserted field carried Environment.NewLine, so it matched
        // whenever the test file happened to be CRLF and inserted a stray CRLF into an LF file
        // otherwise. Which one this source file is saved as must not decide what is covered.
        [Theory]
        [InlineData("\n")]
        [InlineData("\r\n")]
        public Task Fix_KeepsTheLineEndingsTheFileAlreadyUses(string newLine)
        {
            var source = string.Join(newLine, new[]
            {
                "using UnityEngine;",
                "",
                "class C",
                "{",
                "    Material mat = null!;",
                "",
                "    void M()",
                "    {",
                @"        {|UPA0003:mat.SetFloat(""_Alpha"", 1f)|};",
                "    }",
                "}",
            });

            var fixedSource = string.Join(newLine, new[]
            {
                "using UnityEngine;",
                "",
                "class C",
                "{",
                @"    private static readonly int Alpha = Shader.PropertyToID(""_Alpha"");",
                "",
                "    Material mat = null!;",
                "",
                "    void M()",
                "    {",
                "        mat.SetFloat(Alpha, 1f);",
                "    }",
                "}",
            });

            return VerifyFixAsync(source, fixedSource);
        }

        // Found in Visual Studio, not here. The fix used to hand the result to the formatter,
        // whose options come from the workspace -- which in an IDE is that user's global
        // settings. A class written with four spaces got a field indented with two, because
        // that editor was configured for two. No unit test could see it: their workspace is an
        // AdhocWorkspace whose defaults happen to match the sources they are written with,
        // which is exactly why the indentation is built here instead of inherited from it.
        [Theory]
        [InlineData("  ")]
        [InlineData("    ")]
        [InlineData("\t")]
        public Task Fix_IndentsTheFieldTheWayTheFileDoes(string indent)
        {
            var source = string.Join("\n", new[]
            {
                "using UnityEngine;",
                "",
                "class C",
                "{",
                indent + "Material mat = null!;",
                "",
                indent + "void M()",
                indent + "{",
                indent + indent + "{|UPA0003:mat.SetFloat(\"_Alpha\", 1f)|};",
                indent + "}",
                "}",
            });

            var fixedSource = string.Join("\n", new[]
            {
                "using UnityEngine;",
                "",
                "class C",
                "{",
                indent + "private static readonly int Alpha = Shader.PropertyToID(\"_Alpha\");",
                "",
                indent + "Material mat = null!;",
                "",
                indent + "void M()",
                indent + "{",
                indent + indent + "mat.SetFloat(Alpha, 1f);",
                indent + "}",
                "}",
            });

            return VerifyFixAsync(source, fixedSource);
        }

        // The member the field is inserted above usually carries an attribute in Unity code,
        // and one Fix All run produced a blank line after the field in a file whose first
        // member had none and no blank line in a file whose first member did. Same run, two
        // results — so the attribute is the variable.
        [Fact]
        public Task Fix_AboveAnAttributedMember_StillLeavesABlankLine()
        {
            return VerifyFixAsync(
                @"
using UnityEngine;

class C
{
    [System.NonSerialized]
    Material mat = null!;

    void M()
    {
        {|UPA0003:mat.SetFloat(""_Alpha"", 1f)|};
    }
}",
                @"
using UnityEngine;

class C
{
    private static readonly int Alpha = Shader.PropertyToID(""_Alpha"");

    [System.NonSerialized]
    Material mat = null!;

    void M()
    {
        mat.SetFloat(Alpha, 1f);
    }
}");
        }

        // "_Color" is one of the most common shader property names, and the field name comes
        // from the literal with the underscore dropped -- so the obvious choice is `Color`,
        // which shadows UnityEngine.Color for the whole type and stops every other line using
        // Color.white from compiling. Nothing about the diagnostic hints at that; the fix has
        // to ask what the name already means where the call sits.
        [Fact]
        public Task Fix_WhenTheNameWouldShadowAType_PicksAnotherOne()
        {
            return VerifyFixAsync(
                @"
using UnityEngine;

class C
{
    MaterialPropertyBlock mpb = null!;

    void M(Color c)
    {
        {|UPA0003:mpb.SetColor(""_Color"", c)|};
    }
}",
                @"
using UnityEngine;

class C
{
    private static readonly int ColorId = Shader.PropertyToID(""_Color"");

    MaterialPropertyBlock mpb = null!;

    void M(Color c)
    {
        mpb.SetColor(ColorId, c);
    }
}");
        }

        // Two namespaces in one file, only one of which imports UnityEngine -- the other
        // reaches Material through its full name, which is why the diagnostic fires there at
        // all. Asking whether every call site already had the import answered "no, one of
        // them does", so nothing was added and the field generated in the second namespace
        // could not resolve Shader. Found by review, not by a test, because every test until
        // now had one namespace.
        [Fact]
        public Task Fix_WhenOnlyOneNamespaceHasTheImport_StillAddsIt()
        {
            return VerifyFixAsync(
                @"
namespace A
{
    using UnityEngine;

    class First
    {
        Material mat = null!;

        void M()
        {
            {|UPA0003:mat.SetFloat(""_Alpha"", 1f)|};
        }
    }
}

namespace B
{
    class Second
    {
        UnityEngine.Material mat = null!;

        void M()
        {
            {|UPA0003:mat.SetFloat(""_Beta"", 1f)|};
        }
    }
}",
                @"using UnityEngine;

namespace A
{
    using UnityEngine;

    class First
    {
        private static readonly int Alpha = Shader.PropertyToID(""_Alpha"");

        Material mat = null!;

        void M()
        {
            mat.SetFloat(Alpha, 1f);
        }
    }
}

namespace B
{
    class Second
    {
        private static readonly int Beta = Shader.PropertyToID(""_Beta"");

        UnityEngine.Material mat = null!;

        void M()
        {
            mat.SetFloat(Beta, 1f);
        }
    }
}");
        }

        // A partial type declared twice is two declarations and one type. Grouping the work by
        // declaration gave each its own field named after the same literal, and two members of
        // the same name in one type do not compile. Review found the shape; the failure is
        // worse than the "one field per type" claim it was raised against.
        [Fact]
        public Task Fix_PartialTypeDeclaredTwice_GetsOneField()
        {
            return VerifyFixAsync(
                @"
using UnityEngine;

partial class C
{
    Material mat = null!;

    void First()
    {
        {|UPA0003:mat.SetFloat(""_Alpha"", 1f)|};
    }
}

partial class C
{
    void Second()
    {
        {|UPA0003:mat.SetFloat(""_Alpha"", 2f)|};
    }
}",
                @"
using UnityEngine;

partial class C
{
    private static readonly int Alpha = Shader.PropertyToID(""_Alpha"");

    Material mat = null!;

    void First()
    {
        mat.SetFloat(Alpha, 1f);
    }
}

partial class C
{
    void Second()
    {
        mat.SetFloat(Alpha, 2f);
    }
}");
        }

        // An existing cache field is reused rather than shadowed by a near-duplicate. Without
        // this, running the fix on a second call site would leave two fields hashing the same
        // string, and neither would be obviously wrong to a reader.
        [Fact]
        public Task Fix_ExistingCacheField_IsReused()
        {
            return VerifyFixAsync(
                @"
using UnityEngine;

class C
{
    private static readonly int Alpha = Shader.PropertyToID(""_Alpha"");

    Material mat = null!;

    void M()
    {
        {|UPA0003:mat.SetFloat(""_Alpha"", 1f)|};
    }
}",
                @"
using UnityEngine;

class C
{
    private static readonly int Alpha = Shader.PropertyToID(""_Alpha"");

    Material mat = null!;

    void M()
    {
        mat.SetFloat(Alpha, 1f);
    }
}");
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

        /// <summary>
        /// The shape that keeps OnEnable out of the exclusion set, written out: an object
        /// returned to a pool by SetActive(false) runs OnEnable again every time it is handed
        /// back out, so the call is as frequent as the pool is busy.
        /// </summary>
        [Fact]
        public Task InOnEnable_OfAPooledObject_Triggers()
        {
            return VerifyAsync(@"
using UnityEngine;

class Bullet : MonoBehaviour
{
    public Material mat;

    void OnEnable()
    {
        {|UPA0003:mat.SetColor(""_Tint"", default(Color))|};
    }

    void ReturnToPool()
    {
        gameObject.SetActive(false);
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
