using System;
using UnityEngine;

namespace TimeKeeper
{
    // When waiting may open and start (spec §6.6). CheckOpen runs every row; CheckStart repeats the rows that can change
    // while the picker is open (time keeps running there). The first match wins. A row that throws blocks with the
    // generic note and is logged once. Refusing never costs anything: the player keeps playing.
    internal static class WaitGate
    {
        internal const string TimeStopped = "Time can't pass right now.";
        internal const string InJail = "You can't wait in jail.";
        internal const string Police = "Not with the police after you.";
        internal const string InDanger = "Not while you're in danger.";
        internal const string OnTrain = "Not while riding the minitrain.";
        internal const string InWater = "Not in the water.";
        internal const string OnLadder = "Not on a ladder.";
        internal const string SitDown = "Sit down first.";
        internal const string NeedClock = "You need a watch or a clock.";
        internal const string TooDrunk = "Too drunk to wait.";

        // The game's own need texts read "Too low Health" and "Too low mental health" (SRC/PlayerStatsTiredness.cs:169),
        // so the mod words its refusals and stop notes itself.
        internal static string RefuseText(PlayerStatsTiredness.Reason r)
        {
            switch (r)
            {
                case PlayerStatsTiredness.Reason.Health: return "Your health is too low to wait.";
                case PlayerStatsTiredness.Reason.Hunger: return "Too hungry to wait.";
                case PlayerStatsTiredness.Reason.Thirst: return "Too thirsty to wait.";
                case PlayerStatsTiredness.Reason.MentalHealth: return "Your mental health is too low to wait.";
                case PlayerStatsTiredness.Reason.Tiredness: return "Too tired to wait.";
                default: return PlayGate.CantWait;
            }
        }

        internal static string StopText(PlayerStatsTiredness.Reason r)
        {
            switch (r)
            {
                case PlayerStatsTiredness.Reason.Health: return "Stopped waiting: your health is too low.";
                case PlayerStatsTiredness.Reason.Hunger: return "Stopped waiting: you're too hungry.";
                case PlayerStatsTiredness.Reason.Thirst: return "Stopped waiting: you're too thirsty.";
                case PlayerStatsTiredness.Reason.MentalHealth: return "Stopped waiting: your mental health is too low.";
                case PlayerStatsTiredness.Reason.Tiredness: return "Stopped waiting: you're too tired.";
                default: return "Stopped waiting.";
            }
        }

        internal const string StoppedDrunk = "Stopped waiting: you're too drunk.";

        private static Prison _prison;
        private static bool _prisonSearched;

        internal static GateResult CheckOpen(bool fromPad)
        {
            try
            {
                var r = PlayGate.Core();
                if (r.Blocked) return r;
                r = PlayGate.Free(fromPad);
                if (r.Blocked) return r;
                return Rows(start: false);
            }
            catch (Exception e)
            {
                Log.Exception("wait gate (open)", e);
                Log.WarnOnce("gate threw", "Wait: a gate check failed; waiting is refused while it does (see the error above).");
                return GateResult.Note(PlayGate.CantWait);
            }
        }

        internal static GateResult CheckStart()
        {
            try
            {
                var r = PlayGate.Core();
                if (r.Blocked) return r;
                if (PlayGate.InDialogue) return GateResult.Note(PlayGate.CantWait);
                return Rows(start: true);
            }
            catch (Exception e)
            {
                Log.Exception("wait gate (start)", e);
                Log.WarnOnce("gate threw", "Wait: a gate check failed; waiting is refused while it does (see the error above).");
                return GateResult.Note(PlayGate.CantWait);
            }
        }

        // Rows 5-18. The rows marked "open only" in the spec cannot change while the picker holds the player in place.
        private static GateResult Rows(bool start)
        {
            var wc = WaitingController.instance;
            var stats = PlayerStats.instance;

            // 5. Time can't pass.
            if (TimeOfDayAzure.instance.updateTimeDisabled || stats.StatsProgressDisabled || !wc.CanStartWaitingStaus)
                return GateResult.Note(TimeStopped);
            // 6. Card game and other blockers.
            if (WaitingController.WaitingBlocked) return GateResult.Note(PlayGate.CantWait);
            // 7. An arrest is under way: the game handles it.
            if (Crime.instance != null && Crime.instance.ArrestInProgress) return GateResult.Silent("arrest");
            // 8. Jail.
            if (!Cfg.Wait.AllowInJail.Value && InJailNow()) return GateResult.Note(InJail);
            // 9, 10. Police pursuit, robbers, recent damage.
            var threat = Danger.ScanNpcs();
            if (threat == Danger.Threat.Police) return GateResult.Note(Police);
            if (threat == Danger.Threat.Robber || Danger.RecentlyHurt()) return GateResult.Note(InDanger);

            if (!start)
            {
                // 11-13. Places where the game's own controls already hold the player.
                var train = TrainController.instance;
                if (train != null && train.UsingMiniTrain()) return GateResult.Note(OnTrain);
                var water = WaterController.instance;
                if (water != null && (water.PlayerIsSwimming || water.inDeepWater)) return GateResult.Note(InWater);
                if (OnLadderNow()) return GateResult.Note(OnLadder);
            }

            // 14, 15. Optional strictness.
            if (Cfg.Wait.RequireSeated.Value && !InteractableChair.PlayerIsSitting) return GateResult.Note(SitDown);
            if (Cfg.Wait.RequireClock.Value && (Inventory.instance == null || !Inventory.instance.CheckForCategoryItem("Clock", ignoreBackpack: true)))
                return GateResult.Note(NeedClock);

            // 16-18. Needs that would end the wait at once (CheckEvent, SRC/WaitingController.cs:176-185), and the
            // tiredness and alcohol levels at which our wait stops or the game passes the player out.
            var reason = stats.tiredness.CanSleep(waiting: true);
            if (reason != PlayerStatsTiredness.Reason.None) return GateResult.Note(RefuseText(reason));
            if (TooTired()) return GateResult.Note(RefuseText(PlayerStatsTiredness.Reason.Tiredness));
            if (TooDrunkNow()) return GateResult.Note(TooDrunk);
            return GateResult.Clear;
        }

        // Tiredness over 90: CanSleep then returns None (SRC/PlayerStatsTiredness.cs:138), which switches off every
        // vanilla need wake-up, and a wait would slide into a pass-out.
        internal static bool TooTired() => PlayerStats.instance.tiredness.currentStatus > 90f;

        // Alcohol in the blood over 95 (close to DrunkEffect's pass-out at 100, SRC/DrunkEffect.cs:108), or blood plus
        // stomach over 100. Drink in the stomach keeps moving into the blood with nothing burnt meanwhile
        // (SRC/PlayerStatsAlcohol.cs:21-29), so the second case is a pass-out already on its way. A pass-out during
        // our wait would inherit its alarm (SRC/WaitingController.cs:219-223 never clears alarmEnabled) and end early.
        internal static bool TooDrunkNow()
        {
            var a = PlayerStats.instance.alcohol;
            return a.AlcoholInBlood > 95f || a.AlcoholInBlood + a.AlcoholDrunk > 100f;
        }

        // In a jail cell. Without the private field, any Prison in the scene counts (fails closed).
        private static bool InJailNow()
        {
            var p = CurrentPrison();
            if (p == null) return false;
            if (Vanilla.InPrison == null)
            {
                Log.WarnOnce("prison field", "Wait: the prison state could not be read; waiting is refused wherever there is a jail unless AllowInJail is on.");
                return true;
            }
            return Vanilla.InPrison(p);
        }

        private static Prison CurrentPrison()
        {
            if (!_prisonSearched || (_prison is object && _prison == null))
            {
                _prisonSearched = true;
                _prison = UnityEngine.Object.FindAnyObjectByType<Prison>();
            }
            return _prison;
        }

        private static bool OnLadderNow()
        {
            if (Vanilla.PlayerClimbing == null) return false;
            return Vanilla.PlayerClimbing.GetValue(null) is bool b && b;
        }

        // Scene load: the cached Prison belongs to the old scene.
        internal static void OnSceneLoaded()
        {
            _prison = null;
            _prisonSearched = false;
        }
    }
}
