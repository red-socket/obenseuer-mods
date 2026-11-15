using UnityEngine;

namespace BetterTenement
{
    // IMGUI styles, copied from Save Guard: dark translucent boxes, off-white text, amber accent (art/NOTES.md).
    // Built lazily inside OnGUI, where GUI.skin is available.
    internal static class Skin
    {
        // red-socket house palette (art/STYLE.md): charcoal, panel grey, text greys and one amber accent.
        // No red text on charcoal (too little contrast): warnings use amber.
        internal static readonly Color Panel = Hex(0x1f1f1f, 0.96f);
        internal static readonly Color Row = Hex(0x2b2b2b, 1f);
        internal static readonly Color RowHover = Hex(0x3a3a3a, 1f);
        internal static readonly Color RowSelected = Hex(0x835213, 1f);
        internal static readonly Color Text = Hex(0xcacaca, 1f);
        internal static readonly Color Dim = Hex(0x8a8a88, 1f);
        internal static readonly Color Amber = Hex(0xffab00, 1f);

        private static Color Hex(int rgb, float a) => new Color(((rgb >> 16) & 0xff) / 255f, ((rgb >> 8) & 0xff) / 255f, (rgb & 0xff) / 255f, a);

        internal static GUIStyle Box, Title, TitleRich, Label, Small, Bold, Button, RowStyle, RowSelectedStyle, Toggle;
        private static bool _built;

        // Scales GUI.matrix to a 1080-high virtual screen times UI.Scale; returns the virtual screen size.
        internal static Vector2 Begin()
        {
            Build();
            float s = Screen.height / 1080f * Cfg.UiScale.Value;
            if (s <= 0.01f) s = 1f;
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
            Title = new GUIStyle(GUI.skin.label) { fontSize = 22, fontStyle = FontStyle.Bold };
            Title.normal.textColor = Amber;
            TitleRich = new GUIStyle(Title) { richText = true };
            Label = new GUIStyle(GUI.skin.label) { fontSize = 16, wordWrap = true, richText = true };
            Label.normal.textColor = Text;
            Small = new GUIStyle(Label) { fontSize = 14 };
            Small.normal.textColor = Dim;
            Bold = new GUIStyle(Label) { fontStyle = FontStyle.Bold, fontSize = 18 };
            Button = new GUIStyle(GUI.skin.button) { fontSize = 16, padding = new RectOffset(10, 10, 5, 5) };
            Button.normal.textColor = Text;
            Button.hover.textColor = Amber;
            RowStyle = new GUIStyle(GUI.skin.label) { fontSize = 15, padding = new RectOffset(8, 8, 4, 4), richText = true, wordWrap = false };
            RowStyle.normal.background = Tex(Row);
            RowStyle.normal.textColor = Text;
            RowStyle.hover.background = Tex(RowHover);
            RowStyle.hover.textColor = Text;
            RowSelectedStyle = new GUIStyle(RowStyle);
            RowSelectedStyle.normal.background = Tex(RowSelected);
            RowSelectedStyle.hover.background = Tex(RowSelected);
            Toggle = new GUIStyle(GUI.skin.toggle) { fontSize = 15 };
            Toggle.normal.textColor = Text;
            Toggle.onNormal.textColor = Text;
        }

        internal static string Color(string text, Color c) => $"<color=#{ColorUtility.ToHtmlStringRGB(c)}>{text}</color>";

        private static Texture2D Tex(Color c)
        {
            var t = new Texture2D(1, 1, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave };
            t.SetPixel(0, 0, c);
            t.Apply();
            return t;
        }
    }
}
