using System;

namespace SoundSanity
{
    // One flag set per patch group (spec §5). Patched is set by Plugin.PatchGroup; the first exception in a patch body
    // logs an error with the stack and sets Broken, after which every body of that group returns at once (vanilla
    // behaviour) for the rest of the session. Groups fail independently.
    internal sealed class Group
    {
        internal readonly string Name;
        internal bool Patched, Broken;

        internal Group(string name) { Name = name; }

        internal bool On => Patched && !Broken;

        internal void Fail(Exception e)
        {
            if (Broken) return;
            Broken = true;
            Log.Error($"'{Name}' hit an error and is off for the rest of the session (the game's own behaviour applies): {e}");
        }
    }

    internal static class Groups
    {
        internal static readonly Group Routing = new Group("routing");
        internal static readonly Group Spawns = new Group("routing spawns");
        internal static readonly Group Characters = new Group("routing characters");
        internal static readonly Group AreaTracking = new Group("area tracking");
        internal static readonly Group GainCore = new Group("gain core");
        internal static readonly Group Machines = new Group("machines");
        internal static readonly Group Radios = new Group("radios");
        internal static readonly Group RadioChannel = new Group("radio channel");
        internal static readonly Group AmbientRadios = new Group("ambient radios");
        internal static readonly Group MusicSlider = new Group("music slider");
    }
}
