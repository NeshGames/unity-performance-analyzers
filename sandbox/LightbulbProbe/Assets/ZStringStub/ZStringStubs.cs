// Minimal Cysharp.Text surface producing an assembly named exactly "ZString". UpaProfile
// detects ZString by assembly name, and the code fix needs the type to exist for its output to
// compile - a stub satisfies both without redistributing the real library.
namespace Cysharp.Text
{
    public static class ZString
    {
        public static string Concat<T1, T2>(T1 arg1, T2 arg2) => string.Empty;

        public static string Concat<T1, T2, T3>(T1 arg1, T2 arg2, T3 arg3) => string.Empty;
    }
}
