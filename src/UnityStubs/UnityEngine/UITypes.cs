namespace UnityEngine.UI
{
    public class Graphic : Behaviour
    {
    }

    public class Image : Graphic
    {
    }
}

namespace TMPro
{
    public class TMP_Text : UnityEngine.Behaviour
    {
        public string text { get; set; } = string.Empty;

        public void SetText(string format, float arg0) { }
    }

    public class TextMeshProUGUI : TMP_Text
    {
    }
}
