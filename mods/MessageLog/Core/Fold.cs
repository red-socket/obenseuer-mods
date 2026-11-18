using System.Globalization;
using System.Text;

namespace MessageLog
{
    // Case- and accent-insensitive folding for the search (spec §6.3): "Pätkä" and "patka" match. Decompose, drop the
    // combining marks, lower-case with the invariant culture.
    internal static class Fold
    {
        private static readonly StringBuilder Sb = new StringBuilder(64);

        internal static string Of(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            string d = s.Normalize(NormalizationForm.FormD);
            Sb.Length = 0;
            foreach (char ch in d)
                if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark) Sb.Append(ch);
            return Sb.ToString().ToLowerInvariant();
        }

        // The whitespace tokens of a folded query; empty for an empty query.
        internal static string[] Tokens(string query)
        {
            string f = Of(query);
            return f.Length == 0 ? new string[0] : f.Split(new[] { ' ', '\t' }, System.StringSplitOptions.RemoveEmptyEntries);
        }
    }
}
