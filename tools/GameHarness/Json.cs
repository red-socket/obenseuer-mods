using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;

namespace GameHarness
{
    // Minimal JSON writer for replies: dictionaries, lists, strings, numbers, bools, null and Vector3 (as [x,y,z]).
    internal static class Json
    {
        internal static string Ok(Dictionary<string, object> body = null)
        {
            var d = body ?? new Dictionary<string, object>();
            d["ok"] = true;
            return Write(d);
        }

        internal static string Error(string message) => Write(new Dictionary<string, object> { { "ok", false }, { "error", message } });

        internal static string Write(object o)
        {
            var sb = new StringBuilder();
            Append(sb, o);
            return sb.ToString();
        }

        private static void Append(StringBuilder sb, object o)
        {
            switch (o)
            {
                case null: sb.Append("null"); break;
                case string s: Str(sb, s); break;
                case bool b: sb.Append(b ? "true" : "false"); break;
                case float f: Num(sb, f); break;
                case double d: Num(sb, d); break;
                case int or long or uint or short or byte: sb.Append(Convert.ToString(o, CultureInfo.InvariantCulture)); break;
                case Vector3 v:
                    sb.Append('['); Num(sb, v.x); sb.Append(','); Num(sb, v.y); sb.Append(','); Num(sb, v.z); sb.Append(']');
                    break;
                case IDictionary<string, object> map:
                    sb.Append('{');
                    bool first = true;
                    foreach (var kv in map)
                    {
                        if (!first) sb.Append(',');
                        first = false;
                        Str(sb, kv.Key);
                        sb.Append(':');
                        Append(sb, kv.Value);
                    }
                    sb.Append('}');
                    break;
                case IEnumerable list:
                    sb.Append('[');
                    bool firstItem = true;
                    foreach (var item in list)
                    {
                        if (!firstItem) sb.Append(',');
                        firstItem = false;
                        Append(sb, item);
                    }
                    sb.Append(']');
                    break;
                default: Str(sb, o.ToString()); break;
            }
        }

        private static void Num(StringBuilder sb, double d)
        {
            if (double.IsNaN(d) || double.IsInfinity(d)) sb.Append("null");
            else sb.Append(Math.Round(d, 3).ToString("R", CultureInfo.InvariantCulture));
        }

        private static void Str(StringBuilder sb, string s)
        {
            sb.Append('"');
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
        }
    }
}
