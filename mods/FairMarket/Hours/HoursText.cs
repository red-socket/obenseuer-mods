using System;
using System.Collections.Generic;
using Object = UnityEngine.Object;

namespace FairMarket
{
    // The opening-hours text (spec §7.3), display only. The game has just written its prompt into UseText.useText; we
    // only add to the two texts we can recognise for certain and keep everything else (a key, a lockpick, "(Cannot be
    // lockpicked)", a custom lock message, a bypassed lock, a door the clock hasn't flipped yet) as the game wrote it.
    // The open/closed word always follows the live lock; the times follow the schedule. The prompt runs every frame
    // while the player aims, so each target caches (text in, game minute, lock) -> text out.
    internal static class HoursText
    {
        private struct Entry
        {
            internal string In, Out;
            internal int Minute;
            internal bool Locked;
        }

        private static readonly Dictionary<int, Entry> Cache = new Dictionary<int, Entry>();

        internal static void Clear() => Cache.Clear();

        internal static void Door(Object door, Lock lck)
        {
            if (door == null || lck == null) return;
            Apply(door, lck.Locked, true);
        }

        internal static void Stall(Trade trade)
        {
            if (trade == null) return;
            Apply(trade, false, false);
        }

        private static void Apply(Object target, bool locked, bool isDoor)
        {
            var ut = UseText.instance;
            if (ut == null || ut.useText == null) return;
            if (Vanilla.UseTextOverridden != null && Vanilla.UseTextOverridden(ut)) return;
            var tod = TimeOfDayAzure.instance;
            if (tod == null || tod.updateTimeDisabled || tod.currentTimeAndDay == null) return;
            string t = ut.useText.text;
            if (string.IsNullOrEmpty(t)) return;
            var s = HoursIndex.For(target);
            if (s == null) return;

            int minute = Game.WeekMinute;
            int id = target.GetInstanceID();
            if (Cache.TryGetValue(id, out var e) && e.In == t && e.Minute == minute && e.Locked == locked)
            {
                if (e.Out != t) ut.useText.text = e.Out;
                return;
            }

            int day = tod.currentTimeAndDay.weekDay, hour = (int)tod.currentTimeAndDay.hours;
            string o = isDoor ? DoorText(t, s, locked, day, hour) : StallText(t, s, day, hour);
            Cache[id] = new Entry { In = t, Out = o ?? t, Minute = minute, Locked = locked };
            if (o != null) ut.useText.text = o;
        }

        // Unlocked door, schedule open, text "(...)": "(Enter, open until 21:00)". An in-scene door (H2, SRC/Door.cs:845-867)
        // prompts "(Open)" while shut and "(Close)" while standing open: "(Open, open until ...)" and "(Close, open until ...)"
        // would read as if the shop were closing, so "(Open)" gets the hours as a separate part, "(Open) · open until
        // 21:00", and "(Close)" and "(Unlock)" stay as the game wrote them.
        // Locked door, schedule closed, text exactly "(Locked)": "(Closed, opens 9:00)" and the variants below.
        private static string DoorText(string t, Schedule s, bool locked, int day, int hour)
        {
            bool open = s.IsOpen(day, hour);
            var week = Cfg.Hot.WeekLine;
            if (!locked && open)
            {
                if (t.Length < 3 || t[0] != '(' || t[t.Length - 1] != ')' || t == "(Unlock)" || t == "(Close)") return null;
                string part = OpenPart(s, day, hour);
                string r = t == "(Open)" ? t + Strings.HoursSeparator + part.Substring(1) : t.Substring(0, t.Length - 1) + part + ")";
                return week == Cfg.WeekLineMode.Always ? r + WeekLine(s) : r;
            }
            if (locked && !open)
            {
                if (t != "(Locked)") return null;
                string r = ClosedText(s, day, hour);
                return week != Cfg.WeekLineMode.Never ? r + WeekLine(s) : r;
            }
            return null;
        }

        // Only while the schedule says open: closed stalls hide their trader, so there is nothing to label then.
        private static string StallText(string t, Schedule s, int day, int hour)
        {
            if (!s.IsOpen(day, hour)) return null;
            string part = OpenPart(s, day, hour);
            if (t.Length >= 2 && t[t.Length - 1] == ')' && t.IndexOf('(') >= 0) return t.Substring(0, t.Length - 1) + part + ")";
            return t + " (" + part.Substring(2) + ")";
        }

        private static string OpenPart(Schedule s, int day, int hour)
        {
            var f = s.Next(day, hour);
            if (f == null || f.Value.After >= 24) return Strings.OpenAllDay;
            int closeHour = f.Value.Hour == 0 ? 24 : f.Value.Hour;
            return Strings.OpenUntil(Fmt.Clock(closeHour));
        }

        // Opening later today: "(Closed, opens 9:00)". After a normal day's hours, opening within 24 h: the same, as
        // "9:00" then plainly means tomorrow morning. Otherwise the day is named, and "Closed today" when the shop has no
        // hours at all today. Never opens: "(Closed)".
        private static string ClosedText(Schedule s, int day, int hour)
        {
            var f = s.Next(day, hour);
            if (f == null) return Strings.ClosedNoHours;
            var n = f.Value;
            bool hoursToday = s.AnyOpenOn(day);
            string clock = Fmt.Clock(n.Hour);
            if (n.Day == day && n.After < 24) return Strings.ClosedOpens(clock);
            if (hoursToday && n.After < 24) return Strings.ClosedOpens(clock);
            string when = Fmt.Day(n.Day) + " " + clock;
            return hoursToday ? Strings.ClosedOpens(when) : Strings.ClosedTodayOpens(when);
        }

        private static string WeekLine(Schedule s) => "\n<size=80%>" + s.Week() + "</size>";
    }
}
