using UnityEngine;

namespace VitalSigns
{
    // IMGUI styles, copied from Quest Helper (Save Guard's house palette: charcoal, panel grey, text greys and one amber accent; no red
    // text on charcoal). Quest Helper added the focused style and the tab row. Built lazily inside
    // OnGUI, where GUI.skin is available.
    internal static class Skin
    {
        internal static readonly UnityEngine.Color Panel = Hex(0x1f1f1f, 0.96f);
        internal static readonly UnityEngine.Color Row = Hex(0x2b2b2b, 1f);
        internal static readonly UnityEngine.Color RowHover = Hex(0x3a3a3a, 1f);
        internal static readonly UnityEngine.Color RowSelected = Hex(0x835213, 1f);
        internal static readonly UnityEngine.Color Text = Hex(0xcacaca, 1f);
        internal static readonly UnityEngine.Color Text2 = Hex(0xbcbab1, 1f);
        internal static readonly UnityEngine.Color Dim = Hex(0x8a8a88, 1f);
        internal static readonly UnityEngine.Color Amber = Hex(0xffab00, 1f);

        private static UnityEngine.Color Hex(int rgb, float a) => new UnityEngine.Color(((rgb >> 16) & 0xff) / 255f, ((rgb >> 8) & 0xff) / 255f, (rgb & 0xff) / 255f, a);

        internal static GUIStyle Box, Title, Heading, Label, Small, DimLabel, Warn, Button, RowStyle,
            Tab, TabOn, ToastBox, Banner;
        private static bool _built;

        // Our UiScale; ToastStack compares it with Save Guard's and Quest Helper's.
        internal static float Scale => Cfg.UiScale != null ? Cfg.UiScale.Value : 1f;

        // Pixels per virtual unit for a given UiScale.
        internal static float PixelScale(float uiScale)
        {
            float s = Screen.height / 1080f * uiScale;
            return s <= 0.01f ? 1f : s;
        }

        // Scales GUI.matrix to a 1080-high virtual screen times UiScale; returns the virtual screen size.
        internal static Vector2 Begin()
        {
            Build();
            float s = PixelScale(Scale);
            GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(s, s, 1f));
            GUI.depth = -1000;
            return new Vector2(Screen.width / s, Screen.height / s);
        }

        private static void Build()
        {
            if (_built && Box != null && Box.normal.background != null) return;
            _built = true;
            Box = new GUIStyle(GUI.skin.box) { padding = new RectOffset(16, 16, 12, 12), alignment = TextAnchor.UpperLeft, wordWrap = true, fontSize = 16 };
            Box.normal.background = Tex(Panel);
            Box.normal.textColor = Text;
            ToastBox = new GUIStyle(Box) { padding = new RectOffset(14, 14, 10, 10), fontSize = 17, richText = true };
            Title = new GUIStyle(GUI.skin.label) { fontSize = 22, fontStyle = FontStyle.Bold };
            Title.normal.textColor = Amber;
            Heading = new GUIStyle(GUI.skin.label) { fontSize = 18, fontStyle = FontStyle.Bold, wordWrap = true, richText = true, margin = new RectOffset(4, 4, 10, 2) };
            Heading.normal.textColor = Text2;
            Label = new GUIStyle(GUI.skin.label) { fontSize = 16, wordWrap = true, richText = true };
            Label.normal.textColor = Text;
            Small = new GUIStyle(Label) { fontSize = 14 };
            Small.normal.textColor = Text2;
            DimLabel = new GUIStyle(Label) { fontSize = 15 };
            DimLabel.normal.textColor = Dim;
            Warn = new GUIStyle(Label);
            Warn.normal.textColor = Amber;
            Banner = new GUIStyle(Label) { fontStyle = FontStyle.Bold, padding = new RectOffset(10, 10, 6, 6) };
            Banner.normal.background = Tex(RowSelected);
            Banner.normal.textColor = UnityEngine.Color.white;
            Button = new GUIStyle(GUI.skin.button) { fontSize = 16, padding = new RectOffset(12, 12, 6, 6), richText = true };
            Button.normal.textColor = Text;
            Button.hover.textColor = Amber;
            RowStyle = new GUIStyle(GUI.skin.label) { fontSize = 16, padding = new RectOffset(10, 10, 5, 5), richText = true, wordWrap = true };
            RowStyle.normal.background = Tex(Row);
            RowStyle.normal.textColor = Text;
            RowStyle.hover.background = Tex(RowHover);
            RowStyle.hover.textColor = Text;
            Tab = new GUIStyle(Button) { fontSize = 16 };
            TabOn = new GUIStyle(Tab);
            TabOn.normal.background = Tex(RowSelected);
            TabOn.hover.background = Tex(RowSelected);
            TabOn.normal.textColor = UnityEngine.Color.white;
            TabOn.hover.textColor = UnityEngine.Color.white;
        }

        internal static string Color(string text, UnityEngine.Color c) => $"<color=#{ColorUtility.ToHtmlStringRGB(c)}>{text}</color>";

        // IMGUI rich text has no <noparse>: angle brackets in game text are swapped for look-alikes.
        internal static string Esc(string s) => string.IsNullOrEmpty(s) ? "" : s.Replace("<", "‹").Replace(">", "›");

        private static Texture2D Tex(UnityEngine.Color c)
        {
            var t = new Texture2D(1, 1, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave };
            t.SetPixel(0, 0, c);
            t.Apply();
            return t;
        }
    }
}
