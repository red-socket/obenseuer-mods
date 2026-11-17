using System.Globalization;
using System.Text;

namespace FairMarket
{
    // Name folding for config matching (spec §5.2, §6.2): trimmed, accents removed (FormD minus combining marks, so
    // "Ari Pulsunmäki" matches "Ari Pulsunmaki" and "Irma Kött" matches "Irma Kott"), lower case invariant.
    internal static class Names
    {
        internal static string Fold(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            string d = s.Trim().Normalize(NormalizationForm.FormD);
            var sb = new StringBuilder(d.Length);
            foreach (char ch in d)
                if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark) sb.Append(ch);
            return sb.ToString().Normalize(NormalizationForm.FormC).ToLowerInvariant();
        }

        // Splits a comma-separated config list, dropping empty entries.
        internal static string[] List(string s)
        {
            if (string.IsNullOrEmpty(s)) return new string[0];
            var parts = s.Split(',');
            var l = new System.Collections.Generic.List<string>(parts.Length);
            foreach (var p in parts)
            {
                string t = p.Trim();
                if (t.Length > 0) l.Add(t);
            }
            return l.ToArray();
        }
    }
}
