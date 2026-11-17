using System.Collections.Generic;

namespace VitalSigns
{
    // What the game is doing when health, the hidden-damage pool or bleeding changes (spec §5.1).
    internal enum K : byte
    {
        Untraced, Damage, HealthTick, Item, TirednessTick, Wait, SmvTick, MoodTick, Toilet, SelfHarm, FungalTreatment, Crafting,
    }

    internal struct Frame
    {
        internal K Kind;
        internal short Reason;   // DamageTypeNames for Damage frames
        internal int Id;         // interned label (item title, wait title)
        internal bool Silent;    // Damage: no flash or sound (including the mask on spores)
        internal bool Asleep;
        internal float Smv;      // Item: the fungal share of GetHealth (negative = the SMV > 40 food penalty)
    }

    // The context stack: a fixed struct array (depth 16) with an implicit Untraced bottom. Context hooks push in a prefix
    // and truncate back to their stored depth in a finalizer, so an exception never leaves a frame behind. A push past
    // the depth is dropped (WarnOnce); the matching truncate is then a no-op. Labels are interned once per item ID or wait
    // instance, so per-frame hazards allocate nothing.
    internal static class Ctx
    {
        private const int Max = 16;
        private static readonly Frame[] Stack = new Frame[Max];
        private static Frame _bottom;
        internal static int Depth;

        internal static ref Frame Top
        {
            get
            {
                if (Depth == 0) { _bottom = default; return ref _bottom; }
                return ref Stack[Depth - 1];
            }
        }

        // Returns the depth before the push.
        internal static int Push(Frame f)
        {
            int d = Depth;
            if (d >= Max)
            {
                Log.WarnOnce("ctx overflow", "Log: the context stack is full; some losses get a less exact label.");
                return d;
            }
            Stack[d] = f;
            Depth = d + 1;
            return d;
        }

        internal static void Truncate(int depth)
        {
            if (depth >= 0 && depth < Depth) Depth = depth;
        }

        internal static bool Has(K kind)
        {
            for (int i = 0; i < Depth; i++) if (Stack[i].Kind == kind) return true;
            return false;
        }

        internal static Frame At(int i) => Stack[i];

        // Between frames the stack must be empty (Plugin.Update).
        internal static void FrameCheck()
        {
            if (Depth == 0) return;
            Log.WarnOnce("ctx leak", $"Log: {Depth} context frame(s) were still open between frames; reset.");
            Depth = 0;
        }

        // ---- Interned labels ----

        private static readonly List<string> Labels = new List<string> { "" };
        private static readonly Dictionary<string, int> ByText = new Dictionary<string, int>();
        private static readonly Dictionary<int, int> ByItem = new Dictionary<int, int>();
        private static readonly Dictionary<int, int> ByWait = new Dictionary<int, int>();

        internal static int Intern(string text)
        {
            text = Game.Clean(text);
            if (text.Length == 0) return 0;
            if (ByText.TryGetValue(text, out int id)) return id;
            id = Labels.Count;
            Labels.Add(text);
            ByText[text] = id;
            return id;
        }

        internal static string Label(int id) => id > 0 && id < Labels.Count ? Labels[id] : "";

        internal static int ItemLabel(Item it)
        {
            if (it == null) return Intern(Strings.FoodOrDrink);
            if (ByItem.TryGetValue(it.ID, out int id)) return id;
            id = Intern(string.IsNullOrEmpty(it.Title) ? "#" + it.ID : it.Title);
            ByItem[it.ID] = id;
            return id;
        }

        internal static int WaitLabel(InteractableWait w)
        {
            if (w == null) return Intern(Strings.AWait);
            int key = w.GetInstanceID();
            if (ByWait.TryGetValue(key, out int id)) return id;
            string title = null;
            try { title = w.title; } catch { }
            if (string.IsNullOrEmpty(Game.Clean(title))) title = w.gameObject != null ? w.gameObject.name : Strings.AWait;
            id = Intern(title);
            ByWait[key] = id;
            return id;
        }

        // Scene objects are destroyed on every level change; their instance IDs may be reused.
        internal static void OnSceneLoaded() => ByWait.Clear();
    }
}
