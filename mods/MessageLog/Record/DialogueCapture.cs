using System;
using System.IO;
using System.Reflection;
using HarmonyLib;
using PixelCrushers.DialogueSystem;

namespace MessageLog
{
    // D1: DialogueSystemController.OnConversationLine(Subtitle) postfix (spec §4.2). The method raises
    // conversationLinePrepared, where the game's DialogueController.UseTalkerNameForEmptyActor names a speaker that has no
    // actor (SRC/DialogueController.cs:76-86), so the postfix sees the name the subtitle panel shows, "???" included.
    // Barks never come through here (FP BarkController.cs:123-143).
    //
    // The line is skipped exactly where ConversationView.ShouldShowSubtitle hides it (FP ConversationView.cs:334-356):
    // noSubtitle, a None() sequence, or a Continue() sequence while showSubtitleOnEmptyContinue is off. Player lines are
    // not shown as subtitles in this game, but they are the reply the player picked (D2), so they are logged as "You".
    // Strings are copied at once; no Dialogue System object is kept except the last subtitle for the duplicate check
    // (NotifyParticipants broadcasts to the speaker, the listener and the Dialogue Manager, which can reach us twice).
    [HarmonyPatch]
    internal static class DialogueCapture
    {
        private static Subtitle _last;

        private static MethodBase TargetMethod() => Vanilla.OnConversationLine;

        internal static void OnSceneLoaded() => _last = null;

        private static void Postfix(Subtitle subtitle)
        {
            try
            {
                if (!Cfg.Hot.Dialogue || subtitle == null || ReferenceEquals(subtitle, _last)) return;
                _last = subtitle;
                var ft = subtitle.formattedText;
                if (ft == null || ft.noSubtitle) return;
                string seq = subtitle.sequence;
                if (seq == "None()" || seq == "None();") return;
                if ((seq == "Continue()" || seq == "Continue();") && !EmptyContinueShown()) return;
                var info = subtitle.speakerInfo;
                bool pc = info != null && info.isPlayer;
                if (pc && !Cfg.Hot.PlayerChoices) return;
                string text = Text.StripDialogue(ft.text);
                if (text.Trim().Length == 0) return;
                // What the subtitle panel shows (FP StandardUISubtitlePanel.cs:738-740). Never nameInDatabase: it would
                // reveal names the game hides as "???".
                string who = pc ? "" : SafeFileName(info?.Name);
                History.AddDialogue(pc ? 'c' : 'd', who, Text.Cap(text));
            }
            catch (Exception e) { Log.Once("Dialogue: line", e); }
        }

        private static bool EmptyContinueShown()
        {
            try { return DialogueManager.displaySettings?.cameraSettings?.showSubtitleOnEmptyContinue ?? false; }
            catch (Exception) { return false; }
        }

        private static string SafeFileName(string name)
        {
            if (string.IsNullOrEmpty(name)) return "";
            try { return Path.GetFileName(name); }
            catch (Exception) { return name; }
        }
    }
}
