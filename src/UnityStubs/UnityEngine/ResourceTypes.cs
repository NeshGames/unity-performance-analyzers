using System;

namespace UnityEngine
{
    public class ResourceRequest
    {
    }

    public class Resources
    {
        public static Object Load(string path) => null!;
        public static T Load<T>(string path) where T : Object => null!;
        public static Object Load(string path, Type systemTypeInstance) => null!;
        public static Object[] LoadAll(string path) => null!;
        public static T[] LoadAll<T>(string path) where T : Object => null!;
        public static Object[] LoadAll(string path, Type systemTypeInstance) => null!;
        public static ResourceRequest LoadAsync(string path) => null!;
        public static ResourceRequest LoadAsync<T>(string path) where T : Object => null!;
        public static ResourceRequest LoadAsync(string path, Type type) => null!;
        public static ResourceRequest UnloadAsync(Object assetToUnload) => null!;
        public static void UnloadUnusedAssets() { }
    }
}
