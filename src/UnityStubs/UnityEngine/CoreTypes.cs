using System;
using System.Collections.Generic;

namespace UnityEngine
{
    public class Object
    {
        public string name { get; set; } = string.Empty;

        public static T FindObjectOfType<T>() => default!;
        public static Object FindObjectOfType(Type type) => null!;
        public static T[] FindObjectsOfType<T>() => null!;
        public static Object[] FindObjectsOfType(Type type) => null!;
        public static T FindFirstObjectByType<T>() => default!;
        public static Object FindFirstObjectByType(Type type) => null!;
        public static T FindAnyObjectByType<T>() => default!;
        public static Object FindAnyObjectByType(Type type) => null!;
        public static T[] FindObjectsByType<T>(FindObjectsSortMode sortMode) => null!;
        public static Object[] FindObjectsByType(Type type, FindObjectsSortMode sortMode) => null!;

        // Inherited statics. Everything derived from Object -- which is every MonoBehaviour --
        // calls these with no receiver, and that is the form UPA0031 is about.
        public static Object Instantiate(Object original) => original;
        public static Object Instantiate(Object original, Transform parent) => original;
        public static T Instantiate<T>(T original) where T : Object => original;
        public static void Destroy(Object obj) { }
        public static void Destroy(Object obj, float t) { }
        public static void DestroyImmediate(Object obj) { }
    }

    public enum FindObjectsSortMode
    {
        None,
        InstanceID,
    }

    public class GameObject : Object
    {
        public string tag { get; set; } = string.Empty;

        public static GameObject Find(string name) => null!;
        public static GameObject FindWithTag(string tag) => null!;
        public static GameObject FindGameObjectWithTag(string tag) => null!;
        public static GameObject[] FindGameObjectsWithTag(string tag) => null!;

        public void SetActive(bool value) { }

        public bool CompareTag(string tag) => false;

        public void SendMessage(string methodName) { }
        public void SendMessage(string methodName, object value) { }
        public void SendMessageUpwards(string methodName) { }
        public void SendMessageUpwards(string methodName, object value) { }
        public void BroadcastMessage(string methodName) { }
        public void BroadcastMessage(string methodName, object value) { }

        public T GetComponent<T>() => default!;
        public Component GetComponent(Type type) => null!;
        public T[] GetComponents<T>() => null!;
        public T GetComponentInChildren<T>() => default!;
        public T[] GetComponentsInChildren<T>() => null!;
        public T GetComponentInParent<T>() => default!;
        public T[] GetComponentsInParent<T>() => null!;
        public void GetComponents<T>(List<T> results) { }
        public void GetComponentsInChildren<T>(List<T> results) { }
        public void GetComponentsInParent<T>(bool includeInactive, List<T> results) { }
        public bool TryGetComponent<T>(out T component)
        {
            component = default!;
            return false;
        }
    }

    public class Component : Object
    {
        public string tag { get; set; } = string.Empty;

        public GameObject gameObject => null!;

        public Transform transform => null!;

        public bool CompareTag(string tag) => false;

        public void SendMessage(string methodName) { }
        public void SendMessage(string methodName, object value) { }
        public void SendMessageUpwards(string methodName) { }
        public void SendMessageUpwards(string methodName, object value) { }
        public void BroadcastMessage(string methodName) { }
        public void BroadcastMessage(string methodName, object value) { }

        public T GetComponent<T>() => default!;
        public Component GetComponent(Type type) => null!;
        public T[] GetComponents<T>() => null!;
        public T GetComponentInChildren<T>() => default!;
        public T[] GetComponentsInChildren<T>() => null!;
        public T GetComponentInParent<T>() => default!;
        public T[] GetComponentsInParent<T>() => null!;
        public void GetComponents<T>(List<T> results) { }
        public void GetComponentsInChildren<T>(List<T> results) { }
        public void GetComponentsInParent<T>(bool includeInactive, List<T> results) { }
        public bool TryGetComponent<T>(out T component)
        {
            component = default!;
            return false;
        }
    }

    public class Behaviour : Component
    {
    }

    public class MonoBehaviour : Behaviour
    {
    }

    public class Transform : Component
    {
    }

    public class Rigidbody : Component
    {
    }

    /// <summary>
    /// Mirrors the overload set that makes UPA0027 worth having: Unity ships arity-2 Max/Min
    /// only, so a third argument silently resolves to the params overload and allocates.
    /// </summary>
    public static class Mathf
    {
        public static float Max(float a, float b) => 0f;
        public static float Max(params float[] values) => 0f;
        public static float Min(float a, float b) => 0f;
        public static float Min(params float[] values) => 0f;
    }
}
