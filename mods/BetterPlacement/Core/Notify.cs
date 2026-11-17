using System;

namespace BetterPlacement
{
    // The game's own notification with our title, for key collision notes (spec §5.2).
    internal static class Notify
    {
        internal static void Post(string text)
        {
            try
            {
                if (Notifications.instance != null) Notifications.instance.CreateNotification(Strings.Title, text);
            }
            catch (Exception e) { Log.Exception("notification", e); }
        }
    }
}
