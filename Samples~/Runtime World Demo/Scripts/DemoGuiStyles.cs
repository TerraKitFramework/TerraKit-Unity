using UnityEngine;

namespace TerraKit.Samples
{
    internal sealed class DemoGuiStyles
    {
        public GUIStyle Heading { get; private set; }
        public GUIStyle Subheading { get; private set; }
        public GUIStyle Status { get; private set; }
        public GUIStyle Panel { get; private set; }
        public GUIStyle Small { get; private set; }
        public GUIStyle Error { get; private set; }

        public void Ensure()
        {
            if (Heading != null)
            {
                return;
            }

            Heading = new GUIStyle(GUI.skin.label)
            {
                fontSize = 20,
                fontStyle = FontStyle.Bold
            };

            Subheading = new GUIStyle(GUI.skin.label)
            {
                fontSize = 14,
                fontStyle = FontStyle.Bold
            };

            Status = new GUIStyle(GUI.skin.label)
            {
                wordWrap = true
            };

            Small = new GUIStyle(GUI.skin.label)
            {
                fontSize = 11,
                wordWrap = true
            };

            Error = new GUIStyle(Status);
            Error.normal.textColor = new Color(1f, 0.45f, 0.4f);

            Panel = new GUIStyle(GUI.skin.window)
            {
                padding = new RectOffset(12, 12, 12, 12)
            };
        }
    }
}
