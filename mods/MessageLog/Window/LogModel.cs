using System;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;

namespace MessageLog
{
    // What the window lists (spec §8.4): the filtered rows newest first, the chip counts and the shown/total counts.
    // Rebuilt in Update only when the log, the chip, the pin, the search (after a short pause in typing) or the PT-BR
    // state changed; never in OnGUI. The rebuild needs only the cheap part of each entry (category, English search key).
    // With PT-BR on, the translated display text is built per entry in a time-boxed pass (newest first, a few ms per
    // frame) and on demand for the rows actually drawn, so opening a long log loaded from file never translates thousands
    // of messages in one frame. Display text is kept until the PT-BR state flips.
    internal static class LogModel
    {
        private const float SearchDebounce = 0.15f;
        private const double PassBudgetMs = 3.0;

        internal static readonly List<Entry> Rows = new List<Entry>();
        internal static readonly int[] ChipCounts = new int[Categories.Chips.Length];
        internal static int Total;            // messages in the log (gap markers not counted)
        internal static int ShownCount;       // rows shown (gap markers not counted)
        internal static bool AnyDialogue;     // the log holds dialogue (the chip stays even with capture off)

        internal static int Chip;             // index into Categories.Chips; kept for the session
        internal static string Search = "";  // kept for the session
        internal static char PinKind;         // '\0' none, 'd' a speaker, 'c' all choices, 'n' one notification type
        internal static string PinHead;

        private static int _builtVersion = -1, _builtChip = -1, _gen;
        private static string _builtSearch, _builtPin;
        private static bool _builtPt, _dirty = true;
        private static float _searchChangedAt;
        private static string[] _terms = new string[0];
        private static int _passVersion = -1, _passGen = -1, _passI, _passJ;
        private static bool _passDone;
        private static readonly Stopwatch PassClock = new Stopwatch();

        internal static void MarkDirty() => _dirty = true;

        internal static void SetSearch(string s)
        {
            s = s ?? "";
            if (s == Search) return;
            Search = s;
            _searchChangedAt = Time.unscaledTime;
        }

        internal static void SetChip(int i)
        {
            int n = Categories.Chips.Length;
            Chip = ((i % n) + n) % n;
        }

        internal static bool Pinned => PinKind != '\0';

        internal static void Pin(Entry e)
        {
            if (e == null || e.Kind == 'g') return;
            char kind = e.Kind;
            string head = kind == 'c' ? "" : e.Head ?? "";
            if (PinKind == kind && PinHead == head) { Unpin(); return; }
            PinKind = kind;
            PinHead = head;
        }

        internal static void Unpin()
        {
            PinKind = '\0';
            PinHead = null;
        }

        // The pin chip's text, built in the rebuild.
        internal static string PinText = "";

        private static string PinLabel()
        {
            if (!Pinned) return "";
            if (PinKind == 'c') return Strings.You;
            string head = PinHead ?? "";
            if (_builtPt) head = Compat.PtBr.Translate(head) ?? head;
            return Skin.Esc(Text.OneLine(Text.StripTags(head)));
        }

        // Called from LogWindow.Update while the window is open.
        internal static void Tick()
        {
            bool pt = false;
            try { pt = Compat.PtBr.Active; } catch { }
            if (pt != _builtPt) { _builtPt = pt; _gen++; _dirty = true; }
            string pin = Pinned ? PinKind + "|" + PinHead : "";
            bool searchSettled = Time.unscaledTime - _searchChangedAt >= SearchDebounce;
            bool changed = _dirty || _builtVersion != History.Version || _builtChip != Chip || _builtPin != pin;
            if (changed || (_builtSearch != Search && searchSettled)) Rebuild(pin);
            TranslatePass();
        }

        // PT-BR on: translate the entries not done yet, newest first, within PassBudgetMs per frame. Restarts when the log
        // or the PT-BR state changes (done entries are skipped at the cost of one compare). While a search is active,
        // the list is rebuilt after each slice, so Portuguese words match as their messages get translated.
        private static void TranslatePass()
        {
            if (!_builtPt) return;
            if (_passVersion != History.Version || _passGen != _gen)
            {
                _passVersion = History.Version;
                _passGen = _gen;
                _passI = History.Notes.Count - 1;
                _passJ = History.Talk.Count - 1;
                _passDone = false;
            }
            if (_passDone) return;
            var notes = History.Notes;
            var talk = History.Talk;
            if (_passI >= notes.Count) _passI = notes.Count - 1;
            if (_passJ >= talk.Count) _passJ = talk.Count - 1;
            bool any = false;
            PassClock.Reset();
            PassClock.Start();
            while (_passI >= 0 || _passJ >= 0)
            {
                bool takeNote = _passJ < 0 || (_passI >= 0 && notes[_passI].Seq > talk[_passJ].Seq);
                var e = takeNote ? notes[_passI--] : talk[_passJ--];
                if (e.Kind == 'g' || e.Gen == _gen) continue;
                Prepare(e);
                any = true;
                if (PassClock.Elapsed.TotalMilliseconds >= PassBudgetMs) break;
            }
            PassClock.Stop();
            if (_passI < 0 && _passJ < 0) _passDone = true;
            if (any && _terms.Length > 0) _dirty = true;
        }

        // The window, for each row it draws and for the detail strip: the display text, built now if the pass hasn't
        // reached this entry yet.
        internal static void Show(Entry e)
        {
            if (e != null && e.Kind != 'g' && e.Gen != _gen) Prepare(e);
        }

        private static void Rebuild(string pin)
        {
            _dirty = false;
            _builtVersion = History.Version;
            _builtChip = Chip;
            _builtPin = pin;
            if (_builtSearch != Search)
            {
                _builtSearch = Search;
                _terms = Fold.Tokens(Search);
            }
            PinText = PinLabel();
            Rows.Clear();
            Array.Clear(ChipCounts, 0, ChipCounts.Length);
            Total = 0;
            ShownCount = 0;
            AnyDialogue = History.Talk.Count > 0;
            var chips = Categories.Chips;
            Cat want = chips[Mathf.Clamp(Chip, 0, chips.Length - 1)];
            bool all = want == Cat.None;
            bool showGaps = all && !Pinned && _terms.Length == 0;

            var notes = History.Notes;
            var talk = History.Talk;
            int i = notes.Count - 1, j = talk.Count - 1;
            while (i >= 0 || j >= 0)
            {
                bool takeNote = j < 0 || (i >= 0 && notes[i].Seq > talk[j].Seq);
                var e = takeNote ? notes[i--] : talk[j--];
                if (e.Kind == 'g')
                {
                    if (showGaps) Rows.Add(e);
                    continue;
                }
                Total++;
                PrepareBase(e);
                if (!PinMatches(e) || !SearchMatches(e)) continue;
                ChipCounts[0]++;
                for (int c = 1; c < chips.Length; c++)
                    if ((e.Cat & chips[c]) != 0) ChipCounts[c]++;
                if (!all && (e.Cat & want) == 0) continue;
                Rows.Add(e);
                ShownCount++;
            }
        }

        private static bool PinMatches(Entry e)
        {
            if (!Pinned) return true;
            if (e.Kind != PinKind) return false;
            return PinKind == 'c' || e.Head == PinHead;
        }

        private static bool SearchMatches(Entry e)
        {
            if (_terms.Length == 0) return true;
            string f = e.Folded ?? "";
            foreach (var t in _terms)
                if (f.IndexOf(t, StringComparison.Ordinal) < 0) return false;
            return true;
        }

        // The cheap part, for every entry at each rebuild: the category and a search key. Without PT-BR that is the whole
        // display text (no Translator calls). With PT-BR, the key holds the English only until Prepare adds the
        // Portuguese.
        private static void PrepareBase(Entry e)
        {
            if (e.BaseGen == _gen) return;
            if (!_builtPt) { Prepare(e); return; }
            e.BaseGen = _gen;
            e.Cat = Categories.Of(e.Kind, e.Head);
            if (e.Gen != _gen) e.Folded = Fold.Of((e.Head ?? "") + " " + (e.Kind == 'c' ? Strings.You : "") + " " + (e.Text ?? ""));
        }

        // Display text (spec §5.3), built once per entry and PT-BR state. Notification type and text go through the
        // PT-BR Translator (stored raw, so the lookup gets the exact English); dialogue text is already shown as captured
        // (PT-BR translates it at the source), only generic speaker titles ("Guard") are looked up.
        private static void Prepare(Entry e)
        {
            if (e.Gen == _gen && e.Folded != null) return;
            e.Gen = _gen;
            e.BaseGen = _gen;
            e.Cat = Categories.Of(e.Kind, e.Head);
            string head = e.Head ?? "", text = e.Text ?? "";
            if (_builtPt)
            {
                if (e.Kind == 'n')
                {
                    string th = Compat.PtBr.Translate(head), tt = Compat.PtBr.Translate(text);
                    if (th != null) head = Compat.PtBr.Gender(th);
                    if (tt != null) text = Compat.PtBr.Gender(tt);
                }
                else if (e.Kind == 'd' && head.Length > 0)
                {
                    string th = Compat.PtBr.Translate(head);
                    if (th != null) head = Compat.PtBr.Gender(th);
                }
            }
            if (e.Kind == 'c') head = Strings.You;
            e.ShownHead = Skin.Esc(Text.OneLine(Text.StripTags(head)));
            e.ShownText = Skin.Esc(Text.StripTags(text));
            e.RowText = Text.OneLine(e.ShownText);
            e.Folded = Fold.Of((e.Head ?? "") + " " + head + " " + (e.Text ?? "") + " " + text);
        }
    }
}
