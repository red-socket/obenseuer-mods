using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using TMPro;
using UnityEngine;

namespace BetterPlacement
{
    // Private game members and exact overloads, read through Harmony's AccessTools rather than the publicized
    // reference (docs/modding-guide.md §4, spec §3.1). A required member that is missing logs one error and stays null;
    // Plugin.PatchGroup checks the members each group needs, so only the feature using it fails. Every member here was
    // checked against the shipped 0.5.19 Assembly-CSharp.dll with Mono.Cecil (design.md §5).
    internal static class Vanilla
    {
        // ---- Patch targets ----

        // BuildingSystem.ShowPreview(RaycastHit) (SRC/BuildingSystem.cs:1192), public, IL 2690 bytes. T1, R1, A2, C3.
        internal static readonly MethodInfo ShowPreview = Method(typeof(BuildingSystem), "ShowPreview", new[] { typeof(RaycastHit) });
        // BuildingSystem.Place() (:1485), public. A3, M1.
        internal static readonly MethodInfo Place = Method(typeof(BuildingSystem), "Place", Type.EmptyTypes);
        // BuildingSystem.ChangeCurrentObject(FurnitureInfo, ItemStack, int, bool) (:778), public. M2.
        internal static readonly MethodInfo ChangeCurrentObject = Method(typeof(BuildingSystem), "ChangeCurrentObject",
            new[] { typeof(BuildingSystem.FurnitureInfo), typeof(ItemStack), typeof(int), typeof(bool) });
        // ObjectPreview.ChangeColor() (SRC/ObjectPreview.cs:126), public. A1.
        internal static readonly MethodInfo ChangeColor = Method(typeof(ObjectPreview), "ChangeColor", Type.EmptyTypes);
        // InsideTrigger.OnTriggerEnter/OnTriggerExit(Collider) (SRC/InsideTrigger.cs:27, :57), private. C1, C2.
        internal static readonly MethodInfo TriggerEnter = Method(typeof(InsideTrigger), "OnTriggerEnter", new[] { typeof(Collider) });
        internal static readonly MethodInfo TriggerExit = Method(typeof(InsideTrigger), "OnTriggerExit", new[] { typeof(Collider) });
        // BuildingSystemMenu.GetRotationMode() and GetButtonInfo() (SRC/BuildingSystemMenu.cs:898, :883), private. H1, H2.
        internal static readonly MethodInfo GetRotationMode = Method(typeof(BuildingSystemMenu), "GetRotationMode", Type.EmptyTypes);
        internal static readonly MethodInfo GetButtonInfo = Method(typeof(BuildingSystemMenu), "GetButtonInfo", Type.EmptyTypes);

        // ---- Calls ----

        // BuildingSystemMenu.WarningTextAnimation() (:871), public: vanilla's refusal shake and sound. A3 calls it directly.
        internal static readonly MethodInfo WarningTextAnimation = Method(typeof(BuildingSystemMenu), "WarningTextAnimation", Type.EmptyTypes);
        // BuildingSystem.UpdateConnectionIndicator() (:725), private. Optional: without it, connection lines are drawn from
        // the unsnapped spot. A4 patches it; the delegate calls the patched method, so A4 lets our own call through.
        internal static readonly MethodInfo UpdateConnectionIndicator =
            Method(typeof(BuildingSystem), "UpdateConnectionIndicator", Type.EmptyTypes, optional: true);
        internal static readonly Action<BuildingSystem> UpdateConnection =
            Delegate<Action<BuildingSystem>>(UpdateConnectionIndicator, "BuildingSystem.UpdateConnectionIndicator", optional: true);
        // GetButtonInfo as a delegate for Hint.Refresh (it runs through our H2 postfix like any other call).
        internal static readonly Func<BuildingSystemMenu, string> ButtonInfo =
            Delegate<Func<BuildingSystemMenu, string>>(GetButtonInfo, "BuildingSystemMenu.GetButtonInfo", optional: true);
        // KeyBindings.GetKey(string) (SRC/KeyBindings.cs:266), private. The public CheckKey switches the input type to
        // keyboard when the key is held (:257-264), so the collision check and the rotation-input test use this
        // side-effect-free one. Optional.
        internal static readonly Func<KeyBindings, string, KeyCode> BindingKey =
            Delegate<Func<KeyBindings, string, KeyCode>>(Method(typeof(KeyBindings), "GetKey", new[] { typeof(string) }, optional: true),
                "KeyBindings.GetKey", optional: true);

        // ---- Fields ----

        // BuildingSystem (SRC/BuildingSystem.cs:438-475), all private.
        internal static readonly AccessTools.FieldRef<BuildingSystem, Vector3> CurrentPos = Ref<BuildingSystem, Vector3>("currentpos");
        internal static readonly AccessTools.FieldRef<BuildingSystem, Vector3> CurrentRot = Ref<BuildingSystem, Vector3>("currentrot");
        internal static readonly AccessTools.FieldRef<BuildingSystem, RaycastHit> Hit = Ref<BuildingSystem, RaycastHit>("hit");
        internal static readonly AccessTools.FieldRef<BuildingSystem, bool> AttachPoint = Ref<BuildingSystem, bool>("attachpoint");
        internal static readonly AccessTools.FieldRef<BuildingSystem, float> RotationModeCooldown = Ref<BuildingSystem, float>("rotationModeCooldown");
        internal static readonly AccessTools.FieldRef<BuildingSystem, FurniturePlaceable> MovingPlacedItem = Ref<BuildingSystem, FurniturePlaceable>("movingPlacedItem");
        // BuildingSystemMenu.controlsInfoText (SRC/BuildingSystemMenu.cs:28), private.
        internal static readonly AccessTools.FieldRef<BuildingSystemMenu, TextMeshProUGUI> ControlsInfoText = Ref<BuildingSystemMenu, TextMeshProUGUI>("controlsInfoText");
        // ObjectPreview.type (SRC/ObjectPreview.cs:8), private serialized.
        internal static readonly AccessTools.FieldRef<ObjectPreview, PlaceType> PreviewType = Ref<ObjectPreview, PlaceType>("type");
        // InsideTrigger.collision (SRC/InsideTrigger.cs:10), private.
        internal static readonly AccessTools.FieldRef<InsideTrigger, List<Collider>> Collision = Ref<InsideTrigger, List<Collider>>("collision");

        // ---- Helpers ----

        internal static MethodInfo Method(Type t, string name, Type[] args, bool optional = false)
        {
            try
            {
                var m = AccessTools.DeclaredMethod(t, name, args);
                if (m == null) Missing($"{t.Name}.{name}", null, optional);
                return m;
            }
            catch (Exception e) { Missing($"{t.Name}.{name}", e, optional); return null; }
        }

        private static AccessTools.FieldRef<T, F> Ref<T, F>(string name, bool optional = false)
        {
            try { return AccessTools.FieldRefAccess<T, F>(name); }
            catch (Exception e) { Missing($"{typeof(T).Name}.{name}", e, optional); return null; }
        }

        private static D Delegate<D>(MethodInfo m, string what, bool optional) where D : Delegate
        {
            if (m == null) return null;
            try { return (D)System.Delegate.CreateDelegate(typeof(D), m); }
            catch (Exception e) { Missing(what, e, optional); return null; }
        }

        private static void Missing(string what, Exception e, bool optional)
        {
            string why = e != null ? $" ({e.Message})" : "";
            if (optional) Log.Warn($"{what} not found{why}; the part of the feature using it is off.");
            else Log.Error($"{what} not found{why}; the feature using it is off.");
        }

        // ---- Typed wrappers ----

        // The preview's ObjectPreview: on child(0) for furniture previews ("rotate") and item previews ("Rotate").
        internal static ObjectPreview PreviewOf(Transform preview) =>
            preview != null && preview.childCount > 0 ? preview.GetChild(0).GetComponent<ObjectPreview>() : null;

        internal static void Warn()
        {
            try { if (BuildingSystemMenu.instance != null) BuildingSystemMenu.instance.WarningTextAnimation(); }
            catch (Exception e) { Log.Exception("warning shake", e); }
        }
    }
}
