using System;
using System.Collections.Generic;
using System.IO;
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
