using System;
using UnityEngine.EventSystems;

namespace SaveGuard
{
    // While a Save Guard window has focus, the current EventSystem is disabled so clicks do not reach the
    // world-space menu underneath. Only an EventSystem that Save Guard disabled is ever re-enabled.
    internal static class InputBlock
    {
        private static EventSystem _disabled;

        internal static void Sync(bool block)
        {
            try
            {
                if (block)
                {
                    if (_disabled != null) return;
                    var es = EventSystem.current;
                    if (es == null || !es.enabled) return;
                    es.enabled = false;
                    _disabled = es;
                }
                else Release();
            }
            catch (Exception e) { Log.Exception("EventSystem toggle", e); }
        }

        internal static void Release()
        {
            try
            {
                if (_disabled != null) _disabled.enabled = true;   // destroyed with its scene: nothing to restore
            }
            catch (Exception e) { Log.Exception("EventSystem restore", e); }
            _disabled = null;
        }
    }
}
