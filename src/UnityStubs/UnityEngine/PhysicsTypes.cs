namespace UnityEngine
{
    public struct Vector3
    {
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
