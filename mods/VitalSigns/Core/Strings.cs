using System;
using System.Globalization;

namespace VitalSigns
{
    // Every player-facing string (spec §2). Humanized 2026-11-17; run through the humanizer again after any change (CLAUDE.md).
    // Tooltip strings (Tip*, More, the addiction and duck lines) are ASCII only: the game's tooltip font may lack other
    // characters. Raw DamageTypeNames never reach the screen.
    internal static class Strings
    {
        private static readonly CultureInfo C = CultureInfo.InvariantCulture;

        internal const string ModName = "Vital Signs";

        // ---- Window frame ----
        private static readonly string[] PageNames = { "Health", "Mental health", "Addictions" };
        internal static string PageName(int i) => i >= 0 && i < PageNames.Length ? PageNames[i] : "";
        internal const string Close = "Close";
        internal const string BackHint = "BACK";
        internal const string PagesHint = "PAGES";
        internal const string ScrollHint = "ARROWS)SCROLL";
        internal const string PageFailed = "This page couldn't be built. The log has the details.";
        internal const string NoPlayer = "No character loaded.";
        internal const string HintLabel = "VITAL SIGNS";

        // "More: Character page, X", built from the ways in that work right now; null when there is none.
        internal static string More
        {
            get
            {
                if (!VitalWindow.Available) return null;
                if (CharacterPageInput.Available && Cfg.Hot.CharacterPageKey)
                    return "More: Character page, " + Game.KeyText("Use Item", Game.Controller ? "A" : "X");
                if (PauseEntry.Wanted && PauseEntry.Present) return "More: pause menu, Vital Signs";
                if (Keys.Usable(Cfg.Access.OpenKey)) return "More: press " + Cfg.Access.OpenKey.Value;
                return null;
            }
        }

        // A tooltip addition with the "More" line when there is a way in.
        internal static string WithMore(string s)
        {
            string more = More;
            return more == null ? s : s + "\n" + more;
        }

        // ---- Toasts ----
        internal static string ToastFold(int n) => $"+{n} more: see Vital Signs";
        internal static string ThresholdToast(string label, float sum, int minutes) => $"{label}: -{Fmt.Num(sum)} HP over {Fmt.Minutes(minutes)}";
        internal static string WakeToast(bool slept, float hours, string list) =>
            $"While you {(slept ? "slept" : "waited")} ({(hours < 1f ? UnderAnHour : Fmt.Hours(hours))}): {list}";

        // ---- Reason labels (spec §5.8) ----
        internal static string Reason(int reason, bool silent)
        {
            switch ((DamageTypeNames)reason)
            {
                case DamageTypeNames.Unspecified: return "Injury";
                case DamageTypeNames.Violence: return "Violence";
                case DamageTypeNames.Hunger: return "Starvation";
                case DamageTypeNames.Thirst: return "Dehydration";
                case DamageTypeNames.ShitSeizure: return "Lost bowel control";
                case DamageTypeNames.SMV: return "Fungal infection";
                case DamageTypeNames.Drown: return "Drowning";
                case DamageTypeNames.Fire: return "Heat or fire";
                case DamageTypeNames.Radiation: return silent ? "Radiation dose" : "Radiation area";
                case DamageTypeNames.Fall: return "Fall";
                case DamageTypeNames.Poison: return "Poison";
                case DamageTypeNames.Withdrawal: return "Withdrawal";
                case DamageTypeNames.Suicide: return "Suicide";
                case DamageTypeNames.Bleed: return "Bleeding";
                case DamageTypeNames.Mushrooms: return "Mushroom spores";
                case DamageTypeNames.Explosion: return "Explosion";
                case DamageTypeNames.Gun: return "Gunshot";
                case DamageTypeNames.Gass: return "Gas";
                case DamageTypeNames.Force: return "Impact";
                case DamageTypeNames.Liquid: return "Harmful liquid";
                case DamageTypeNames.Electric: return "Electric shock";
                default: return "Injury";
            }
        }

        internal const string Untraced = "Not traced (console or another mod)";
        internal const string BleedingLoss = "Bleeding";
        internal const string HiddenPrefix = "Hidden damage: ";

        internal static string LossLabel(ulong key)
        {
            int id = LossKey.Id(key), flags = LossKey.Flags(key), reason = LossKey.Reason(key);
            switch (LossKey.CatOf(key))
            {
                case Cat.Damage:
                {
                    string s = Reason(reason, (flags & LossKey.Silent) != 0);
                    bool violence = reason == (int)DamageTypeNames.Violence;
                    if ((flags & LossKey.Fungal) != 0 && violence) s = "Fungal treatment at the doctor";
                    else if ((flags & LossKey.Crafting) != 0) s = violence ? "Crafting accident" : "Crafting accident (" + s + ")";
                    if (id > 0) s += " (" + Ctx.Label(id) + ")";
                    if ((flags & LossKey.Asleep) != 0) s += " (while asleep)";
                    return s;
                }
                case Cat.SmvCap: return "Fungal infection: health capped";
                case Cat.Bleeding: return BleedingLoss;
                case Cat.Hidden: return HiddenPrefix + Ctx.Label(id);
                default: return Untraced;
            }
        }

        // Toast, wake-summary and death-summary label: no stacked colons ("Caffeine overdose: -5 HP over 5 min").
        internal static string ShortLabel(Entry e) => ToastLabel(e.Key, e.Label);

        internal static string ToastLabel(ulong key, string label)
        {
            switch (LossKey.CatOf(key))
            {
                case Cat.Hidden: return Ctx.Label(LossKey.Id(key));
                case Cat.SmvCap: return SmvCapShort;
                default: return label;
            }
        }

        internal const string SmvCapShort = "Fungal infection cap";

        internal static string SmvCapLabel(float cap) => "Fungal infection: health capped at " + Math.Max(0f, cap).ToString("0", C);

        // ---- Sources ----
        internal const string FoodOrDrink = "Food or drink";
        internal const string AWait = "A wait";
        internal const string UnknownSource = "Unknown source";
        internal const string UnknownHealing = "unknown healing";
        internal const string CaffeineOverdose = "Caffeine overdose";
        internal const string CaffeineFromBefore = "caffeine from before this load";
        internal const string After = "after ";
        internal const string FromBeforeLoad = "From before this load";
        internal const string FromBeforeLoadDetail = "the game doesn't record where it came from";
        internal const string Wound = "Wound";
        internal const string SelfHarmBreakdown = "Self-harm during a breakdown";
        internal const string SelfHarmBlackout = "Self-harm during a blackout";
        internal const string CrumpledPaper = "Crumpled paper instead of toilet paper";
        internal const string OtherBleeding = "Other (harmful shower water or another source)";
        internal static string FoodPenaltyLabel(string title) => "Fungal food penalty (" + title + ")";
        internal static string FoodPenaltyDetail(float smv) => "food and drink hurt above SMV 40; SMV " + smv.ToString("0", C);
        internal static string Merged(string a, string b) => a == b ? a : a + " and " + b;
        internal static string CancelledBy(string from) => "partly cancelled by healing from " + from;

        // What a pool credit came from.
        internal static string FrameSource(Frame f)
        {
            switch (f.Kind)
            {
                case K.Item:
                case K.Wait: return Ctx.Label(f.Id);
                case K.FungalTreatment: return "the doctor";
                default: return UnknownHealing;
            }
        }

        // ---- Log rows (spec §5.7) ----
        internal static string Line(Entry e)
        {
            string when = Fmt.Range(e.FirstStamp, e.LastStamp);
            switch (e.Kind)
            {
                case EntryKind.Owed:
                {
                    string s = $"{when}   {e.Cause}: {Fmt.Num(e.Hp)} hidden damage queued";
                    return Math.Abs(e.Cost - e.Hp) > 0.05f ? s + $" (adds {Fmt.Num(e.Cost)} HP to the drain)" : s;
                }
                case EntryKind.Bleed:
                    return $"{when}   Bleeding +{Fmt.Num(e.Hp)}% ({e.Cause})";
                default:
                {
                    string hits = e.Hits < 2 ? "" : e.Tick ? $"   {e.Hits} updates" : $"   {e.Hits} hits";
                    return $"{when}   {e.Label}   {Fmt.Hp(e.Hp)}{hits}";
                }
            }
        }

        internal static string EntryDetail(Entry e)
        {
            string d = e.Detail ?? "";
            if (e.Attributed) d = Join(d, "shared out oldest first");
            if (e.Kind == EntryKind.Loss && e.Hidden) d = Join(d, "no warning from the game");
            return d.Length > 0 ? d : null;
        }

        private static string Join(string a, string b) => a.Length == 0 ? b : a + " · " + b;

        internal static string DividerLine(string when, string text) => $"<b>{when}   {text}</b>";
        internal const string LoadedSave = "Loaded a save";
        internal const string NewGame = "Started a new game";
        internal static string OlderHidden(int n) => $"{n} older lines not shown.";
        internal const string LogHeading = "Damage log";
        internal const string LogEmpty = "Nothing logged yet this session.";
        internal static string LastDeath(string when, string title) => $"Last death ({when}): {title}";

        // ---- Death (spec §5.9) ----
        internal const string DeathHidden = "You died from hidden damage (the game calls it poisoning)";
        internal const string DeathBled = "You bled out";
        internal const string DeathSmv = "Fungal infection reached 100";
        internal static string DeathOther(string label) => "You died: " + label;
        internal static string StillOwed(string debts) => "Still queued: " + debts;

        // ---- Losing health now (spec §6) ----
        internal const string LosingNow = "Losing health now";
        internal static string HealthNow(int hp) => $"Health {hp}%";
        internal static string HiddenToCome(float hp, int ticks) =>
            $"Hidden damage still to come: -{Fmt.Num(hp)} HP over {Fmt.Ticks(ticks)} at normal speed";
        internal static string DebtLine(string label, float remaining, float original, string note)
        {
            string s = $"{label}: {Fmt.Num(remaining)} left";
            if (original - remaining > 0.05f) s += $" of {Fmt.Num(original)}";
            return note != null ? s + " (" + note + ")" : s;
        }
        internal const string AdviceHealingFood = "Healing food and medicine cancel hidden damage.";
        internal const string AdviceDoctor = "The doctor's Heal Wounds restores health but doesn't clear hidden damage.";
        internal static string HiddenHealing(float hp, int ticks) => $"Hidden healing still to come: +{Fmt.Num(hp)} HP over {Fmt.Ticks(ticks)} at normal speed";
        internal static string BleedEndless(float perTick) => $"Bleeding won't stop on its own: -{Fmt.Num(perTick)} HP a minute at normal speed";
        internal static string BleedStops(float hp, int ticks) => $"Bleeding: about -{Fmt.Num(hp)} HP more over {Fmt.Ticks(ticks)} if untreated (normal speed)";
        internal static string BleedCausesLine(string causes) => "Caused by: " + causes;
        internal static string DeathIn(int ticks, string causes) =>
            $"At this rate you die in {Fmt.Ticks(ticks)} at normal speed, if nothing else changes (counting {causes}).";
        internal const string CauseHidden = "hidden damage", CauseBleeding = "bleeding", CauseStarving = "starvation",
            CauseThirst = "dehydration", CauseRadiation = "the radiation dose you carry";
        internal static string ListAnd(System.Collections.Generic.List<string> items)
        {
            if (items.Count == 0) return "";
            if (items.Count == 1) return items[0];
            return string.Join(", ", items.GetRange(0, items.Count - 1).ToArray()) + " and " + items[items.Count - 1];
        }
        internal static string SmvCapNow(float cap) => $"Health can't go above {Math.Max(0f, cap).ToString("0", C)} while the fungal infection is changing.";
        internal const string SmvNoCap = "Fungal infection: no health cap right now (only while the infection is changing).";
        internal const string SmvFood = "Ordinary food and drink cost health: (food + drink) x (SMV - 40) / 60 per item, scaled by quality.";
        internal static string CapCancelled(float hp) => $"Healing cancelled by the fungal cap this session: {Fmt.Num(hp)} HP";
        internal static string RadiationRate(float perHour) => $"Radiation dose: -{Fmt.Num(perHour)} HP an hour at this dose";
        internal static string RadiationStops(int ticks, float hp) => $"If you stay out of radiation, it stops in {Fmt.Ticks(ticks)} after about -{Fmt.Num(hp)} HP.";
        internal static string RadiationLong(int ticks) => $"If you stay out of radiation, it lasts more than {Fmt.Hours(ticks / 60f)}.";
        internal static string CaffeineNextUpdate(float amount, bool atLeast) =>
            $"Caffeine overdose at the next update: {(atLeast ? "at least " : "")}{Fmt.Num(amount)} hidden damage" +
            (atLeast ? " (caffeine still kicking in adds to it)" : "");
        internal const string CaffeineKickingIn = "Caffeine still kicking in: if tiredness drops below -100, half of what's left becomes hidden damage.";
        internal const string UnderAnHour = "under an hour";
        internal const string JustNow = "just now";
        internal const string AgoWord = "ago";

        // ---- Health tooltip (D8) ----
        internal static string TipHidden(float hp, int ticks) => $"Hidden damage: -{Fmt.Num(hp)} HP over {Fmt.Ticks(ticks)}";
        internal static string TipLastLoss(string label, string hp, string ago) => $"Last loss: {label} {hp}, {ago}";

        // ---- Mental health (spec §7) ----
        internal const string MoodHeading = "Mental health";
        internal const string MoodDisabled = "Mental health is turned off in your difficulty settings.";
        internal static string HudNow(float pct) => $"Mental health (HUD): {Fmt.Pct(pct)}";
        internal static string Underlying(float v) => $"Underlying mood: {Fmt.Num(v)}";
        internal static string AlcoholLift(float v) => $"Alcohol lift: +{Fmt.Num(v)} points";
        internal static string HygieneTerm(float v) => $"Hygiene: {Fmt.Signed(v)} points";
        internal const string Clamped = "The sum is outside 0-100, so the HUD shows the nearest end.";
        internal static string HudStale(float y) => $"The HUD value only updates while your mood is moving. With your hygiene now, the next update would show {Fmt.Pct(y)}.";
        internal static string NextUpdateOnly(float y) => $"Next update: {Fmt.Pct(y)}.";
        // Target, ETA and needs limit are on the underlying-mood scale (100 - currentStatus), not the HUD's (D14).
        internal static string HeadingToward(float v) => $"Underlying mood heading toward {Fmt.Num(v)}";
        internal const string AtTarget = "Your underlying mood has reached that level.";
        internal static string HeldByNeeds(float v) => $"While you're awake, unmet needs hold it at {Fmt.Num(v)} until they're met.";
        internal static string ReachesIn(float v, float hours) => $"Reaches {Fmt.Num(v)} in {Fmt.Hours(hours)} awake at normal speed, if nothing changes.";
        internal static string HudAtEnd(float pct) => $"With your hygiene and alcohol now, the HUD would then show {Fmt.Pct(pct)}.";
        internal static string AboutAwake(float hours) => $"(about {Fmt.Hours(hours)} awake)";
        internal static string TipHeldAt(float v) => $", held at {Fmt.Num(v)} by needs";
        internal static string SmvMood(float smv, float cut) => $"Fungal infection (SMV {smv.ToString("0", C)}) cuts depression by {(cut * 100f).ToString("0", C)}% each update.";
        internal const string SmokingSlow = "4x slower: you're addicted to smoking and your smokes need is under 50%.";
        internal const string SmokingFast = "Faster than normal because of your smokes need.";
        internal static string AsleepDrift(float perMinute) => $"While asleep, mental health also rises by {perMinute.ToString("0.###", C)} points a minute, and unmet needs don't hold it down.";
        internal const string NeedsUnreadable = "The needs limit can't be read in this game version.";
        internal static string NeedsCap(float v) => $"Unmet needs cap your underlying mood at {Fmt.Num(Math.Max(0f, Math.Min(100f, v)))} (next update)";
        internal static string NeedTerm(string name, float term) => $"{name}: {(term < 0f ? "+" : "-")}{Math.Abs(term).ToString("0.##", C)} points";
        internal const string NeedsAsleep = "Asleep: this limit applies again when you wake.";
        internal const string GamblingBug = "Your gambling need isn't counted here because of a game bug.";
        internal const string NeedHealth = "Health", NeedTiredness = "Tiredness", NeedHunger = "Hunger", NeedThirst = "Thirst",
            NeedBowel = "Bowel", NeedBladder = "Bladder", NeedAlcohol = "Alcohol need", NeedMushroom = "Mushroom need",
            NeedSmokes = "Smokes need", NeedGambling = "Gambling need", NeedOrangeHigh = "Orange mushroom high", NeedHigh = "Mushroom high";
        internal static string Expectations(float level) => $"Expectations: {level.ToString("0", C)} of 100 (50 is neutral)";
        internal static string ExpectationEffect(float o)
        {
            float a = Math.Abs(o);
            if (a < 0.005f) return "Items, activities, your bed and hygiene count as they are.";
            string worse = o > 0f ? "worse" : "better", other = o > 0f ? "better" : "worse";
            return $"Items, activities and waits count {a.ToString("0.##", C)} points {worse}; your bed counts {(10f * a).ToString("0.#", C)} {worse}; hygiene counts {a.ToString("0.##", C)} {other}.";
        }
        internal static string Strength(float c) =>
            $"At your current mood, antidepressants work at {c.ToString("0", C)}% strength and the mood boost from food and drink at {(100f - c).ToString("0", C)}%.";

        internal const string FatigueHeading = "Items wearing off";
        internal const string FatigueNone = "No item has lost strength.";
        internal static string StrengthPct(float s) => $"{(s * 100f).ToString("0", C)}% strength";
        internal static string NoEffectFor(float noEffectHours, float fullHours) => $"no effect for another {Fmt.Hours(noEffectHours)}, full strength in {Fmt.Hours(fullHours)} (game hours, asleep or awake)";
        internal static string FullIn(float hours) => $"full strength in {Fmt.Hours(hours)} (game hours, asleep or awake)";
        internal static string DrinksFrom(string container) => "Drinks from a " + container;
        internal const string ContainerNote = "(drinks from this container share one entry)";
        internal static string OtherFatigued(int n) => $"{n} other item{(n == 1 ? "" : "s")} that lower your mood now (fatigue only weakens good effects)";
        internal static string WithdrawalCost(string name, float perHour) => $"{name} withdrawal: -{Fmt.Num(perHour)} points an hour awake";
        internal static string DuckMoodPerHour(float pts) => $"Duck craving costs {Fmt.Num(pts)} points an hour awake";
        internal const string DuckMoodPerFrame = "Duck craving costs a little every frame";

        // Mood tooltip (ASCII).
        internal static string TipAlcohol(float v) => "Alcohol +" + Fmt.Num(v);
        internal static string TipHygiene(float v) => "Hygiene " + Fmt.Signed(v);
        internal static string TipNeeds(float v, string names) =>
            $"Needs cap it at {Fmt.Num(Math.Max(0f, Math.Min(100f, v)))}" + (names.Length > 0 ? $" ({names})" : "");
        internal static string TipMedicine(string title, float strength, float fullHours) =>
            $"{title}: {(strength * 100f).ToString("0", C)}% strength, full in {Fmt.Hours(fullHours)}";

        // ---- Addictions (spec §8.4) ----
        internal const string AddictionsHeading = "Addictions";
        internal const string AddAlcohol = "Alcohol", AddMushroom = "Mushroom", AddSmoking = "Smoking", AddGambling = "Gambling";
        internal static string AddictionRow(string name, float level, float need) =>
            $"{name}: addiction {level.ToString("0.#", C)}/100, need {Fmt.Pct(need)}";
        internal const string NotAddicted = "Not addicted";
        internal const string NoRecovery = "Doesn't wear off on this difficulty";
        internal const string NextUpdate = "Wears off at the next update";
        internal const string Rising = "Rising over time (changed by another setting or mod)";
        internal static string WearsOff(float hours) => $"Wears off in about {Fmt.Hours(hours)} awake at normal speed";
        internal static string WearsOffMore(float hours) => $"Wears off in more than {Fmt.Hours(hours)} awake";
        internal static string PerHour(float v) => $"-{v.ToString("0.##", C)} an hour";
        internal const string AboveCapFixed = "above the cap; set to 100 at your next item";
        internal const string AboveCap = "above 100";
        internal const string AddictionNote = "Paused while asleep or passed out and during conversations; slower during the game's own fast waits. Sauna, gambling and items change it.";
        internal const string SceneEvent = "a scene event";
        internal static string TipAddiction(float level, string eta, bool recovering) =>
            $"Addiction {level.ToString("0", C)}/100, {LowerFirst(eta)}" + (recovering ? " (paused while asleep)" : "");

        private static string LowerFirst(string s) => string.IsNullOrEmpty(s) ? s : char.ToLowerInvariant(s[0]) + s.Substring(1);

        internal const string DuckHeading = "Duck addiction";
        internal static string DuckCraving(float status, float from) => $"Craving {status.ToString("0", C)}% (hurts mood from {from.ToString("0", C)}%)";
        internal static string DuckCount(int n, int need) => $"Rubber ducks carried: {n} of {need} (inventory, backpack and worn slots; metal ducks and storage don't count)";
        internal const string DuckNoDecay = "Duck addiction doesn't wear off with time.";
    }
}
