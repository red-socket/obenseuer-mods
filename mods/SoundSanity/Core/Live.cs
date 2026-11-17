using System;

namespace SoundSanity
{
    // Live updates (spec §3.5). Triggers (config changes, the Music slider, VolumeSave.GetVolume, routing toggles) only set
    // flags; Plugin.Update runs each dirty pass at most once per frame, so dragging a slider costs one pass per frame.
    internal static class Live
    {
        [Flags]
        internal enum Pass { None = 0, Routing = 1, Machines = 2, Radios = 4, Loops = 8 }

        private static Pass _dirty;

        internal static void MarkDirty(Pass p) => _dirty |= p;

        internal static void Pump()
        {
            if (_dirty == Pass.None) return;
            var d = _dirty;
            _dirty = Pass.None;
            if ((d & Pass.Routing) != 0) Plugin.Step("live routing", Router.OnToggle);
            if ((d & Pass.Machines) != 0) Plugin.Step("live machines", Stations.LivePass);
            if ((d & Pass.Radios) != 0) Plugin.Step("live radios", Radios.LivePass);
            if ((d & Pass.Loops) != 0) Plugin.Step("live loops", Loops.LivePass);
        }
    }
}
