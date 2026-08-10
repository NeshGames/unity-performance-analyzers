namespace UnityPerformanceAnalyzers.Tests
{
    /// <summary>
    /// Minimal DG.Tweening surface compiled into DOTween-rule tests as an extra source file.
    /// Profile detection is by assembly name, so tests pair this with
    /// <c>TestMetadataReferences.EmptyAssembly("DOTween")</c>; the types themselves may live
    /// in the test compilation — the analyzers resolve them by metadata name.
    /// </summary>
    internal static class DoTweenTestSources
    {
        public const string Stubs = @"
namespace DG.Tweening
{
    public abstract class Tween { }

    public class Tweener : Tween { }

    public class Sequence : Tween { }

    public static class DOTween
    {
        public static Sequence Sequence() => null;
        public static Tweener To(System.Func<float> getter, System.Action<float> setter, float endValue, float duration) => null;
        public static int Kill(object targetOrId, bool complete = false) => 0;
        public static int Play(object targetOrId) => 0;
        public static bool IsTweening(object targetOrId, bool alsoCheckIfIsPlaying = false) => false;
    }

    public static class ShortcutExtensions
    {
        public static Tweener DOMove(this UnityEngine.Transform target, UnityEngine.Vector3 endValue, float duration) => null;
        public static Tweener DORotate(this UnityEngine.Transform target, UnityEngine.Vector3 endValue, float duration) => null;
    }

    public static class TweenSettingsExtensions
    {
        public static T SetLoops<T>(this T t, int loops) where T : Tween => t;
        public static T SetEase<T>(this T t, int ease) where T : Tween => t;
        public static Sequence AppendCallback(this Sequence sequence, System.Action callback) => sequence;
        public static T SetLink<T>(this T t, UnityEngine.GameObject gameObject) where T : Tween => t;
        public static T SetId<T>(this T t, object objectId) where T : Tween => t;
        public static T SetAutoKill<T>(this T t, bool autoKillOnCompletion) where T : Tween => t;
    }
}
";
    }
}
