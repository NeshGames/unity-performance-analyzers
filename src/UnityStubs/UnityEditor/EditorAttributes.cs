using System;

// Editor-only entry points. EditorOnlyMethods resolves these by metadata name, so the class
// names and namespaces must match Unity's exactly - including which ones carry the
// "Attribute" suffix and which do not. With no stubs here that mismatch shipped unnoticed.
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
}

namespace UnityEditor.Callbacks
{
    [AttributeUsage(AttributeTargets.Method)]
    public sealed class DidReloadScripts : Attribute
    {
        public DidReloadScripts()
        {
        }

        public DidReloadScripts(int callbackOrder)
        {
        }
    }
}
