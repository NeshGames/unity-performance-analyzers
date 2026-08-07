using System.Collections.Generic;

namespace UnityEngine
{
    public struct Color
    {
    }

    public class Camera : Behaviour
    {
        public static Camera main => null!;
        public static Camera[] allCameras => null!;
        public static int allCamerasCount => 0;
        public static int GetAllCameras(Camera[] cameras) => 0;
    }

    public struct Vector4
    {
        public float magnitude => 0f;
        public float sqrMagnitude => 0f;

        public static float Distance(Vector4 a, Vector4 b) => 0f;
        public static Vector4 operator -(Vector4 a, Vector4 b) => default;
    }

    public class Material : Object
    {
        public Color color { get; set; }

        public void SetFloat(string name, float value) { }
        public void SetFloat(int nameID, float value) { }
        public float GetFloat(string name) => 0f;
        public float GetFloat(int nameID) => 0f;
        public bool HasProperty(string name) => false;
        public bool HasProperty(int nameID) => false;
        public void SetColor(string name, Color value) { }
        public void SetColor(int nameID, Color value) { }
    }

    public class MaterialPropertyBlock
    {
        public void SetFloat(string name, float value) { }
        public void SetFloat(int nameID, float value) { }
        public void SetColor(string name, Color value) { }
        public void SetColor(int nameID, Color value) { }
    }

    public class Shader : Object
    {
        public static int PropertyToID(string name) => 0;
        public static void SetGlobalVector(string name, Vector4 value) { }
        public static void SetGlobalVector(int nameID, Vector4 value) { }
        public static Vector4 GetGlobalVector(string name) => default;
        public static Vector4 GetGlobalVector(int nameID) => default;
    }

    public class Mesh : Object
    {
        public void RecalculateBounds() { }
    }

    public class Renderer : Component
    {
        public Material material { get; set; } = null!;
        public Material[] materials { get; set; } = null!;
        public Material sharedMaterial { get; set; } = null!;
        public Material[] sharedMaterials { get; set; } = null!;

        public void GetSharedMaterials(List<Material> m) { }
    }

    public class SkinnedMeshRenderer : Renderer
    {
    }

    public class MeshFilter : Component
    {
        public Mesh mesh { get; set; } = null!;
        public Mesh sharedMesh { get; set; } = null!;
    }

    public class AnimatorControllerParameter
    {
    }

    public class Animator : Behaviour
    {
        public AnimatorControllerParameter[] parameters => null!;
        public int parameterCount => 0;
        public AnimatorControllerParameter GetParameter(int index) => null!;

        public static int StringToHash(string name) => 0;
        public void Play(string stateName) { }
        public void Play(int stateNameHash) { }
        public void CrossFade(string stateName, float normalizedTransitionDuration) { }
        public void CrossFade(int stateHashName, float normalizedTransitionDuration) { }
        public void CrossFadeInFixedTime(string stateName, float fixedTransitionDuration) { }
        public void CrossFadeInFixedTime(int stateHashName, float fixedTransitionDuration) { }
        public void SetFloat(string name, float value) { }
        public void SetFloat(int id, float value) { }
        public float GetFloat(string name) => 0f;
        public float GetFloat(int id) => 0f;
        public void SetTrigger(string name) { }
        public void SetTrigger(int id) { }
        public void ResetTrigger(string name) { }
        public void ResetTrigger(int id) { }
    }
}
