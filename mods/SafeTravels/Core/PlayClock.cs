using UnityEngine;

namespace SafeTravels
{
    // Seconds of play: real time that only runs while the game is not paused. Spot ages and the Unstuck cooldown use it,
    // so sitting in the pause menu neither ages the trail nor runs the cooldown down. Ticked first in Plugin.Update.
    internal static class PlayClock
    {
        internal static float Now { get; private set; }

        internal static void Tick()
        {
            if (!Game.Paused) Now += Time.unscaledDeltaTime;
        }
    }
}
