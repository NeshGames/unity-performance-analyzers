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

        // UPA0003 test case 10
        [Fact]
        public Task HotPathOnly_CallInStart_DoesNotTrigger()
        {
            return VerifyAsync(@"
using UnityEngine;

class C : MonoBehaviour
{
    Material mat = null!;

    void Start()
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

    void Start()
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

    void Start()
    {
        {|UPA0003:mat.SetFloat(""_Alpha"", 1f)|};
    }
}");
            var relaxed = ("/Relaxed.cs", @"
using UnityEngine;

class Relaxed : MonoBehaviour
{
    public Material mat;

    void Start()
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

    }
}
