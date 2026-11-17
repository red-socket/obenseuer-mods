using System.Collections.Generic;
using UnityEngine;

namespace FairMarket
{
    // A weekly schedule in whole hours (spec §7.1). Days are the game's weekDay (0 = Monday). Both kinds copy the
    // game's own test exactly and snapshot their data when the scene index is built, so Next can be cached.
    internal abstract class Schedule
    {
        internal struct Flip
        {
            internal int Day, Hour, After;   // the hour the state changes, and how many hours from now
        }

        private readonly Dictionary<int, Flip?> _next = new Dictionary<int, Flip?>();
        private string _week;

        internal abstract bool IsOpen(int day, int hour);

        internal string Kind;    // "RelayWeekdays", "DoorSchedule", "Trade" (for the log)
        internal string Source;  // the source GameObject's path (for the log)

        internal bool AnyOpenOn(int day)
        {
            for (int h = 0; h < 24; h++) if (IsOpen(day, h)) return true;
            return false;
        }

        // The next hour at which the open/closed state differs from now, within a week; null if it never changes.
        internal Flip? Next(int day, int hour)
        {
            int key = day * 24 + hour;
            if (_next.TryGetValue(key, out var cached)) return cached;
            bool now = IsOpen(day, hour);
            Flip? found = null;
            int d = day, h = hour;
            for (int k = 1; k <= 168; k++)
            {
                if (++h >= 24) { h = 0; d = (d + 1) % 7; }
                if (IsOpen(d, h) != now)
                {
                    found = new Flip { Day = d, Hour = h, After = k };
                    break;
                }
            }
            _next[key] = found;
            return found;
        }

        // "Mon-Fri 8-16, Sat 10-16, Sun closed" or "Daily 9-21" (spec §7.3). Runs inside one day; several runs on a day
        // are joined with ", ".
        internal string Week()
        {
            if (_week != null) return _week;
            var days = new string[7];
            for (int d = 0; d < 7; d++) days[d] = DayRuns(d);
            bool allSame = true;
            for (int d = 1; d < 7; d++) if (days[d] != days[0]) allSame = false;
            if (allSame) return _week = days[0] == Strings.DayClosed ? Strings.ClosedAllWeek : Strings.Daily + " " + days[0];
            var parts = new List<string>();
            int start = 0;
            for (int d = 1; d <= 7; d++)
            {
                if (d < 7 && days[d] == days[start]) continue;
                string label = d - 1 == start ? Fmt.Day(start) : Fmt.Day(start) + "-" + Fmt.Day(d - 1);
                parts.Add(label + " " + days[start]);
                start = d;
            }
            return _week = string.Join(", ", parts.ToArray());
        }

        private string DayRuns(int day)
        {
            var runs = new List<string>();
            int h = 0;
            while (h < 24)
            {
                if (!IsOpen(day, h)) { h++; continue; }
                int a = h;
                while (h < 24 && IsOpen(day, h)) h++;
                runs.Add(Fmt.Hour(a) + "-" + Fmt.Hour(h));
            }
            return runs.Count == 0 ? Strings.DayClosed : string.Join(", ", runs.ToArray());
        }
    }

    // OpeningTimes.IsOpen / IsOpenHour (SRC/OpeningTimes.cs:56-91): opens == closes is closed all day; opens > closes is
    // open from opens to midnight and from midnight to closes on the same day's entry; otherwise opens <= h < closes.
    internal sealed class WeekdaysSchedule : Schedule
    {
        private readonly int[] _o = new int[7], _c = new int[7];

        internal WeekdaysSchedule(OpeningTimes t)
        {
            _o[0] = t.opensOnMonday; _c[0] = t.closesOnMonday;
            _o[1] = t.opensOnTuesday; _c[1] = t.closesOnTuesday;
            _o[2] = t.opensOnWednesday; _c[2] = t.closesOnWednesday;
            _o[3] = t.opensOnThursday; _c[3] = t.closesOnThursday;
            _o[4] = t.opensOnFriday; _c[4] = t.closesOnFriday;
            _o[5] = t.opensOnSaturday; _c[5] = t.closesOnSaturday;
            _o[6] = t.opensOnSunday; _c[6] = t.closesOnSunday;
        }

        internal override bool IsOpen(int day, int hour)
        {
            if (day < 0 || day > 6) return false;
            int o = _o[day], c = _c[day];
            if (o == c) return false;
            if (o > c) return hour >= o || hour < c;
            return hour >= o && hour < c;
        }
    }

    // DoorSchedule.CurrentTime (SRC/DoorSchedule.cs:40-100): open while any entry has x == day and y <= h < z. No
    // overnight windows; a day without an entry is closed.
    internal sealed class DoorScheduleSchedule : Schedule
    {
        private readonly Vector3Int[] _times;

        internal DoorScheduleSchedule(Vector3Int[] times) => _times = (Vector3Int[])times.Clone();

        internal override bool IsOpen(int day, int hour)
        {
            foreach (var e in _times)
                if (e.x == day && hour >= e.y && hour < e.z) return true;
            return false;
        }
    }
}
