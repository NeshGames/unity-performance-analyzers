namespace UnityEngine
{
    public struct Vector2
    {
        public float magnitude => 0f;
        public float sqrMagnitude => 0f;

        public static float Distance(Vector2 a, Vector2 b) => 0f;
        public static Vector2 operator -(Vector2 a, Vector2 b) => default;
    }

    public struct Vector3
    {
        public float magnitude => 0f;
        public float sqrMagnitude => 0f;

        public static float Distance(Vector3 a, Vector3 b) => 0f;
        public static Vector3 operator -(Vector3 a, Vector3 b) => default;
    }

    public struct Ray
    {
    }

    public struct RaycastHit
    {
    }

    public class Physics
    {
        public static bool Raycast(Vector3 origin, Vector3 direction) => false;

        public static bool Raycast(Vector3 origin, Vector3 direction, out RaycastHit hitInfo)
        {
            hitInfo = default;
            return false;
        }

        public static bool Raycast(Vector3 origin, Vector3 direction, out RaycastHit hitInfo, float maxDistance)
        {
            hitInfo = default;
            return false;
        }

        public static bool Raycast(Vector3 origin, Vector3 direction, out RaycastHit hitInfo, float maxDistance, int layerMask)
        {
            hitInfo = default;
            return false;
        }

        public static bool Raycast(Ray ray, out RaycastHit hitInfo)
        {
            hitInfo = default;
            return false;
        }

        public static bool Raycast(Ray ray, out RaycastHit hitInfo, float maxDistance, int layerMask)
        {
            hitInfo = default;
            return false;
        }

        public static RaycastHit[] RaycastAll(Vector3 origin, Vector3 direction) => null!;

        public static RaycastHit[] RaycastAll(Vector3 origin, Vector3 direction, float maxDistance, int layerMask) => null!;

        public static int RaycastNonAlloc(Vector3 origin, Vector3 direction, RaycastHit[] results) => 0;

        public static int RaycastNonAlloc(Vector3 origin, Vector3 direction, RaycastHit[] results, float maxDistance, int layerMask) => 0;
    }
}
