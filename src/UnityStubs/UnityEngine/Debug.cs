using System;
using System.Diagnostics;

namespace UnityEngine
{
    public class Debug
    {
        public static void Log(object message) { }
        public static void LogWarning(object message) { }
        public static void LogError(object message) { }
        public static void LogFormat(string format, params object[] args) { }
        public static void LogWarningFormat(string format, params object[] args) { }
        public static void LogErrorFormat(string format, params object[] args) { }
        public static void LogException(Exception exception) { }
        public static void LogAssertion(object message) { }
        public static void LogAssertionFormat(string format, params object[] args) { }

        [Conditional("UNITY_ASSERTIONS")]
        public static void Assert(bool condition) { }

        [Conditional("UNITY_ASSERTIONS")]
        public static void AssertFormat(bool condition, string format, params object[] args) { }
    }
}
