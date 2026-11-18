using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace GameHarness
{
    // Virtual key presses, OR-ed into Unity's legacy Input queries. That covers the game (InputManager reads keys
    // through Input.GetKey*), the console's F1 check and mods' BepInEx KeyboardShortcut hotkeys. A press becomes
    // GetKeyDown on the next frame, GetKey until it is released, and GetKeyUp on the frame after the release.
    internal static class VirtualInput
    {
        private sealed class KeyState
        {
            internal int DownFrame = -1;
            internal int UpFrame = -1;
            internal bool Held;
            internal float ReleaseAt;
        }

        private static readonly Dictionary<KeyCode, KeyState> Keys = new Dictionary<KeyCode, KeyState>();

        // Fast path for the patches: false whenever no virtual key is pressed or about to report its release.
        internal static bool Any;

        internal static void Press(KeyCode key, float seconds)
        {
            if (!Keys.TryGetValue(key, out var s)) Keys[key] = s = new KeyState();
            s.DownFrame = Time.frameCount + 1;
            s.UpFrame = -1;
            s.Held = true;
            s.ReleaseAt = Time.unscaledTime + Mathf.Max(0f, seconds);
            Any = true;
        }

        internal static void ReleaseAll()
        {
            foreach (var s in Keys.Values)
            {
                if (!s.Held) continue;
                s.Held = false;
                s.UpFrame = Time.frameCount + 1;
            }
        }

        internal static List<string> HeldKeys()
        {
            var l = new List<string>();
            foreach (var kv in Keys) if (kv.Value.Held) l.Add(kv.Key.ToString());
            return l;
        }

        internal static void Tick()
        {
            if (!Any) return;
            int frame = Time.frameCount;
            bool any = false;
            foreach (var s in Keys.Values)
            {
                if (s.Held && frame > s.DownFrame && Time.unscaledTime >= s.ReleaseAt)
                {
                    s.Held = false;
                    s.UpFrame = frame + 1;
                }
                if (s.Held || s.UpFrame >= frame) any = true;
            }
            Any = any;
        }

        internal static bool AnyDown()
        {
            foreach (var s in Keys.Values) if (s.DownFrame == Time.frameCount) return true;
            return false;
        }

        internal static bool AnyHeld()
        {
            foreach (var s in Keys.Values) if (s.Held && Time.frameCount >= s.DownFrame) return true;
            return false;
        }

        internal static bool IsDown(KeyCode k) => Keys.TryGetValue(k, out var s) && s.DownFrame == Time.frameCount;
        internal static bool IsHeld(KeyCode k) => Keys.TryGetValue(k, out var s) && s.Held && Time.frameCount >= s.DownFrame;
        internal static bool IsUp(KeyCode k) => Keys.TryGetValue(k, out var s) && s.UpFrame == Time.frameCount;
    }

    [HarmonyPatch(typeof(Input), nameof(Input.GetKeyDown), typeof(KeyCode))]
    internal static class InputGetKeyDownPatch
    {
        private static void Postfix(KeyCode key, ref bool __result)
        {
            if (!__result && VirtualInput.Any) __result = VirtualInput.IsDown(key);
        }
    }

    [HarmonyPatch(typeof(Input), nameof(Input.GetKey), typeof(KeyCode))]
    internal static class InputGetKeyPatch
    {
        private static void Postfix(KeyCode key, ref bool __result)
        {
            if (!__result && VirtualInput.Any) __result = VirtualInput.IsHeld(key);
        }
    }

    [HarmonyPatch(typeof(Input), nameof(Input.GetKeyUp), typeof(KeyCode))]
    internal static class InputGetKeyUpPatch
    {
        private static void Postfix(KeyCode key, ref bool __result)
        {
            if (!__result && VirtualInput.Any) __result = VirtualInput.IsUp(key);
        }
    }

    // The title screen's "press any key" (PressAnyKey.Update) polls Input.anyKey.
    [HarmonyPatch(typeof(Input), nameof(Input.anyKey), MethodType.Getter)]
    internal static class InputAnyKeyPatch
    {
        private static void Postfix(ref bool __result)
        {
            if (!__result && VirtualInput.Any) __result = VirtualInput.AnyHeld();
        }
    }

    [HarmonyPatch(typeof(Input), nameof(Input.anyKeyDown), MethodType.Getter)]
    internal static class InputAnyKeyDownPatch
    {
        private static void Postfix(ref bool __result)
        {
            if (!__result && VirtualInput.Any) __result = VirtualInput.AnyDown();
        }
    }

    [HarmonyPatch(typeof(Input), nameof(Input.GetMouseButtonDown))]
    internal static class InputGetMouseButtonDownPatch
    {
        private static void Postfix(int button, ref bool __result)
        {
            if (!__result && VirtualInput.Any) __result = VirtualInput.IsDown(KeyCode.Mouse0 + button);
        }
    }

    [HarmonyPatch(typeof(Input), nameof(Input.GetMouseButton))]
    internal static class InputGetMouseButtonPatch
    {
        private static void Postfix(int button, ref bool __result)
        {
            if (!__result && VirtualInput.Any) __result = VirtualInput.IsHeld(KeyCode.Mouse0 + button);
        }
    }

    [HarmonyPatch(typeof(Input), nameof(Input.GetMouseButtonUp))]
    internal static class InputGetMouseButtonUpPatch
    {
        private static void Postfix(int button, ref bool __result)
        {
            if (!__result && VirtualInput.Any) __result = VirtualInput.IsUp(KeyCode.Mouse0 + button);
        }
    }

    // Second layer in case the JIT inlined the small Input wrappers into InputManager before the patches applied.
    [HarmonyPatch(typeof(InputManager), "IsKeyDown")]
    internal static class InputManagerIsKeyDownPatch
    {
        private static void Postfix(KeyCode key, ref bool __result)
        {
            if (!__result && VirtualInput.Any) __result = VirtualInput.IsDown(key);
        }
    }

    [HarmonyPatch(typeof(InputManager), "IsKey")]
    internal static class InputManagerIsKeyPatch
    {
        private static void Postfix(KeyCode key, ref bool __result)
        {
            if (!__result && VirtualInput.Any) __result = VirtualInput.IsHeld(key);
        }
    }

    [HarmonyPatch(typeof(InputManager), "IsKeyUp")]
    internal static class InputManagerIsKeyUpPatch
    {
        private static void Postfix(KeyCode key, ref bool __result)
        {
            if (!__result && VirtualInput.Any) __result = VirtualInput.IsUp(key);
        }
    }
}
