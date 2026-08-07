using System;

namespace UnityEngine
{
    public class Object
    {
        public string name { get; set; } = string.Empty;
    }

    public class GameObject : Object
    {
        public string tag { get; set; } = string.Empty;

        public void SetActive(bool value) { }

        public bool CompareTag(string tag) => false;

        public T GetComponent<T>() => default!;
        public Component GetComponent(Type type) => null!;
        public T[] GetComponents<T>() => null!;
        public T GetComponentInChildren<T>() => default!;
        public T[] GetComponentsInChildren<T>() => null!;
        public T GetComponentInParent<T>() => default!;
        public T[] GetComponentsInParent<T>() => null!;
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

        public T GetComponent<T>() => default!;
        public Component GetComponent(Type type) => null!;
        public T[] GetComponents<T>() => null!;
        public T GetComponentInChildren<T>() => default!;
        public T[] GetComponentsInChildren<T>() => null!;
        public T GetComponentInParent<T>() => default!;
        public T[] GetComponentsInParent<T>() => null!;
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
}
