using System.Collections;
using UnityEngine;

namespace BetterPlacement
{
    // Physics step counter (spec §3.6). A plugin coroutine resumes after each physics step's simulation and trigger
    // callbacks (Unity's WaitForFixedUpdate), so Count increases exactly when InsideTrigger and ObjectPreview have seen
    // the preview's latest pose. The settle guard compares it with the step of the last snap change.
    internal static class Steps
    {
        internal static int Count;

        internal static IEnumerator Run()
        {
            var wait = new WaitForFixedUpdate();
            while (true)
            {
                yield return wait;
                Count++;
            }
        }
    }
}
