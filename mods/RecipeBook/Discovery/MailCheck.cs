using System;
using UnityEngine;

namespace RecipeBook
{
    // D22: Recipe Book 0.1.0 does not re-roll mailed recipes. On 0.5.19 no random mail entry is of type Recipe (0 of
    // 260); subscriptions mail magazines, which the readable picks cover. This read-only check runs once at the first
    // gameplay scene and warns if a later game version starts mailing random recipes (spec §12.3, Appendix A).
    internal static class MailCheck
    {
        internal static void Run()
        {
            try
            {
                int subs = 0, senders = 0, recipe = 0;
                foreach (var s in Resources.FindObjectsOfTypeAll<PlayerMailSubscription>())
                {
                    if (s == null || s.mail == null) continue;
                    subs++;
                    foreach (var m in s.mail)
                        if (m != null && m.type == RandommailSender.Type.Recipe) recipe++;
                }
                foreach (var s in UnityEngine.Object.FindObjectsByType<RandommailSender>(FindObjectsSortMode.None))
                {
                    if (s == null || s.randomMail == null) continue;
                    senders++;
                    foreach (var m in s.randomMail)
                        if (m != null && m.type == RandommailSender.Type.Recipe) recipe++;
                }
                if (recipe > 0)
                    Log.Warn($"Mail: this game version mails random recipes ({recipe} entries); Recipe Book {Plugin.Version} does not re-roll them.");
                else
                    Log.Verbose($"Mail: no random recipe mail ({subs} subscriptions, {senders} senders in this scene).");
            }
            catch (Exception e) { Log.Exception("Mail: check", e); }
        }
    }
}
