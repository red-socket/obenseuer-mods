using System;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace BetterPlacement
{
    internal enum Surface { None, Floor, Ceiling, Wall, Sloped }

    // Small read-only helpers over game state shared by several features. Unity objects are null-checked with != null.
    internal static class Game
    {
        internal static bool ConsoleOpen
        {
            get
            {
                try { var c = DeveloperConsole.Instance; return c != null && c.consoleOpened; }
                catch { return false; }
            }
        }

        // A text field has the keyboard: the game's writeLock (computers, notes, the console's own input), or any
        // focused input field (editable signs only call ControlsDisabled, SRC/EditableSign.cs:127).
        internal static bool Typing
        {
            get
            {
                try
                {
                    if (GameController.instance != null && GameController.instance.writeLock) return true;
                    var es = EventSystem.current;
                    var go = es != null ? es.currentSelectedGameObject : null;
                    if (go == null) return false;
                    var tmp = go.GetComponent<TMP_InputField>();
                    if (tmp != null && tmp.isFocused) return true;
                    var ui = go.GetComponent<InputField>();
                    return ui != null && ui.isFocused;
                }
                catch { return false; }
            }
        }

        // The building menu is open (BuildingSystem.Update only runs the preview while it is, SRC/BuildingSystem.cs:601-662).
        internal static bool BuildOpen
        {
            get
            {
                try
                {
                    var m = BuildingSystemMenu.instance;
                    return m != null && m.menuIsOpen && BuildingSystem.instance != null;
                }
                catch { return false; }
            }
        }

        // The game's own surface classes with its own thresholds (SRC/BuildingSystem.cs:1391-1405): within 5° of up is a
        // floor, within 5° of down a ceiling, more than 85° from both a wall, anything else sloped.
        internal static Surface SurfaceOf(Vector3 n)
        {
            if (n == Vector3.zero) return Surface.None;
            if (Vector3.Angle(n, Vector3.up) < 5f) return Surface.Floor;
            float down = Vector3.Angle(n, Vector3.down);
            if (down < 5f) return Surface.Ceiling;
            if (down > 85f && Vector3.Angle(n, Vector3.up) > 85f) return Surface.Wall;
            return Surface.Sloped;
        }

        // The most an axis-aligned box reaches along a direction.
        internal static float MaxAlong(Bounds b, Vector3 n) =>
            Vector3.Dot(b.center, n) + Mathf.Abs(n.x) * b.extents.x + Mathf.Abs(n.y) * b.extents.y + Mathf.Abs(n.z) * b.extents.z;

        internal static float MinAlong(Bounds b, Vector3 n) =>
            Vector3.Dot(b.center, n) - Mathf.Abs(n.x) * b.extents.x - Mathf.Abs(n.y) * b.extents.y - Mathf.Abs(n.z) * b.extents.z;

        // "Root/child/collider" for Verbose lines.
        internal static string Path(Component c)
        {
            if (c == null) return "(none)";
            try
            {
                var sb = new StringBuilder();
                var t = c.transform;
                int depth = 0;
                while (t != null && depth < 8)
                {
                    sb.Insert(0, depth == 0 ? t.name : t.name + "/");
                    t = t.parent;
                    depth++;
                }
                return $"{sb} (layer {c.gameObject.layer})";
            }
            catch (Exception) { return "?"; }
        }

        // A placed piece's name for Verbose lines.
        internal static string Title(FurniturePlaceable fp)
        {
            if (fp == null) return "?";
            try
            {
                if (fp.furniture != null && !string.IsNullOrEmpty(fp.furniture.title)) return fp.furniture.title;
                return fp.name;
            }
            catch { return "?"; }
        }
    }
}
