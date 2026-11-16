namespace SafeTravels
{
    // Every string players see in game (spec §4.3), humanized 2026-11-16. Config descriptions live in Cfg.cs and key
    // collision notes in Keys.cs.
    internal static class Strings
    {
        internal const string TollPaid = "Paid - walk through";

        internal const string UnstuckButton = "Get Unstuck";
        internal const string RerailButton = "Rerail Train";
        internal static string WithCooldown(string label, int seconds) => $"{label} ({seconds} s)";

        internal const string Busy = "You can't do that right now.";
        internal const string Seated = "Get up first.";
        internal const string Ladder = "Get off the ladder first.";
        internal const string OffTrain = "Get off the train first.";
        internal const string Jail = "Not while you're in jail.";
        internal const string Police = "Not while the police are after you.";
        internal const string Falling = "Not while you're falling.";
        internal static string Cooldown(int seconds) => $"You can get unstuck again in {seconds} s.";
        internal static string RerailCooldown(int seconds) => $"You can rerail again in {seconds} s.";
        internal const string NoSpot = "There's no safe spot nearby.";
        internal const string Moved = "You're back where you last stood safely.";
        internal const string MovedEntrance = "You've been moved to the nearest entrance.";

        internal const string Rerailed = "The train is back on its rails.";
        internal const string RerailedUncoupled = "The train is back on its rails. Its wagons were uncoupled.";
        internal const string NoTrain = "There's no train nearby to rerail.";
        internal const string LookAtLoco = "Look at the locomotive to rerail it.";
        internal const string Moving = "The train is still moving.";
        internal const string EngineOn = "Turn the engine off first.";
        internal const string OnTrain = "Step off the train first.";
    }
}
