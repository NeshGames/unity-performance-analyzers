using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;

namespace NeshGames.UnityPerformanceAnalyzers.Editor
{
    /// <summary>
    /// Manages the UPA_TARGET_WEBGL scripting define. The define is applied to every build
    /// target group — not just the active one — so the WebGL rules keep running during
    /// day-to-day development on other targets, which is the whole point of a resident
    /// define over UNITY_WEBGL.
    /// </summary>
    internal static class WebGlTargetSupport
    {
        public const string Define = "UPA_TARGET_WEBGL";

        public static bool IsEnabledEverywhere()
        {
            var states = TargetStates();
            return states.Count > 0 && states.All(state => state);
        }

        public static bool IsEnabledSomewhere()
        {
            return TargetStates().Any(state => state);
        }

        public static void SetDefine(bool enabled)
        {
            foreach (var target in ValidTargets())
            {
                var defines = PlayerSettings.GetScriptingDefineSymbols(target)
                    .Split(';')
                    .Where(define => define.Length > 0)
                    .ToList();
                var present = defines.Contains(Define);
                if (enabled && !present)
                {
                    defines.Add(Define);
                }
                else if (!enabled && present)
                {
                    defines.RemoveAll(define => define == Define);
                }
                else
                {
                    continue;
                }

                PlayerSettings.SetScriptingDefineSymbols(target, string.Join(";", defines));
            }
        }

        private static List<bool> TargetStates()
        {
            return ValidTargets()
                .Select(target => PlayerSettings.GetScriptingDefineSymbols(target)
                    .Split(';')
                    .Contains(Define))
                .ToList();
        }

        private static IEnumerable<NamedBuildTarget> ValidTargets()
        {
            foreach (BuildTargetGroup group in Enum.GetValues(typeof(BuildTargetGroup)))
            {
                if (group == BuildTargetGroup.Unknown)
                {
                    continue;
                }

                NamedBuildTarget target;
                try
                {
                    // Throws for obsolete or otherwise unmapped groups.
                    target = NamedBuildTarget.FromBuildTargetGroup(group);
                }
                catch (ArgumentException)
                {
                    continue;
                }

                yield return target;
            }
        }
    }
}
