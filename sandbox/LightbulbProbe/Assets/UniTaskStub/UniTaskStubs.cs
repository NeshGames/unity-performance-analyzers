// Minimal Cysharp.Threading.Tasks surface producing an assembly named exactly "UniTask".
// UpaProfile detects UniTask by assembly name, so this stub is enough to exercise the
// conditional advice and the Forget code fix without redistributing the real library.
namespace Cysharp.Threading.Tasks
{
    public struct UniTask
    {
    }

    public static class UniTaskExtensions
    {
        public static void Forget(this UniTask task)
        {
        }
    }
}
