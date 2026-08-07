using System;

namespace UnityEngine
{
    public class YieldInstruction
    {
    }

    public class WaitForSeconds : YieldInstruction
    {
        public WaitForSeconds(float seconds) { }
    }

    public class WaitForEndOfFrame : YieldInstruction
    {
    }

    public abstract class CustomYieldInstruction
    {
        public abstract bool keepWaiting { get; }
    }

    public class WaitUntil : CustomYieldInstruction
    {
        public WaitUntil(Func<bool> predicate) { }

        public override bool keepWaiting => false;
    }

    public class WaitWhile : CustomYieldInstruction
    {
        public WaitWhile(Func<bool> predicate) { }

        public override bool keepWaiting => false;
    }
}
