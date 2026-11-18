using UnityEngine;

namespace MessageLog
{
    // IMGUI styles, copied from Fair Market (Quest Helper's; Save Guard's house palette: charcoal, panel grey, text greys
    // and one amber accent; a focused row and field for controller and keyboard navigation), plus filter chips (Tab/TabOn)
    // and the category colours (spec §5.2). Built lazily inside OnGUI, where GUI.skin is available.
    internal static class Skin
    {
        internal static readonly UnityEngine.Color Panel = Hex(0x1f1f1f, 0.96f);
        internal static readonly UnityEngine.Color RowBg = Hex(0x2b2b2b, 1f);
        internal static readonly UnityEngine.Color RowHover = Hex(0x3a3a3a, 1f);
        internal static readonly UnityEngine.Color RowSelected = Hex(0x835213, 1f);
        internal static readonly UnityEngine.Color Text = Hex(0xcacaca, 1f);
        internal static readonly UnityEngine.Color Text2 = Hex(0xbcbab1, 1f);
        internal static readonly UnityEngine.Color DimColor = Hex(0x8a8a88, 1f);
        internal static readonly UnityEngine.Color Amber = Hex(0xffab00, 1f);

        // Category colours. The category is always printed too, so colour is never the only cue.
        internal static readonly UnityEngine.Color CrimeColor = Hex(0xe06666, 1f);
        internal static readonly UnityEngine.Color MoneyColor = Hex(0xffab00, 1f);
        internal static readonly UnityEngine.Color MailColor = Hex(0x6fa8dc, 1f);
        internal static readonly UnityEngine.Color QuestsColor = Hex(0x93c47d, 1f);
        internal static readonly UnityEngine.Color ItemsColor = Hex(0xd9d9d9, 1f);
        internal static readonly UnityEngine.Color DialogueColor = Hex(0xc3a6e0, 1f);
        internal static readonly UnityEngine.Color OtherColor = Hex(0x9e9e9e, 1f);

        private static UnityEngine.Color Hex(int rgb, float a) => new UnityEngine.Color(((rgb >> 16) & 0xff) / 255f, ((rgb >> 8) & 0xff) / 255f, (rgb & 0xff) / 255f, a);

        internal static GUIStyle Box, Title, Label, Small, Dim, Button, Tab, TabOn, Row, RowFocused, Field, FieldFocused;
        private static bool _built;

        // Scales GUI.matrix to a 1080-high virtual screen times UiScale; returns the virtual screen size.
        internal static Vector2 Begin()
        {
            Build();
            float scale = Cfg.Window.UiScale != null ? Mathf.Clamp(Cfg.Window.UiScale.Value, 0.75f, 2f) : 1f;
            float s = Screen.height / 1080f * scale;
            if (s <= 0.01f) s = 1f;
            GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(s, s, 1f));
            GUI.depth = -1000;
            return new Vector2(Screen.width / s, Screen.height / s);
        }

        internal static UnityEngine.Color Color(Cat c)
        {
            if ((c & Cat.Crime) != 0) return CrimeColor;
            if ((c & Cat.Money) != 0) return MoneyColor;
            if ((c & Cat.Mail) != 0) return MailColor;
            if ((c & Cat.Quests) != 0) return QuestsColor;
            if ((c & Cat.Items) != 0) return ItemsColor;
            if ((c & Cat.Dialogue) != 0) return DialogueColor;
            return OtherColor;
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
            Label = new GUIStyle(GUI.skin.label) { fontSize = 16, wordWrap = true, richText = true };
            Label.normal.textColor = Text;
            Small = new GUIStyle(Label) { fontSize = 14 };
            Small.normal.textColor = Text2;
            Dim = new GUIStyle(Label) { fontSize = 15 };
            Dim.normal.textColor = DimColor;
            Button = new GUIStyle(GUI.skin.button) { fontSize = 16, padding = new RectOffset(12, 12, 6, 6), richText = true };
            Button.normal.textColor = Text;
            Button.hover.textColor = Amber;
            Tab = new GUIStyle(Button) { fontSize = 14, padding = new RectOffset(10, 10, 5, 5), margin = new RectOffset(0, 6, 2, 2) };
            Tab.normal.background = Tex(RowBg);
            Tab.hover.background = Tex(RowHover);
            Tab.normal.textColor = Text2;
            TabOn = new GUIStyle(Tab);
            TabOn.normal.background = Tex(RowSelected);
            TabOn.hover.background = Tex(RowSelected);
            TabOn.normal.textColor = UnityEngine.Color.white;
            TabOn.hover.textColor = UnityEngine.Color.white;
            Row = new GUIStyle(GUI.skin.label) { fontSize = 16, padding = new RectOffset(0, 0, 0, 0), margin = new RectOffset(0, 0, 0, 0) };
            Row.normal.background = Tex(RowBg);
            Row.hover.background = Tex(RowHover);
            RowFocused = new GUIStyle(Row);
            RowFocused.normal.background = Tex(RowSelected);
            RowFocused.hover.background = Tex(RowSelected);
            Field = new GUIStyle(GUI.skin.textField) { fontSize = 16, padding = new RectOffset(8, 8, 5, 5) };
            Field.normal.textColor = Text;
            Field.focused.textColor = UnityEngine.Color.white;
            FieldFocused = new GUIStyle(Field);
            FieldFocused.normal.background = Tex(RowSelected);
        }

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
