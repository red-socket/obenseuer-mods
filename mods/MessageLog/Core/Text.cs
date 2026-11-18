using System;
using System.Text.RegularExpressions;

namespace MessageLog
{
    // Text clean-up (spec §4.2, §5.3). Notification text is stored raw (the PT-BR lookup needs the exact English) and
    // cleaned only for display; dialogue text is stripped once, at capture, the way the Dialogue System strips it.
    internal static class Text
    {
        internal const int MaxChars = 1000;   // per stored head or text (D11)

        // Only TextMeshPro's own tags, so a literal "<3" or "<Unknown>" survives.
        private static readonly Regex TmpTags = new Regex(
            @"</?(b|i|u|s|color|size|alpha|mark|sup|sub|br|font|material|align|cspace|mspace|voffset|indent|line-height|nobr|noparse|allcaps|smallcaps|lowercase|uppercase|space|pos|margin|rotate|width|style|gradient|sprite|link)(=[^<>]*)?>",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        internal static string StripTags(string s) =>
            string.IsNullOrEmpty(s) || s.IndexOf('<') < 0 ? s ?? "" : TmpTags.Replace(s, "");

        // The Dialogue System's own clean-up (FP Tools.cs:253 and :262); our whitelist if it throws.
        internal static string StripDialogue(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            if (s.IndexOf('<') < 0) return s;
            try
            {
                return PixelCrushers.DialogueSystem.Tools.StripTextMeshProTags(PixelCrushers.DialogueSystem.Tools.StripRichTextCodes(s));
            }
            catch (Exception) { return StripTags(s); }
        }

        // One row: line breaks become " / " ("Rent paid" is two lines, SRC/TenementController.cs:432-443).
        internal static string OneLine(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            if (s.IndexOf('\n') < 0 && s.IndexOf('\r') < 0) return s;
            return s.Replace("\r\n", "\n").Replace('\r', '\n').Trim('\n').Replace("\n", " / ");
        }

        internal static string Cap(string s) => s == null ? "" : s.Length <= MaxChars ? s : s.Substring(0, MaxChars);
    }
}
