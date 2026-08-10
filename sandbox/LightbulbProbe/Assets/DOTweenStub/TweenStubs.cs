using System;
using UnityEngine;

// Minimal DG.Tweening surface producing an assembly named exactly "DOTween".
// UpaProfile detects DOTween by assembly name, so this stub exercises the
// conditional UPA2030-2032 rules end to end without redistributing the real
// library (which the analyzer only ever observes by name and type shape).
namespace DG.Tweening
{
    public abstract class Tween
    {
    }

    public class Tweener : Tween
    {
    }

    public class Sequence : Tween
    {
    }

    public static class DOTween
    {
        public static Sequence Sequence() => null;
        public static Tweener To(Func<float> getter, Action<float> setter, float endValue, float duration) => null;
        public static int Kill(object targetOrId, bool complete = false) => 0;
        public static int Play(object targetOrId) => 0;
        public static bool IsTweening(object targetOrId, bool alsoCheckIfIsPlaying = false) => false;
    }

    public static class ShortcutExtensions
    {
        public static Tweener DOMove(this Transform target, Vector3 endValue, float duration) => null;
        public static Tweener DORotate(this Transform target, Vector3 endValue, float duration) => null;
    }

    public static class TweenSettingsExtensions
    {
        public static T SetLoops<T>(this T t, int loops) where T : Tween => t;
        public static T SetEase<T>(this T t, int ease) where T : Tween => t;
        public static T SetLink<T>(this T t, GameObject gameObject) where T : Tween => t;
        public static T SetId<T>(this T t, object objectId) where T : Tween => t;
        public static T SetAutoKill<T>(this T t, bool autoKillOnCompletion) where T : Tween => t;
    }
}
