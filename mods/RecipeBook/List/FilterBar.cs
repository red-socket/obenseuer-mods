using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace RecipeBook
{
    // The search bar injected above a station's recipe list (spec §6.1, §6.2). Built lazily by P1 as the last child of
    // the RecipeListUI GameObject ("Recipes list"), from scratch: no Relay, SavableScript, GridLayoutGroup or RecipeButton
    // anywhere in it, so Initialize*RecipeList still finds the list's own grid. It reuses the recipe button's sprite and
    // font. The root stays active with its list; "Shown" toggles its children and moves the list's scroll view down.
    // Filter state lives here, in RAM only, and is cleared when the station panel is disabled (D5).
    internal sealed class FilterBar : MonoBehaviour
    {
        internal enum PanelKind { Manufacturing, Process, Growing }

        internal const string BarName = "RecipeBook Bar";
        private const string EmptyName = "RecipeBook Empty";
        private const float Inset = 20f, Height = 40f, Top = 4f, Gap = 6f, FieldShare = 0.58f, Shift = 48f;

        private static readonly List<FilterBar> All = new List<FilterBar>();

        internal RecipeListUI List;
        internal PanelKind Kind;
        internal TMP_InputField Field;
        private GameObject _fieldGo, _chipGo, _highlight, _empty;
        private TextMeshProUGUI _chipLabel;
        private RectTransform _scroll;
        private ScrollRect _scrollRect;
        private Vector2 _scrollOffsetMax;
        private bool _shifted;

        // ---- Filter state (spec §6.3) ----
        internal string Text = "";
        internal string[] Tokens = new string[0];
        internal Filter.ChipValue Chip = Filter.ChipValue.All;
        internal bool Pending, Shown, HidAny;
        internal int Sticky = -1, Count, VisibleCount;
        internal bool[] Visible = new bool[0];
        internal RecipeButton[] Rbs = new RecipeButton[0];
        internal GameObject[] Buttons = new GameObject[0];

        private bool _focusNext, _enableCheck, _wasDisabled;
        private int _focusFrame;

        // The field exists when the key guard patched at launch; TextField can still hide it live.
        internal bool HasField => Field != null && Cfg.Hot.TextField;
        internal bool HasChip => _chipGo != null && Cfg.Hot.Chip;

        // The bar of the station panel on screen: only one shows at a time.
        internal static FilterBar Active
        {
            get
            {
                for (int i = 0; i < All.Count; i++)
                {
                    var b = All[i];
                    if (b != null && b.isActiveAndEnabled) return b;
                }
                return null;
            }
        }

        // ---- P1 entry (spec §5.2) ----

        internal static void OnRebuilt(RecipeListUI list)
        {
            var t = list.transform.Find(BarName);
            var bar = t != null ? t.GetComponent<FilterBar>() : null;
            if (!Cfg.Hot.SearchOn)
            {
                if (bar != null) bar.SetShown(false);
                return;
            }
            if (bar == null) bar = Build(list);
            if (bar == null) return;
            bar.SetShown(list.currentRecipes.Count > 0);
            Filter.Apply(bar, live: false);
            Filter.ClampAfterRebuild(bar);
        }

        // Settings changed (spec §4): filter the open list again, without a game rebuild.
        internal void Reapply()
        {
            if (List == null) return;
            if (!Cfg.Hot.SearchOn)
            {
                SetShown(false);
                Filter.ShowAll(List);
                HidAny = false;
                if (InventoryNavigationHandler.Instance != null) Filter.InitList(InventoryNavigationHandler.Instance, Kind);
                return;
            }
            // TextField turned off live: the hidden field's text no longer filters.
            if (!Cfg.Hot.TextField && Text.Length > 0)
            {
                if (Field != null) Field.SetTextWithoutNotify("");
                Text = "";
                Tokens = new string[0];
            }
            SetShown(List.currentRecipes.Count > 0);
            Filter.Apply(this, live: true);
        }

        // ---- Build (spec §6.1) ----

        private static FilterBar Build(RecipeListUI list)
        {
            var prefab = Vanilla.RecipeButtonPrefab(list);
            var prefabLabel = prefab != null ? prefab.GetComponentInChildren<TextMeshProUGUI>(true) : null;
            TextMeshProUGUI liveLabel = null;
            var buttons = Vanilla.RecipeButtons(list);
            if (buttons != null)
                foreach (var b in buttons)
                    if (b != null && (liveLabel = b.GetComponentInChildren<TextMeshProUGUI>(true)) != null) break;
            var styleLabel = prefabLabel != null ? prefabLabel : liveLabel;
            TMP_FontAsset font = styleLabel != null && styleLabel.font != null ? styleLabel.font : TMP_Settings.defaultFontAsset;
            Material mat = styleLabel != null ? styleLabel.fontSharedMaterial : null;
            Color textColor = styleLabel != null ? styleLabel.color : Color.white;
            var prefabImage = prefab != null ? prefab.GetComponent<Image>() : null;
            Sprite hover = null;
            if (prefab != null && Vanilla.HoverImage != null)
            {
                var dub = prefab.GetComponent<DefaultUIButton>();
                if (dub != null) hover = Vanilla.HoverImage(dub);
            }

            var root = new GameObject(BarName, typeof(RectTransform));
            root.SetActive(false);
            var rt = (RectTransform)root.transform;
            rt.SetParent(list.transform, false);
            rt.SetAsLastSibling();
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.offsetMin = new Vector2(Inset, -Top - Height);
            rt.offsetMax = new Vector2(-Inset, -Top);
            root.AddComponent<LayoutElement>().ignoreLayout = true;

            var bar = root.AddComponent<FilterBar>();
            bar.List = list;
            bar.Kind = KindOf(list);

            // Built whenever the key guard patched (TextField was on at launch); Layout shows it while TextField is on.
            if (TypingGuard.Available)
                bar.BuildField(rt, font, mat, textColor, prefabImage);
            bar.BuildChip(rt, font, mat, textColor, prefabImage, hover);
            bar.BuildEmpty(list, font, mat, textColor);
            bar.FindScroll(list);
            bar.Layout();
            root.SetActive(true);
            Log.Verbose($"Filter: bar built on {list.name} ({bar.Kind}, text field {(bar.HasField ? "on" : "off")}).");
            return bar;
        }

        // The three CraftingPanelBaseUI subclasses, in this order (spec §6.2). Picks the Initialize*RecipeList to call.
        private static PanelKind KindOf(RecipeListUI list)
        {
            if (list.GetComponentInParent<ManufacturingProcessPanelUI>(true) != null) return PanelKind.Process;
            if (list.GetComponentInParent<GrowingPanelUI>(true) != null) return PanelKind.Growing;
            return PanelKind.Manufacturing;
        }

        private static Image AddImage(GameObject go, Image style, float brightness)
        {
            var img = go.AddComponent<Image>();
            if (style != null)
            {
                img.sprite = style.sprite;
                img.type = style.type;
                img.pixelsPerUnitMultiplier = style.pixelsPerUnitMultiplier;
            }
            img.color = new Color(brightness, brightness, brightness, 1f);
            return img;
        }

        private static TextMeshProUGUI AddText(Transform parent, string name, TMP_FontAsset font, Material mat, Color color, TextAlignmentOptions align)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            var t = go.AddComponent<TextMeshProUGUI>();
            if (font != null) t.font = font;
            if (mat != null) t.fontSharedMaterial = mat;
            t.color = color;
            t.fontSize = 14f;
            t.enableAutoSizing = false;
            t.alignment = align;
            t.textWrappingMode = TextWrappingModes.NoWrap;
            t.overflowMode = TextOverflowModes.Ellipsis;
            t.raycastTarget = false;
            return t;
        }

        private void BuildField(RectTransform parent, TMP_FontAsset font, Material mat, Color color, Image style)
        {
            _fieldGo = new GameObject("Field", typeof(RectTransform));
            var rt = (RectTransform)_fieldGo.transform;
            rt.SetParent(parent, false);
            var img = AddImage(_fieldGo, style, 0.85f);

            var area = new GameObject("Text Area", typeof(RectTransform));
            var art = (RectTransform)area.transform;
            art.SetParent(rt, false);
            art.anchorMin = Vector2.zero;
            art.anchorMax = Vector2.one;
            art.offsetMin = new Vector2(6f, 0f);
            art.offsetMax = new Vector2(-6f, 0f);
            area.AddComponent<RectMask2D>();

            var placeholder = AddText(art, "Placeholder", font, mat, new Color(color.r, color.g, color.b, 0.55f), TextAlignmentOptions.MidlineLeft);
            placeholder.text = Strings.SearchPlaceholder;
            placeholder.fontStyle = FontStyles.Italic;
            var text = AddText(art, "Text", font, mat, color, TextAlignmentOptions.MidlineLeft);
            text.richText = false;

            var input = _fieldGo.AddComponent<TMP_InputField>();
            input.textViewport = art;
            input.textComponent = text;
            input.placeholder = placeholder;
            if (font != null) input.fontAsset = font;
            input.pointSize = 14f;
            input.targetGraphic = img;
            input.lineType = TMP_InputField.LineType.SingleLine;
            input.characterLimit = 40;
            input.richText = false;
            input.restoreOriginalTextOnEscape = false;
            input.onFocusSelectAll = true;
            input.navigation = new Navigation { mode = Navigation.Mode.None };
            input.onValueChanged.AddListener(s => { try { Filter.SetText(this, s); } catch (Exception e) { Log.Exception("Filter: text", e); } });
            input.onSelect.AddListener(_ => TypingGuard.Hold(this));
            input.onDeselect.AddListener(_ => TypingGuard.MarkRelease());
            input.onEndEdit.AddListener(_ => TypingGuard.MarkRelease());
            Field = input;
        }

        private void BuildChip(RectTransform parent, TMP_FontAsset font, Material mat, Color color, Image style, Sprite hover)
        {
            _chipGo = new GameObject("Chip", typeof(RectTransform));
            var rt = (RectTransform)_chipGo.transform;
            rt.SetParent(parent, false);
            var img = AddImage(_chipGo, style, 1f);
            var button = _chipGo.AddComponent<UnityEngine.UI.Button>();
            button.targetGraphic = img;
            button.navigation = new Navigation { mode = Navigation.Mode.None };
            _chipGo.AddComponent<ChipClicks>().Bar = this;

            // The pad highlight: the recipe button's own hover sprite over the chip (an amber tint without it).
            _highlight = new GameObject("Highlight", typeof(RectTransform));
            var hrt = (RectTransform)_highlight.transform;
            hrt.SetParent(rt, false);
            hrt.anchorMin = Vector2.zero;
            hrt.anchorMax = Vector2.one;
            hrt.offsetMin = hrt.offsetMax = Vector2.zero;
            var himg = _highlight.AddComponent<Image>();
            himg.sprite = hover != null ? hover : img.sprite;
            himg.type = img.type;
            himg.color = hover != null ? Color.white : new Color(1f, 0.79f, 0.3f, 0.6f);
            himg.raycastTarget = false;
            _highlight.SetActive(false);

            _chipLabel = AddText(rt, "Label", font, mat, color, TextAlignmentOptions.Center);
            _chipLabel.enableAutoSizing = true;
            _chipLabel.fontSizeMin = 10f;
            _chipLabel.fontSizeMax = 14f;
            _chipLabel.margin = new Vector4(4f, 0f, 4f, 0f);
            _chipLabel.text = Strings.Chip(Strings.ChipAll, 0);
        }

        private void BuildEmpty(RecipeListUI list, TMP_FontAsset font, Material mat, Color color)
        {
            var viewport = FindViewport(list);
            if (viewport == null) return;
            var existing = viewport.Find(EmptyName);
            if (existing != null) Destroy(existing.gameObject);
            var go = new GameObject(EmptyName, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(viewport, false);
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.offsetMin = new Vector2(Inset, -Inset - Height);
            rt.offsetMax = new Vector2(-Inset, -Inset);
            go.AddComponent<LayoutElement>().ignoreLayout = true;
            var t = AddText(rt, "Text", font, mat, color, TextAlignmentOptions.Center);
            t.text = Strings.NoMatch;
            go.SetActive(false);
            _empty = go;
        }

        // The list's own scroll view is a direct child named "UI Scroll View". RecipeInfo has another one, deeper, which
        // Workshop moves: never Find by path.
        private static RectTransform ScrollOf(RecipeListUI list)
        {
            foreach (Transform c in list.transform)
                if (c.name == "UI Scroll View") return c as RectTransform;
            return null;
        }

        private static Transform FindViewport(RecipeListUI list)
        {
            var scroll = ScrollOf(list);
            return scroll != null ? scroll.Find("Viewport") : null;
        }

        private void FindScroll(RecipeListUI list)
        {
            _scroll = ScrollOf(list);
            if (_scroll == null) return;
            _scrollOffsetMax = _scroll.offsetMax;
            _scrollRect = _scroll.GetComponent<ScrollRect>();
        }

        // ---- Layout and visibility ----

        // Both parts: field 58 %, chip 42 %, 6 px apart. One part: full width.
        internal void Layout()
        {
            bool f = HasField, c = HasChip;
            if (_chipGo != null && _chipGo.activeSelf != (Shown && c)) _chipGo.SetActive(Shown && c);
            if (_fieldGo != null && _fieldGo.activeSelf != (Shown && f))
            {
                if (!(Shown && f) && Field != null && Field.isFocused)
                {
                    Field.DeactivateInputField();
                    TypingGuard.MarkRelease();
                }
                _fieldGo.SetActive(Shown && f);
            }
            if (f) Place((RectTransform)_fieldGo.transform, 0f, c ? FieldShare : 1f, 0f, c ? -Gap / 2f : 0f);
            if (_chipGo != null) Place((RectTransform)_chipGo.transform, f ? FieldShare : 0f, 1f, f ? Gap / 2f : 0f, 0f);
        }

        private static void Place(RectTransform rt, float x0, float x1, float left, float right)
        {
            rt.anchorMin = new Vector2(x0, 0f);
            rt.anchorMax = new Vector2(x1, 1f);
            rt.offsetMin = new Vector2(left, 0f);
            rt.offsetMax = new Vector2(right, 0f);
        }

        // Shown when the list has recipes and the bar has something to show. Moves the list's scroll view down 48 px
        // while shown, never resizing "Recipes list" itself (RecipeInfo stretches to its height).
        internal void SetShown(bool on)
        {
            bool want = on && (HasField || HasChip);
            if (!want && Field != null && Field.isFocused) Field.DeactivateInputField();
            Shown = want;
            Layout();
            if (_scroll != null)
            {
                if (want && !_shifted)
                {
                    _scroll.offsetMax = new Vector2(_scrollOffsetMax.x, _scrollOffsetMax.y - Shift);
                    _shifted = true;
                }
                else if (!want && _shifted) RestoreScroll();
            }
            if (!want)
            {
                SetEmptyNote(false);
                PadRoute.ResetIf(this);
            }
        }

        private void RestoreScroll()
        {
            if (_scroll != null) _scroll.offsetMax = _scrollOffsetMax;
            _shifted = false;
        }

        internal void SetEmptyNote(bool on)
        {
            if (_empty != null && _empty.activeSelf != on) _empty.SetActive(on);
        }

        internal void SetChipLabel(string s)
        {
            if (_chipLabel != null && _chipLabel.text != s) _chipLabel.text = s;
        }

        internal void SetHighlight(bool on)
        {
            if (_highlight != null && _highlight.activeSelf != on) _highlight.SetActive(on);
        }

        internal void ScrollToTop()
        {
            if (_scrollRect != null) _scrollRect.verticalNormalizedPosition = 1f;
        }

        // Keyboard Use Item on the bar (P8): the field takes focus next frame, so the key's own character is never typed.
        internal void FocusNextFrame()
        {
            if (!HasField || !Shown) return;
            _focusNext = true;
            _focusFrame = Time.frameCount;
        }

        // ---- Per frame (Plugin.Update -> TickActive) ----

        internal static void TickActive()
        {
            var bar = Active;
            TypingGuard.Watch(bar);
            PadRoute.Tick(bar);
            if (bar != null) bar.Tick();
        }

        private void Tick()
        {
            if (List == null) return;
            // DestroyButtons-only paths (a vessel that starts processing, SRC/manufacturingProcess.cs:514-526).
            if (Shown && List.currentRecipes.Count == 0) SetShown(false);

            // Re-enabled without a rebuild (a journal tab switch and back): buttons hidden before OnDisable come back.
            if (_enableCheck)
            {
                _enableCheck = false;
                if (HidAny)
                {
                    Filter.Apply(this, live: false);
                    if (InventoryNavigationHandler.Instance != null) Filter.InitList(InventoryNavigationHandler.Instance, Kind);
                }
            }

            if (_focusNext && Time.frameCount > _focusFrame)
            {
                _focusNext = false;
                if (Field != null && Field.isActiveAndEnabled && !Field.isFocused)
                {
                    Field.ActivateInputField();
                    Log.Verbose("Typing: focus from the bar (Use Item).");
                }
            }

            if (Pending)
            {
                Pending = false;
                if (Shown) Filter.Apply(this, live: true);
            }
        }

        // ---- Unity ----

        private void Awake() => All.Add(this);

        private void OnEnable()
        {
            if (_wasDisabled) _enableCheck = true;
        }

        // Station exit (CraftingBase.Exit), journal tab switch or scene unload (D5): clear text and chip without
        // re-applying, the list is being torn down or rebuilds on Enter. Nothing is remembered.
        private void OnDisable()
        {
            _wasDisabled = true;
            _focusNext = false;
            if (Field != null) Field.SetTextWithoutNotify("");
            Text = "";
            Tokens = new string[0];
            Chip = Filter.ChipValue.All;
            Pending = false;
            Sticky = -1;
            SetHighlight(false);
            PadRoute.ResetIf(this);
            TypingGuard.MarkRelease();
        }

        private void OnDestroy()
        {
            All.Remove(this);
            RestoreScroll();
            TypingGuard.Forget(this);
        }

        // Left click: next Show value; right click: previous (spec §6.1).
        internal sealed class ChipClicks : MonoBehaviour, IPointerClickHandler
        {
            internal FilterBar Bar;

            public void OnPointerClick(PointerEventData e)
            {
                if (Bar == null || e == null) return;
                try
                {
                    if (e.button == PointerEventData.InputButton.Left) Filter.Cycle(Bar, +1);
                    else if (e.button == PointerEventData.InputButton.Right) Filter.Cycle(Bar, -1);
                }
                catch (Exception ex) { Log.Exception("Filter: chip click", ex); }
            }
        }
    }
}
