using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using UnityEngine;

/// <summary>
/// Measurement harness shared by A1 (does the default comparer for enum keys box?) and
/// A3 (does each candidate API on the known-allocating list actually allocate?).
///
/// Runtime-only on purpose: the same code has to run inside the editor via batchmode and
/// inside a player build, because the whole question is whether the class library differs
/// between them.
///
/// Two independent signals, because neither is sufficient alone:
///   1. Comparer identity — EqualityComparer&lt;T&gt;.Default resolves to a concrete type
///      whose name says what it does (ObjectEqualityComparer boxes, EnumEqualityComparer
///      and GenericEqualityComparer do not). Deterministic, immune to GC noise, and it
///      answers A1 outright.
///   2. Allocation delta — bytes retained across N iterations. Corroborates signal 1 and
///      is the only available signal for A3. Reports the gen-0 collection count alongside,
///      because a collection mid-run makes the delta a lower bound rather than a total.
/// </summary>
public static class AllocationMeasurement
{
    private const int WarmupIterations = 1000;

    private static RuntimeAnimatorController s_animatorController;

    /// <summary>Underlying type is part of the question: comparer selection may differ by size.</summary>
    public enum IntEnum { A, B, C, D }

    /// <summary>Byte-backed variant, to catch a comparer chosen per underlying type.</summary>
    public enum ByteEnum : byte { A, B, C, D }

    /// <summary>Long-backed variant, same reason.</summary>
    public enum LongEnum : long { A, B, C, D }

    /// <summary>The shape HasFlag is actually written against.</summary>
    [Flags]
    public enum Rights { None = 0, Read = 1, Write = 2, Execute = 4 }

    /// <summary>Byte-backed flags, in case the optimization is chosen per underlying type.</summary>
    [Flags]
    public enum ByteRights : byte { None = 0, Read = 1, Write = 2, Execute = 4 }

    /// <summary>
    /// Consumed result of every HasFlag measurement. Without a sink the call has no effect
    /// and an optimizing backend may remove it outright, which would report "no measurable
    /// allocation" for a call that never ran — the answer we are looking for, arrived at
    /// the one way that would mean nothing.
    /// </summary>
    private static int s_sink;

    private struct PlainKey
    {
        public int X;
        public int Y;
    }

    private struct EquatableKey : IEquatable<EquatableKey>
    {
        public int X;
        public int Y;

        public bool Equals(EquatableKey other) => X == other.X && Y == other.Y;

        public override bool Equals(object obj) => obj is EquatableKey other && Equals(other);

        public override int GetHashCode() => unchecked((X * 397) ^ Y);
    }

    private sealed class IntEnumComparer : IEqualityComparer<IntEnum>
    {
        public bool Equals(IntEnum x, IntEnum y) => x == y;

        public int GetHashCode(IntEnum obj) => (int)obj;
    }

    /// <summary>
    /// Runs both measurement groups and returns the report as text. The caller supplies an
    /// animator controller because building one needs the editor API, and measuring the
    /// clip-info calls without a controller assigned only measures the empty-result path.
    /// </summary>
    public static string RunAll(RuntimeAnimatorController animatorController = null)
    {
        s_animatorController = animatorController;
        var report = new StringBuilder();
        report.AppendLine("=== environment ===");
        report.AppendLine(Line("unity", Application.unityVersion));
        report.AppendLine(Line("platform", Application.platform.ToString()));
        report.AppendLine(Line("runtime", GetRuntimeDescription()));
        report.AppendLine(Line("is64bit", (IntPtr.Size == 8).ToString()));

        report.AppendLine();
        report.AppendLine("=== A1 comparer identity ===");
        AppendComparerIdentities(report);

        report.AppendLine();
        report.AppendLine("=== A1 allocation deltas ===");
        AppendEnumKeyDeltas(report);

        report.AppendLine();
        report.AppendLine("=== A3 known-allocating API candidates ===");
        AppendKnownAllocatingApiDeltas(report);

        report.AppendLine();
        report.AppendLine("=== A4 Enum.HasFlag ===");
        AppendHasFlagDeltas(report);

        report.AppendLine();
        report.AppendLine("=== A5 boxing by inherited method call (UPA0026) ===");
        AppendInheritedCallDeltas(report);

        report.AppendLine();
        report.AppendLine("=== A6 advice whose payoff is time, not bytes ===");
        AppendTimeOnlyComparisons(report);

        report.AppendLine();
        report.AppendLine("=== A7 remaining allocation premises ===");
        AppendRemainingPremiseDeltas(report);

        report.AppendLine();
        report.AppendLine("=== A8 argument boxing: elided per call, or hoisted out of the loop? ===");
        AppendArgumentBoxDeltas(report);

        report.AppendLine();
        report.AppendLine("=== A9 TextMeshPro: text assignment against SetText (UPA0012) ===");
        AppendTextMeshProDeltas(report);

        report.AppendLine();
        report.AppendLine("=== A10 ZString against string concatenation (UPA2000) ===");
        report.AppendLine(Line("instrument", "iterations between gen0 collections; more iterations = less allocated per call"));
        AppendZStringDeltas(report);

        return report.ToString();
    }

    private static void AppendComparerIdentities(StringBuilder report)
    {
        report.AppendLine(Line("EqualityComparer<IntEnum>.Default", ComparerName<IntEnum>()));
        report.AppendLine(Line("EqualityComparer<ByteEnum>.Default", ComparerName<ByteEnum>()));
        report.AppendLine(Line("EqualityComparer<LongEnum>.Default", ComparerName<LongEnum>()));
        report.AppendLine(Line("EqualityComparer<int>.Default", ComparerName<int>()));
        report.AppendLine(Line("EqualityComparer<PlainKey>.Default", ComparerName<PlainKey>()));
        report.AppendLine(Line("EqualityComparer<EquatableKey>.Default", ComparerName<EquatableKey>()));
    }

    private static string ComparerName<T>() => EqualityComparer<T>.Default.GetType().ToString();

    private static void AppendEnumKeyDeltas(StringBuilder report)
    {
        var enumKeyed = new Dictionary<IntEnum, int> { { IntEnum.A, 1 } };
        var byteKeyed = new Dictionary<ByteEnum, int> { { ByteEnum.A, 1 } };
        var intKeyed = new Dictionary<int, int> { { 0, 1 } };
        var explicitComparer = new Dictionary<IntEnum, int>(new IntEnumComparer()) { { IntEnum.A, 1 } };
        var plainStructKeyed = new Dictionary<PlainKey, int> { { default, 1 } };
        var equatableStructKeyed = new Dictionary<EquatableKey, int> { { default, 1 } };

        const int iterations = 200000;
        report.AppendLine(Measure("Dictionary<IntEnum,int>.ContainsKey (no comparer)", iterations,
            _ => enumKeyed.ContainsKey(IntEnum.A)));
        report.AppendLine(Measure("Dictionary<ByteEnum,int>.ContainsKey (no comparer)", iterations,
            _ => byteKeyed.ContainsKey(ByteEnum.A)));
        report.AppendLine(Measure("Dictionary<int,int>.ContainsKey (control)", iterations,
            _ => intKeyed.ContainsKey(0)));
        report.AppendLine(Measure("Dictionary<IntEnum,int>.ContainsKey (explicit comparer)", iterations,
            _ => explicitComparer.ContainsKey(IntEnum.A)));
        report.AppendLine(Measure("Dictionary<PlainKey,int>.ContainsKey (no IEquatable)", iterations,
            _ => plainStructKeyed.ContainsKey(default)));
        report.AppendLine(Measure("Dictionary<EquatableKey,int>.ContainsKey (IEquatable)", iterations,
            _ => equatableStructKeyed.ContainsKey(default)));
    }

    private static void AppendKnownAllocatingApiDeltas(StringBuilder report)
    {
        const int iterations = 20000;
        const string sample = "alpha,beta,gamma";

        report.AppendLine(Measure("String.Split(char)", iterations, _ => sample.Split(',')));
        report.AppendLine(Measure("String.ToCharArray()", iterations, _ => sample.ToCharArray()));
        report.AppendLine(Measure("String.Substring(int,int)", iterations, _ => sample.Substring(0, 5)));
        report.AppendLine(Measure("String.ToLowerInvariant()", iterations, _ => sample.ToLowerInvariant()));
        report.AppendLine(Measure("String.ToUpperInvariant()", iterations, _ => sample.ToUpperInvariant()));
        report.AppendLine(Measure("String.Trim() (nothing to trim)", iterations, _ => sample.Trim()));
        report.AppendLine(Measure("String.Trim() (whitespace present)", iterations, _ => "  padded  ".Trim()));
        report.AppendLine(Measure("Enum.GetValues(typeof(IntEnum))", iterations, _ => Enum.GetValues(typeof(IntEnum))));
        report.AppendLine(Measure("Enum.GetNames(typeof(IntEnum))", iterations, _ => Enum.GetNames(typeof(IntEnum))));

        AppendTextureDeltas(report, iterations);
        AppendAnimatorDelta(report, iterations);
    }

    private static void AppendTextureDeltas(StringBuilder report, int iterations)
    {
        var texture = new Texture2D(16, 16, TextureFormat.RGBA32, mipChain: false);
        try
        {
            report.AppendLine(Measure("Texture2D.GetPixels()", iterations / 10, _ => texture.GetPixels()));
            report.AppendLine(Measure("Texture2D.GetPixels32()", iterations / 10, _ => texture.GetPixels32()));
            report.AppendLine(Measure("Texture2D.GetRawTextureData() (non-generic)", iterations / 10,
                _ => texture.GetRawTextureData()));
            report.AppendLine(Measure("Texture2D.GetRawTextureData<byte>() (generic)", iterations / 10,
                _ => texture.GetRawTextureData<byte>()));
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(texture);
        }
    }

    private static void AppendAnimatorDelta(StringBuilder report, int iterations)
    {
        var host = new GameObject("MeasurementAnimator");
        try
        {
            var animator = host.AddComponent<Animator>();
            var buffer = new List<AnimatorClipInfo>();

            // Without a controller the call returns an empty result, which measures the
            // degenerate path rather than the one the rule is about. The caller supplies a
            // real controller so the array overload actually has clip info to hand back.
            if (s_animatorController is object)
            {
                animator.runtimeAnimatorController = s_animatorController;
                animator.Update(0f);
            }

            report.AppendLine(Line("Animator.runtimeAnimatorController",
                animator.runtimeAnimatorController is null ? "<none>" : s_animatorController.name));
            report.AppendLine(Line("Animator clip count on layer 0",
                animator.GetCurrentAnimatorClipInfoCount(0).ToString()));
            report.AppendLine(Measure("Animator.GetCurrentAnimatorClipInfo(int) (array overload)", iterations / 10,
                _ => animator.GetCurrentAnimatorClipInfo(0)));
            report.AppendLine(Measure("Animator.GetCurrentAnimatorClipInfo(int, List<T>) (list overload)",
                iterations / 10, _ => animator.GetCurrentAnimatorClipInfo(0, buffer)));
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(host);
        }
    }

    /// <summary>
    /// Whether <c>a.HasFlag(b)</c> still boxes, which is the whole premise of advising the
    /// bitwise rewrite. Both runtimes are reported to special-case the same-type call, so
    /// the same-type rows are the ones that decide it; the Enum-typed receiver is included
    /// because a rewrite is not available there either way, and the bitwise form is the
    /// control — it must read zero, or the harness is measuring something else.
    /// </summary>
    private static void AppendHasFlagDeltas(StringBuilder report)
    {
        const int iterations = 200000;

        var rights = Rights.Read | Rights.Write;
        var byteRights = ByteRights.Read | ByteRights.Write;
        Enum boxedReceiver = rights;

        report.AppendLine(Measure("Rights.HasFlag(Rights) (same type)", iterations,
            _ => { if (rights.HasFlag(Rights.Read)) s_sink++; }));
        report.AppendLine(Measure("ByteRights.HasFlag(ByteRights) (same type, byte-backed)", iterations,
            _ => { if (byteRights.HasFlag(ByteRights.Read)) s_sink++; }));
        report.AppendLine(Measure("((Enum)rights).HasFlag(Rights) (receiver already boxed)", iterations,
            _ => { if (boxedReceiver.HasFlag(Rights.Read)) s_sink++; }));
        report.AppendLine(Measure("(a & b) == b (control: what the code fix emits)", iterations,
            _ => { if ((rights & Rights.Read) == Rights.Read) s_sink++; }));

        report.AppendLine(MeasureTime("Rights.HasFlag(Rights)", iterations,
            _ => { if (rights.HasFlag(Rights.Read)) s_sink++; }));
        report.AppendLine(MeasureTime("(a & b) == b", iterations,
            _ => { if ((rights & Rights.Read) == Rights.Read) s_sink++; }));

        // Printed so a reader can tell the loops ran at all. A sink that stayed at zero
        // would mean every branch was false, and the allocation numbers would describe
        // nothing.
        report.AppendLine(Line("sink (non-zero means the calls actually ran)", s_sink.ToString()));
    }

    /// <summary>
    /// Whether calling a method a value type inherits rather than overrides still boxes the
    /// receiver, which is UPA0026's premise. GetHashCode is the probe rather than ToString
    /// because it returns an int: ToString allocates a string either way, so it cannot tell
    /// a boxed receiver from an unboxed one.
    /// </summary>
    private static void AppendInheritedCallDeltas(StringBuilder report)
    {
        const int iterations = 200000;

        var intEnum = IntEnum.B;
        var plain = new PlainKey { X = 1, Y = 2 };
        var equatable = new EquatableKey { X = 1, Y = 2 };

        report.AppendLine(Measure("IntEnum.GetHashCode() (inherited from Enum)", iterations,
            _ => s_sink += intEnum.GetHashCode()));
        report.AppendLine(Measure("PlainKey.GetHashCode() (inherited from ValueType)", iterations,
            _ => s_sink += plain.GetHashCode()));
        report.AppendLine(Measure("EquatableKey.GetHashCode() (overridden: control)", iterations,
            _ => s_sink += equatable.GetHashCode()));
        report.AppendLine(Measure("IntEnum.Equals(object) (inherited)", iterations,
            _ => { if (intEnum.Equals(IntEnum.B)) s_sink++; }));

        // GetType returns a Type, so nothing but the receiver box can allocate here.
        report.AppendLine(Measure("PlainKey.GetType() (inherited from Object)", iterations,
            _ => { if (plain.GetType() is object) s_sink++; }));

        // ToString allocates a string either way; the difference between the two rows is
        // the receiver box and nothing else.
        report.AppendLine(Measure("PlainKey.ToString() (inherited)", iterations / 10,
            _ => s_sink += plain.ToString().Length));
        report.AppendLine(Measure("OverridingKey.ToString() (overridden: control)", iterations / 10,
            _ => s_sink += new OverridingKey { X = 1 }.ToString().Length));

        // The receiver box on its own. Both rows call Equals(object) and both pass an
        // argument that was boxed once, before the loop - so nothing inside the loop can
        // allocate except the receiver, and the only difference between the rows is
        // whether the struct overrides the method.
        object preBoxedPlain = new PlainKey { X = 1, Y = 2 };
        object preBoxedEquatable = new EquatableKey { X = 1, Y = 2 };
        object preBoxedEnum = IntEnum.B;

        report.AppendLine(Measure("PlainKey.Equals(pre-boxed) (inherited)", iterations,
            _ => { if (plain.Equals(preBoxedPlain)) s_sink++; }));
        report.AppendLine(Measure("EquatableKey.Equals(pre-boxed) (overridden: control)", iterations,
            _ => { if (equatable.Equals(preBoxedEquatable)) s_sink++; }));
        report.AppendLine(Measure("IntEnum.Equals(pre-boxed) (inherited)", iterations,
            _ => { if (intEnum.Equals(preBoxedEnum)) s_sink++; }));

        // GetType on an enum receiver; the struct case is measured above.
        report.AppendLine(Measure("IntEnum.GetType() (inherited from Object)", iterations,
            _ => { if (intEnum.GetType() is object) s_sink++; }));

        // Whether the elision that removes HasFlag's argument box needs a constant. If it
        // only fires for one shape, an analyzer exclusion has to be that narrow too.
        var flags = Rights.Read | Rights.Write;
        var flag = Rights.Read;
        report.AppendLine(Measure("HasFlag with a constant argument", iterations,
            _ => { if (flags.HasFlag(Rights.Read)) s_sink++; }));
        report.AppendLine(Measure("HasFlag with a variable argument", iterations,
            _ => { if (flags.HasFlag(flag)) s_sink++; }));
        report.AppendLine(Measure("HasFlag with a computed argument", iterations,
            i => { if (flags.HasFlag((i & 1) == 0 ? Rights.Read : Rights.Write)) s_sink++; }));
    }

    /// <summary>
    /// A5 measured HasFlag with a constant and with a variable argument at 0.00 B/op and with
    /// a computed argument allocating, and the difference between those rows has two possible
    /// causes that the rows themselves cannot tell apart: the runtime removes the box, or the
    /// box is loop-invariant and gets created once outside the loop. Only the first would
    /// justify UPA0006 staying quiet, because a per-frame method is entered afresh every frame
    /// and has no loop to hoist anything out of.
    ///
    /// The control decides it. TakeEnum is opaque and takes a System.Enum, so a box has to
    /// exist by the time it is called - the runtime can elide nothing, it can only build the
    /// box once and reuse it. If the constant row is therefore non-zero, this harness does not
    /// hoist invariant boxes, and A5's zeros mean the HasFlag call itself carries no box. If
    /// the constant row reads zero, the harness hoists, A5 says nothing about per-call cost,
    /// and the question is not answerable by measuring a loop.
    /// </summary>
    private static void AppendArgumentBoxDeltas(StringBuilder report)
    {
        const int iterations = 200000;

        var flag = Rights.Read;
        var flags = Rights.Read | Rights.Write;

        report.AppendLine(Measure("control: TakeEnum(constant) - box cannot be elided", iterations,
            _ => { if (TakeEnum(Rights.Read)) s_sink++; }));
        report.AppendLine(Measure("control: TakeEnum(variable) - box cannot be elided", iterations,
            _ => { if (TakeEnum(flag)) s_sink++; }));
        report.AppendLine(Measure("control: TakeEnum(computed) - box cannot be hoisted", iterations,
            i => { if (TakeEnum((i & 1) == 0 ? Rights.Read : Rights.Write)) s_sink++; }));

        // The same three shapes, but the call sits inside a method the loop cannot hoist
        // out of, and the receiver varies per iteration so the whole call is not invariant.
        report.AppendLine(Measure("HasFlag(constant) per call, varying receiver", iterations,
            i => { if (HasFlagConstant((i & 1) == 0 ? flags : Rights.Write)) s_sink++; }));
        report.AppendLine(Measure("HasFlag(variable) per call, varying receiver", iterations,
            i => { if (HasFlagArgument((i & 1) == 0 ? flags : Rights.Write, flag)) s_sink++; }));
        report.AppendLine(Measure("HasFlag(computed) per call, varying receiver", iterations,
            i => { if (HasFlagArgument((i & 1) == 0 ? flags : Rights.Write,
                (i & 1) == 0 ? Rights.Read : Rights.Write)) s_sink++; }));

        // The exclusion an analyzer can write has to name syntax, so every shape it would
        // name needs its own row. A constant, a local and a parameter are covered above;
        // these are the two remaining spellings that read like a plain load.
        report.AppendLine(Measure("HasFlag(static readonly field) per call", iterations,
            i => { if (HasFlagArgument((i & 1) == 0 ? flags : Rights.Write, s_readField)) s_sink++; }));
        // The holder is built once: constructing it inside the loop would allocate a class
        // per iteration and drown the thing being measured.
        var holder = new FlagHolder();
        report.AppendLine(Measure("HasFlag(instance field) per call", iterations,
            i => { if (holder.Check((i & 1) == 0 ? flags : Rights.Write)) s_sink++; }));
        report.AppendLine(Measure("HasFlag(property) per call", iterations,
            i => { if (holder.CheckViaProperty((i & 1) == 0 ? flags : Rights.Write)) s_sink++; }));

        // And the shape A5 measured as allocating, written the way a call site writes it -
        // inline, with no method boundary in between. If this one stays non-zero while the
        // rows above read zero, the boundary is the spelling at the call site.
        report.AppendLine(Measure("HasFlag(ternary written inline) per call", iterations,
            i => { if (flags.HasFlag((i & 1) == 0 ? Rights.Read : Rights.Write)) s_sink++; }));

        // Time corroborates: a call that boxes cannot cost the same as one that does not.
        report.AppendLine(MeasureTime("HasFlag(constant) per call", iterations,
            i => { if (HasFlagConstant((i & 1) == 0 ? flags : Rights.Write)) s_sink++; }));
        report.AppendLine(MeasureTime("(a & b) == b per call", iterations,
            i => { if (BitwiseConstant((i & 1) == 0 ? flags : Rights.Write)) s_sink++; }));
        report.AppendLine(MeasureTime("TakeEnum(constant) per call", iterations,
            _ => { if (TakeEnum(Rights.Read)) s_sink++; }));

        report.AppendLine(Line("sink (non-zero means the calls actually ran)", s_sink.ToString()));
    }

    /// <summary>
    /// UPA0012 says assigning <c>text</c> allocates an intermediate string every frame and
    /// that <c>SetText</c>'s formatting overloads write into the component's buffer instead.
    /// That second half is TextMeshPro's claim; this is the first time this project has put a
    /// number on it.
    /// </summary>
    /// <remarks>
    /// Both arms mark the text dirty, so mesh regeneration - which needs a font asset and
    /// happens on a later frame - is common to them and cancels out of the difference. What is
    /// left between the rows is the string, which is exactly what the rule claims.
    ///
    /// The component is created once. Failing to create it is reported rather than skipped: a
    /// section that quietly measures nothing reads the same as one that measured zero.
    /// </remarks>
    private static void AppendTextMeshProDeltas(StringBuilder report)
    {
        const int iterations = 20000;

        TMPro.TMP_Text label;
        try
        {
            var host = new GameObject("MeasurementLabel");
            label = host.AddComponent<TMPro.TextMeshPro>();
        }
        catch (Exception exception)
        {
            report.AppendLine(Line("could not create a TMP_Text", exception.GetType().Name + ": " + exception.Message));
            return;
        }

        if (label == null)
        {
            report.AppendLine(Line("could not create a TMP_Text", "AddComponent returned null"));
            return;
        }

        // Control: a string that certainly is allocated, handed to a call that cannot be
        // removed. Without it, a zero in the rows below could be the runtime or could be the
        // instrument.
        report.AppendLine(Measure("control: score.ToString() into an opaque call", iterations,
            i => { s_sink += TakeString((i * 0.5f).ToString()).Length; }));

        report.AppendLine(Measure("label.text = score.ToString()", iterations,
            i => { label.text = (i * 0.5f).ToString(); }));

        report.AppendLine(Measure("label.SetText(\"{0}\", score)", iterations,
            i => { label.SetText("{0}", i * 0.5f); }));

        // The shape the rule also reports and the one it has least to say about: no new string
        // is built, only the dirty flag is set.
        report.AppendLine(Measure("label.text = a constant string", iterations,
            _ => { label.text = "score"; }));

        report.AppendLine(Line("sink (non-zero means the calls actually ran)", s_sink.ToString()));
        report.AppendLine(Line("text length after the run", label.text.Length.ToString()));
    }

    /// <summary>Opaque consumer of a string, so building one cannot be optimized away.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static string TakeString(string value) => value;

    /// <summary>
    /// UPA2000 tells a project with ZString to "format without intermediate string
    /// allocations". This measures what that buys, and where it buys nothing.
    /// </summary>
    /// <remarks>
    /// Both of the rule's documented forms produce a string, so neither can be free. What
    /// separates them is what happens on the way there: <c>"score: " + score</c> binds to the
    /// string + object operator and boxes the int, while ZString's generic overloads do not.
    /// The row that can be free is the builder, when its result is consumed without being
    /// turned into a string at all.
    /// </remarks>
    private static void AppendZStringDeltas(StringBuilder report)
    {
        // Enough collections that one unlucky one does not decide the row.
        const int Collections = 20;

        var prefix = "score: ";
        var suffix = " pts";

        report.AppendLine(MeasureAllocationRate("control: string.Concat(prefix, suffix) into an opaque call", Collections,
            _ => { s_sink += TakeString(string.Concat(prefix, suffix)).Length; }));

        // The shape the rule reports, with the operand that makes it interesting.
        report.AppendLine(MeasureAllocationRate("\"score: \" + score (int operand)", Collections,
            i => { s_sink += TakeString(prefix + i).Length; }));

        report.AppendLine(MeasureAllocationRate("ZString.Concat(\"score: \", score)", Collections,
            i => { s_sink += TakeString(Cysharp.Text.ZString.Concat(prefix, i)).Length; }));

        report.AppendLine(MeasureAllocationRate("ZString.Format(\"score: {0}\", score)", Collections,
            i => { s_sink += TakeString(Cysharp.Text.ZString.Format("score: {0}", i)).Length; }));

        // Two strings: no boxing to avoid, so this pair says where the advice pays nothing.
        report.AppendLine(MeasureAllocationRate("prefix + suffix (two strings)", Collections,
            _ => { s_sink += TakeString(prefix + suffix).Length; }));
        report.AppendLine(MeasureAllocationRate("ZString.Concat(prefix, suffix) (two strings)", Collections,
            _ => { s_sink += TakeString(Cysharp.Text.ZString.Concat(prefix, suffix)).Length; }));

        // The one arm that can be zero: the builder, consumed without materialising a string.
        report.AppendLine(MeasureAllocationRate("ZString.CreateStringBuilder, consumed without ToString", Collections,
            i =>
            {
                using (var builder = Cysharp.Text.ZString.CreateStringBuilder())
                {
                    builder.Append(prefix);
                    builder.Append(i);
                    s_sink += builder.Length;
                }
            }));

        report.AppendLine(MeasureAllocationRate("StringBuilder reused, consumed without ToString (control)", Collections,
            i =>
            {
                s_reusedBuilder.Clear();
                s_reusedBuilder.Append(prefix);
                s_reusedBuilder.Append(i);
                s_sink += s_reusedBuilder.Length;
            }));

        report.AppendLine(Line("sink (non-zero means the calls actually ran)", s_sink.ToString()));
    }

    /// <summary>
    /// How many iterations fit between gen-0 collections. More iterations means less
    /// allocated per iteration, which is the comparison A10 needs.
    /// </summary>
    /// <remarks>
    /// <see cref="Measure"/> answers "did this allocate at all" and nothing finer: once a
    /// collection runs mid-loop its delta is a lower bound. Every row here allocates a string,
    /// so every row collects, and the first attempt read 0.20 B/op for the arm that allocates
    /// most - the collector had already taken it back.
    ///
    /// <c>GC.GetAllocatedBytesForCurrentThread</c> would have been the exact instrument. It
    /// crashes the IL2CPP player: not an exception, a native crash the harness cannot catch,
    /// so do not reach for it again.
    ///
    /// The collector's own budget is the instrument left. It is fixed for a given player, so
    /// it cancels out when two rows are compared, and the ratio between rows is the ratio of
    /// their allocation. Absolute bytes are not recovered - only the comparison is, and the
    /// comparison is the question.
    /// </remarks>
    private static string MeasureAllocationRate(string label, int collections, Action<int> body)
    {
        const long IterationCap = 50_000_000;

        for (var i = 0; i < WarmupIterations; i++)
        {
            body(i);
        }

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var before = GC.CollectionCount(0);
        long iterations = 0;
        while (GC.CollectionCount(0) - before < collections && iterations < IterationCap)
        {
            body((int)(iterations & int.MaxValue));
            iterations++;
        }

        if (iterations >= IterationCap)
        {
            return string.Format(
                "[MEASURE] {0} | no gen0 collection in {1} iterations | allocates nothing this instrument can see",
                label, IterationCap);
        }

        return string.Format(
            "[MEASURE] {0} | {1} iterations per {2} gen0 collections | {3:F0} iterations/collection | more is less allocation",
            label, iterations, collections, (double)iterations / collections);
    }

    /// <summary>The advice a project without ZString gets: one builder, reused.</summary>
    private static readonly StringBuilder s_reusedBuilder = new StringBuilder(64);

    /// <summary>
    /// Opaque consumer of a boxed enum. The parameter type is the one HasFlag declares, so
    /// this is the conversion UPA0006 reports, with nothing else in the frame.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static bool TakeEnum(Enum boxed) => boxed != null;

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static bool HasFlagConstant(Rights value) => value.HasFlag(Rights.Read);

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static bool HasFlagArgument(Rights value, Rights flag) => value.HasFlag(flag);

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static bool BitwiseConstant(Rights value) => (value & Rights.Read) == Rights.Read;

    private static readonly Rights s_readField = Rights.Read;

    /// <summary>Holder for the field and property argument spellings.</summary>
    private sealed class FlagHolder
    {
        private readonly Rights _flag = Rights.Read;

        private Rights Flag => _flag;

        [MethodImpl(MethodImplOptions.NoInlining)]
        public bool Check(Rights value) => value.HasFlag(_flag);

        [MethodImpl(MethodImplOptions.NoInlining)]
        public bool CheckViaProperty(Rights value) => value.HasFlag(Flag);
    }

    private struct OverridingKey
    {
        public int X;

        public override string ToString() => X == 1 ? "one" : "other";
    }

    /// <summary>
    /// Rules whose payoff is time rather than bytes. A saving that does not survive a
    /// measurement is not a saving, and a warning that costs attention to buy nanoseconds
    /// is noise however true its reasoning sounds.
    /// </summary>
    private static void AppendTimeOnlyComparisons(StringBuilder report)
    {
        const int iterations = 200000;

        var a = new Vector3(1f, 2f, 3f);
        var b = new Vector3(4f, 5f, 6f);
        var list = new List<int>();
        for (var i = 0; i < 64; i++)
        {
            list.Add(i);
        }

        // UPA0021: the square root against the comparison it can be folded into.
        report.AppendLine(MeasureTime("Vector3.Distance(a,b) < 5f", iterations,
            _ => { if (Vector3.Distance(a, b) < 5f) s_sink++; }));
        report.AppendLine(MeasureTime("(a-b).sqrMagnitude < 25f", iterations,
            _ => { if ((a - b).sqrMagnitude < 25f) s_sink++; }));

        // UPA0009: the property read the rule asks to hoist, against the hoisted form.
        report.AppendLine(MeasureTime("for (i < list.Count) over 64 items", iterations / 64,
            _ => { for (var i = 0; i < list.Count; i++) { s_sink += list[i]; } }));
        report.AppendLine(MeasureTime("for (i < count) hoisted, 64 items", iterations / 64,
            _ => { var count = list.Count; for (var i = 0; i < count; i++) { s_sink += list[i]; } }));

        // UPA1000. The first pair has one derived class, where whole-program analysis can
        // devirtualize whatever the declaration says - so it shows sealing adds nothing
        // there, not that it never would. The second pair is the case the rule is actually
        // about: several subclasses exist, so the call site cannot be resolved from the
        // hierarchy, and sealed is the only thing that could tell the backend otherwise.
        Leaf leaf = new Leaf();
        Base viaBase = leaf;
        report.AppendLine(MeasureTime("one subclass: call through the base type", iterations,
            _ => s_sink += viaBase.Value()));
        report.AppendLine(MeasureTime("one subclass: call on the sealed leaf", iterations,
            _ => s_sink += leaf.Value()));

        var sealedLeaf = new SealedLeaf();
        var openLeaf = new OpenLeaf();
        Spread viaSpread = sealedLeaf;
        // Constructed so the backend cannot prove which subclass any Spread reference holds.
        var spreads = new Spread[] { sealedLeaf, openLeaf, new OtherLeaf(), new FourthLeaf() };
        foreach (var s in spreads)
        {
            s_sink += s.Value();
        }

        report.AppendLine(MeasureTime("four subclasses: call through the base type", iterations,
            i => s_sink += spreads[i & 3].Value()));
        report.AppendLine(MeasureTime("four subclasses: call on the sealed leaf", iterations,
            _ => s_sink += sealedLeaf.Value()));
        report.AppendLine(MeasureTime("four subclasses: call on the unsealed leaf", iterations,
            _ => s_sink += openLeaf.Value()));
        report.AppendLine(MeasureTime("four subclasses: sealed leaf via the base type", iterations,
            _ => s_sink += viaSpread.Value()));
    }

    /// <summary>
    /// The rules whose premise is "this allocates", checked on the backend that ships. Each
    /// row names the rule it belongs to, because a number without a claim attached is not
    /// evidence of anything.
    /// </summary>
    private static void AppendRemainingPremiseDeltas(StringBuilder report)
    {
        const int iterations = 200000;
        const int few = 20000;

        var host = new GameObject("MeasurementProbe");
        try
        {
            host.AddComponent<Animator>();
            var numbers = new List<int>();
            for (var i = 0; i < 32; i++)
            {
                numbers.Add(i);
            }

            var source = new int[32];
            var value = 7;
            var text = "frame";

            // UPA0001 - Unity documents this as allocating in the editor but not in a build.
            report.AppendLine(Measure("UPA0001 GetComponent<Animator>()", iterations,
                _ => { if (host.GetComponent<Animator>() is object) s_sink++; }));
            report.AppendLine(Measure("UPA0001 TryGetComponent<Animator>()", iterations,
                _ => { if (host.TryGetComponent<Animator>(out var found) && found is object) s_sink++; }));

            // UPA0002 - the marshalled string, and the comparison the rule steers toward.
            report.AppendLine(Measure("UPA0002 gameObject.name", iterations / 4,
                _ => s_sink += host.name.Length));
            report.AppendLine(Measure("UPA0002 gameObject.tag", iterations / 4,
                _ => s_sink += host.tag.Length));
            report.AppendLine(Measure("UPA0002 CompareTag (control)", iterations / 4,
                _ => { if (host.CompareTag("Untagged")) s_sink++; }));

            // UPA0006 - interpolation holes box under the C# 9 Unity pins; the handler that
            // removes that arrived in C# 10.
            report.AppendLine(Measure("UPA0006 $\"x{value}\" (interpolation hole)", few,
                _ => s_sink += $"x{value}".Length));
            report.AppendLine(Measure("UPA0006 new List<int>() per call", few,
                _ => s_sink += new List<int>().Count + 1));

            // UPA0007 - a lambda that captures needs a closure object per construction.
            // Built through a method so each call captures a fresh local. Capturing a
            // variable of the enclosing method instead would hoist it into one display
            // class built once, and the loop would measure a closure it never created.
            report.AppendLine(Measure("UPA0007 capturing lambda constructed per call", few,
                i => { var f = MakeCapturing(i); s_sink += f(); }));
            report.AppendLine(Measure("UPA0007 non-capturing lambda (control)", few,
                _ => { var f = MakeConstant(); s_sink += f(); }));

            // UPA0013 - LINQ over a List, the shape the rule is written against.
            report.AppendLine(Measure("UPA0013 numbers.Any(n => n > 30)", few,
                _ => { if (System.Linq.Enumerable.Any(numbers, n => n > 30)) s_sink++; }));
            report.AppendLine(Measure("UPA0013 hand-written loop (control)", few,
                _ => { foreach (var n in numbers) { if (n > 30) { s_sink++; break; } } }));

            // UPA0019 - yielding a value type from a coroutine boxes it.
            report.AppendLine(Measure("UPA0019 boxing an int to object", iterations,
                _ => { object boxed = value; s_sink += boxed is int ? 1 : 0; }));

            // UPA0027 - the implicit array an expanded params call creates.
            report.AppendLine(Measure("UPA0027 Mathf.Max(a, b, c) (params expansion)", few,
                _ => s_sink += (int)Mathf.Max(1f, 2f, 3f)));
            report.AppendLine(Measure("UPA0027 Mathf.Max(a, b) (arity-2 overload)", few,
                _ => s_sink += (int)Mathf.Max(1f, 2f)));

            // UPA0029 - repeated Add regrows the backing array; AddRange pre-sizes once.
            report.AppendLine(Measure("UPA0029 32 x Add into a fresh List", few / 10,
                _ => { var l = new List<int>(); for (var i = 0; i < 32; i++) { l.Add(i); } s_sink += l.Count; }));
            report.AppendLine(Measure("UPA0029 AddRange of the same 32", few / 10,
                _ => { var l = new List<int>(); l.AddRange(source); s_sink += l.Count; }));

            // UPA2000 - string concatenation in a per-frame method.
            report.AppendLine(Measure("UPA2000 text + \": \" + value", few,
                _ => s_sink += (text + ": " + value).Length));
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(host);
        }
    }

    private static Func<int> MakeCapturing(int captured) => () => captured + 1;

    /// <summary>The same shape without a capture, so the delegate can be cached.</summary>
    private static Func<int> MakeConstant() => () => 1;

    private class Base
    {
        public virtual int Value() => 1;
    }

    /// <summary>
    /// A hierarchy wide enough that a call through the base type cannot be resolved from
    /// the class graph. Sealing the leaf is the only remaining signal, which is the
    /// situation UPA1000 is written for.
    /// </summary>
    private class Spread
    {
        public virtual int Value() => 1;
    }

    private sealed class SealedLeaf : Spread
    {
        public override int Value() => 2;
    }

    private class OpenLeaf : Spread
    {
        public override int Value() => 3;
    }

    private sealed class OtherLeaf : Spread
    {
        public override int Value() => 4;
    }

    private sealed class FourthLeaf : Spread
    {
        public override int Value() => 5;
    }

    private sealed class Leaf : Base
    {
        public override int Value() => 2;
    }

    /// <summary>
    /// Wall-clock per operation. Allocation answers whether the premise survives; this
    /// answers whether the advice is worth a warning even if it does not.
    /// </summary>
    private static string MeasureTime(string label, int iterations, Action<int> body)
    {
        for (var i = 0; i < WarmupIterations; i++)
        {
            body(i);
        }

        var watch = System.Diagnostics.Stopwatch.StartNew();
        for (var i = 0; i < iterations; i++)
        {
            body(i);
        }

        watch.Stop();
        var nanosecondsPerOperation = watch.Elapsed.TotalMilliseconds * 1_000_000.0 / iterations;
        return string.Format(
            "[MEASURE] {0} | {1:F2} ms over {2} iterations | {3:F2} ns/op",
            label, watch.Elapsed.TotalMilliseconds, iterations, nanosecondsPerOperation);
    }

    private static string Measure(string label, int iterations, Action<int> body)
    {
        for (var i = 0; i < WarmupIterations; i++)
        {
            body(i);
        }

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var collectionsBefore = GC.CollectionCount(0);
        var before = GC.GetTotalMemory(false);

        for (var i = 0; i < iterations; i++)
        {
            body(i);
        }

        var after = GC.GetTotalMemory(false);
        var collections = GC.CollectionCount(0) - collectionsBefore;
        var delta = after - before;
        var perOperation = (double)delta / iterations;

        var verdict = collections > 0
            ? "allocates (lower bound: a collection ran mid-measurement)"
            : perOperation >= 1.0 ? "allocates" : "no measurable allocation";

        return string.Format(
            "[MEASURE] {0} | {1} bytes over {2} iterations | {3:F2} bytes/op | gen0 collections: {4} | {5}",
            label, delta, iterations, perOperation, collections, verdict);
    }

    private static string GetRuntimeDescription()
    {
        // Mono.Runtime is NOT a reliable discriminator: IL2CPP ships a stripped copy of the
        // same class library, so the type resolves there too and only GetDisplayName comes
        // back empty. The build layout is what actually says which backend produced this
        // player — an IL2CPP build writes an il2cpp_data folder beside the managed data.
        var il2cppData = Path.Combine(Application.dataPath, "il2cpp_data");
        var backend = Directory.Exists(il2cppData) ? "IL2CPP" : "Mono";

        var monoRuntime = Type.GetType("Mono.Runtime");
        var displayName = monoRuntime?.GetMethod(
            "GetDisplayName",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        var version = displayName?.Invoke(null, null) as string;

        return version is null
            ? backend + " (class library version unavailable)"
            : backend + " / class library reports: " + version;
    }

    private static string Line(string label, string value) => "[MEASURE] " + label + " | " + value;
}
