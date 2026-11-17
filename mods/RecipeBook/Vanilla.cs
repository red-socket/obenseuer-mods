using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using OS.Items;
using TMPro;
using UnityEngine;
using Window = InventoryNavigationHandler.CurrentInventoryWindow;

namespace RecipeBook
{
    // Private game members and exact overloads, read through Harmony's AccessTools rather than the publicized
    // reference (docs/modding-guide.md §4, spec §5.1). A required member that is missing logs one error and stays null;
    // Plugin.PatchGroup checks the members each group needs, so only the feature using it fails (spec §4).
    // Every patch target was checked against SRC and the shipped Assembly-CSharp.dll with Mono.Cecil (design.md §21).
    internal static class Vanilla
    {
        // ---- List (P1) ----

        // P1 SRC/RecipeListUI.cs:29, 447 B. recipeButtons :27 and recipeButtonPrefab :14 (private).
        internal static readonly MethodInfo UpdateRecipes = Method(typeof(RecipeListUI), "UpdateRecipes",
            new[] { typeof(List<Recipe>), typeof(List<Recipe>), typeof(List<Recipe>), typeof(bool), typeof(CraftingBase), typeof(float) });
        internal static readonly AccessTools.FieldRef<RecipeListUI, List<GameObject>> RecipeButtons = Ref<RecipeListUI, List<GameObject>>("recipeButtons");
        internal static readonly AccessTools.FieldRef<RecipeListUI, GameObject> RecipeButtonPrefab = Ref<RecipeListUI, GameObject>("recipeButtonPrefab");
        // DefaultUIButton.hoverImage (SRC/DefaultUIButton.cs:16), protected. Optional: the chip highlight is a tint without it.
        internal static readonly AccessTools.FieldRef<DefaultUIButton, Sprite> HoverImage = Ref<DefaultUIButton, Sprite>("hoverImage", optional: true);

        // ---- Typing guard (P2-P6) ----

        // SRC/InputManager.cs:202-306 (public). GetKeyUp is not guarded (spec §5.2).
        internal static readonly MethodInfo GetKeyDown = Method(typeof(InputManager), "GetKeyDown", new[] { typeof(string) });
        internal static readonly MethodInfo GetKey = Method(typeof(InputManager), "GetKey", new[] { typeof(string) });
        internal static readonly MethodInfo GetKeyWithHold = Method(typeof(InputManager), "GetKeyWithHold", new[] { typeof(string), typeof(int) });
        internal static readonly MethodInfo GetKeyDownKeyboardOnly = Method(typeof(InputManager), "GetKeyDownKeyboardOnly", new[] { typeof(string) });
        internal static readonly MethodInfo GetAuxiliaryReloadDown = Method(typeof(InputManager), "GetAuxiliaryReloadDown", Type.EmptyTypes);
        // GameController.disabledWritelockList (SRC/GameController.cs:25), private.
        internal static readonly AccessTools.FieldRef<GameController, List<GameObject>> DisabledWritelockList =
            Ref<GameController, List<GameObject>>("disabledWritelockList");

        // ---- Controller route and hint (P7-P9) ----

        // P7 SRC/InventoryNavigationMovement.cs:428, 3189 B. SwitchToNextRecipe :1324 and ClearPreviousSelections :1372
        // (both private); GetActiveRecipePanel :1038 (public static).
        internal static readonly MethodInfo NavMove = Method(typeof(InventoryNavigationMovement), "Move",
            new[] { typeof(MovementDirection), typeof(InventoryNavigationData), typeof(InputManager.InputType) });
        internal static readonly MethodInfo SwitchToNextRecipe = Method(typeof(InventoryNavigationMovement), "SwitchToNextRecipe",
            new[] { typeof(InventoryNavigationData), typeof(Vector2), typeof(RecipeButton[,]) });
        internal static readonly MethodInfo ClearPreviousSelections = Method(typeof(InventoryNavigationMovement), "ClearPreviousSelections",
            new[] { typeof(InventoryNavigationData) });
        // P8 SRC/InventoryNavigationHandler.cs:1786, private, 229 B.
        internal static readonly MethodInfo HandleRecipeInteraction = Method(typeof(InventoryNavigationHandler), "HandleRecipeInteraction", Type.EmptyTypes);
        // P9 SRC/InventoryNavigationHandler.cs:424, private, 592 B. KeyCodeInfoToString :464 (public static).
        internal static readonly MethodInfo GetButtonInfoTab0 = Method(typeof(InventoryNavigationHandler), "GetButtonInfoTab0",
            new[] { typeof(Window), typeof(InputManager.InputType) });
        internal static readonly MethodInfo KeyCodeInfoToString = Method(typeof(InventoryNavigationHandler), "KeyCodeInfoToString",
            new[] { typeof(string), typeof(string), typeof(string), typeof(string) });

        // ---- Tooltip and item panel (P10-P12) ----

        // P10 SRC/ToolTip.cs:163, 1857 B. P11 SRC/TooltipUI.cs:136, 597 B. P12 SRC/ItemInfoPanel.cs:182, 1255 B;
        // itemDescripion :38 (private).
        internal static readonly MethodInfo ConstructDataString = Method(typeof(ToolTip), "ConstructDataString",
            new[] { typeof(ItemData), typeof(int), typeof(int), typeof(object[]) });
        internal static readonly MethodInfo UpdateTooltip = Method(typeof(TooltipUI), "UpdateTooltip",
            new[] { typeof(string), typeof(string), typeof(Dictionary<ItemReference, float>), typeof(List<Furniture.ReseourceItem>) });
        internal static readonly MethodInfo UpdateInfo = Method(typeof(ItemInfoPanel), "UpdateInfo", new[] { typeof(ItemData), typeof(ChildItem) });
        internal static readonly AccessTools.FieldRef<ItemInfoPanel, TextMeshProUGUI> ItemDescription = Ref<ItemInfoPanel, TextMeshProUGUI>("itemDescripion");

        // ---- Discovery (P13-P15) ----

        // P13 SRC/OS/Items/ItemReadable.cs:265, private, one `Random.value < 0.8f` at IL_0138 (spec §5.2).
        internal static readonly MethodInfo GiveRecipes = Method(typeof(ItemReadable), "GiveRecipes", Type.EmptyTypes);
        // P14 SRC/CollectibleRecipeBook.cs:90, 214 B. P15 :34, override, 18 B. UpdateTitle :71 (private); recipesAdded
        // :29 and recipeIds :32 (protected).
        internal static readonly MethodInfo SetRandomRecipes = Method(typeof(CollectibleRecipeBook), "SetRandomRecipes", Type.EmptyTypes);
        internal static readonly MethodInfo BookOnLoadingGame = Method(typeof(CollectibleRecipeBook), "OnLoadingGame", Type.EmptyTypes);
        internal static readonly Action<CollectibleRecipeBook> BookUpdateTitle = Delegate<Action<CollectibleRecipeBook>>(typeof(CollectibleRecipeBook), "UpdateTitle", Type.EmptyTypes);
        internal static readonly AccessTools.FieldRef<CollectibleRecipeBook, List<int>> BookRecipeIds = Ref<CollectibleRecipeBook, List<int>>("recipeIds");
        internal static readonly AccessTools.FieldRef<CollectibleRecipeBook, bool> BookRecipesAdded = Ref<CollectibleRecipeBook, bool>("recipesAdded");

        // ---- Calls into private navigation helpers ----

        internal static void Clear(InventoryNavigationMovement mover, InventoryNavigationData data) =>
            ClearPreviousSelections.Invoke(mover, new object[] { data });

        internal static void SwitchTo(InventoryNavigationMovement mover, InventoryNavigationData data, Vector2 at, RecipeButton[,] grid) =>
            SwitchToNextRecipe.Invoke(mover, new object[] { data, at, grid });

        internal static string KeyHint(string current, string text, string key, string key2 = "") =>
            (string)KeyCodeInfoToString.Invoke(null, new object[] { current, text, key, key2 });

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

        private static D Delegate<D>(Type t, string name, Type[] args) where D : Delegate
        {
            var m = Method(t, name, args);
            if (m == null) return null;
            try { return AccessTools.MethodDelegate<D>(m); }
            catch (Exception e) { Missing($"{t.Name}.{name}", e, false); return null; }
        }

        private static void Missing(string what, Exception e, bool optional)
        {
            string why = e != null ? $" ({e.Message})" : "";
            if (optional) Log.Warn($"{what} not found{why}; the part of the feature using it is off.");
            else Log.Error($"{what} not found{why}; the feature using it is off.");
        }
    }
}
