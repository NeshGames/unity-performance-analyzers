using System;

// The editor callback attributes, with Unity's own names and namespaces. Two of them have no
// Attribute suffix, which is the whole reason these exist: a check written against the
// conventional spelling passed every test that never compiled the real one.
namespace UnityEditor
{
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
    public sealed class MenuItem : Attribute
    {
        public MenuItem(string itemName)
        {
        }

        public MenuItem(string itemName, bool isValidateFunction)
        {
        }

        public MenuItem(string itemName, bool isValidateFunction, int priority)
        {
        }
    }

    [AttributeUsage(AttributeTargets.Method)]
    public class InitializeOnLoadMethodAttribute : Attribute
    {
    }

    public abstract class CallbackOrderAttribute : Attribute
    {
    }
}

namespace UnityEditor.Callbacks
{
    [AttributeUsage(AttributeTargets.Method)]
    public sealed class DidReloadScripts : CallbackOrderAttribute
    {
        public DidReloadScripts()
        {
        }

        public DidReloadScripts(int callbackOrder)
        {
        }
    }
}
