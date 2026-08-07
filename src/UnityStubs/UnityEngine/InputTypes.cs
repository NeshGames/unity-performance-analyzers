namespace UnityEngine
{
    public struct Touch
    {
    }

    public class Input
    {
        public static Touch[] touches => null!;
        public static int touchCount => 0;
        public static Touch GetTouch(int index) => default;
    }
}
