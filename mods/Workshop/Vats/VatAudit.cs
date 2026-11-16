using System;
using UnityEngine;

namespace Workshop
{
    // LoadingDone, log only (spec §6.4): vessels holding more stacks than their authored slot count. With Bigger Vats off
    // they keep the extra slots until emptied (ResizeLoadedSlots only drops empty ones), and Workshop's layout still shows
    // all of them.
    internal static class VatAudit
    {
        internal static void Run()
        {
            foreach (var p in UnityEngine.Object.FindObjectsByType<manufacturingProcess>(FindObjectsSortMode.None))
            {
                try
                {
                    var s = p != null ? p.InputOutputStorage : null;
                    if (s == null || s.storageSlots == null) continue;
                    int authored = VatSlots.AuthoredOf(s);
                    int used = 0;
                    foreach (var st in s.storageSlots)
                        if (st != null && !st.IsEmpty()) used++;
                    if (used <= authored) continue;
                    string msg = $"Vats: {Stations.Name(p)} ({p.gameObject.scene.name}) holds {used} stacks in {s.storageSlots.Length} slots (authored {authored}).";
                    if (Cfg.BiggerVats.Enabled.Value) Log.Info(msg);
                    else Log.Warn(msg + $" It keeps {used} slots until emptied; Workshop's layout shows all of them.");
                }
                catch (Exception e) { Log.Exception("vat audit", e); }
            }
        }
    }
}
