using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace SaveGuard
{
    // key=value text files (UTF-8, no BOM) for journals, state.txt and entry.txt.
    // A file only counts when its last line is "end=1", so a torn write is never mistaken for a complete one.
    internal sealed class Kv
    {
        private readonly List<string> _order = new List<string>();
        private readonly Dictionary<string, string> _values = new Dictionary<string, string>(StringComparer.Ordinal);

        internal string this[string key]
        {
            get => _values.TryGetValue(key, out var v) ? v : null;
            set
            {
                if (key == "end") return;
                if (!_values.ContainsKey(key)) _order.Add(key);
                _values[key] = value ?? "";
            }
        }

        internal bool Has(string key) => _values.ContainsKey(key);

        internal int Int(string key, int fallback = 0) => int.TryParse(this[key], out var n) ? n : fallback;
        internal long Long(string key, long fallback = 0) => long.TryParse(this[key], out var n) ? n : fallback;
        internal bool Bool(string key) => this[key] == "1";

        internal Kv Clone()
        {
            var k = new Kv();
            foreach (var key in _order) k[key] = _values[key];
            return k;
        }

        internal string Serialize()
        {
            var sb = new StringBuilder();
            foreach (var key in _order)
                sb.Append(key).Append('=').Append(Escape(_values[key])).Append('\n');
            sb.Append("end=1\n");
            return sb.ToString();
        }

        internal static Kv Parse(string text)
        {
            var kv = new Kv();
            bool ended = false;
            foreach (var raw in text.Split('\n'))
            {
                string line = raw.TrimEnd('\r');
                if (line.Length == 0) continue;
                int eq = line.IndexOf('=');
                if (eq <= 0) continue;
                string key = line.Substring(0, eq);
                string value = Unescape(line.Substring(eq + 1));
                if (key == "end") { ended = value == "1"; continue; }
                kv[key] = value;
                ended = false;   // "end=1" must be the last entry
            }
            return ended ? kv : null;
        }

        private static string Escape(string s) => s.Replace("\\", "\\\\").Replace("\n", "\\n").Replace("\r", "\\r");

        private static string Unescape(string s)
        {
            if (s.IndexOf('\\') < 0) return s;
            var sb = new StringBuilder(s.Length);
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c == '\\' && i + 1 < s.Length)
                {
                    char n = s[++i];
                    sb.Append(n == 'n' ? '\n' : n == 'r' ? '\r' : n);
                }
                else sb.Append(c);
            }
            return sb.ToString();
        }
    }

    internal static class KvFile
    {
        private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(false);

        // Reads path; if it is missing (killed between WriteAtomic's delete and move), falls back to path.tmp.
        internal static Kv Read(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    var kv = Kv.Parse(File.ReadAllText(path, Utf8NoBom));
                    if (kv != null) return kv;
                }
                string tmp = path + ".tmp";
                if (File.Exists(tmp)) return Kv.Parse(File.ReadAllText(tmp, Utf8NoBom));
            }
            catch (Exception e) { Log.Exception("KvFile.Read " + Path.GetFileName(path), e); }
            return null;
        }

        // tmp, Flush(true), delete destination, move. A reader sees the old file, the tmp, or the new file.
        internal static void WriteAtomic(string path, Kv kv)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string tmp = path + ".tmp";
            byte[] bytes = Utf8NoBom.GetBytes(kv.Serialize());
            using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                fs.Write(bytes, 0, bytes.Length);
                fs.Flush(true);
            }
            if (File.Exists(path)) File.Delete(path);
            File.Move(tmp, path);
        }

        internal static void Delete(string path)
        {
            if (File.Exists(path)) File.Delete(path);
            if (File.Exists(path + ".tmp")) File.Delete(path + ".tmp");
        }
    }
}
