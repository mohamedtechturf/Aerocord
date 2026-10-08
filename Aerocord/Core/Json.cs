using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Aerocord.Core
{
    public static class Json
    {
        public static object Parse(string text)
        {
            if (string.IsNullOrEmpty(text)) return null;
            int pos = 0;
            SkipWhitespace(text, ref pos);
            object result = ParseValue(text, ref pos);
            return result;
        }

        public static JNode ParseNode(string text)
        {
            return new JNode(Parse(text));
        }

        private static object ParseValue(string s, ref int i)
        {
            SkipWhitespace(s, ref i);
            if (i >= s.Length) throw new FormatException("Unexpected end of JSON");
            char c = s[i];
            switch (c)
            {
                case '{': return ParseObject(s, ref i);
                case '[': return ParseArray(s, ref i);
                case '"': return ParseString(s, ref i);
                case 't':
                    Expect(s, ref i, "true");
                    return true;
                case 'f':
                    Expect(s, ref i, "false");
                    return false;
                case 'n':
                    Expect(s, ref i, "null");
                    return null;
                default:
                    return ParseNumber(s, ref i);
            }
        }

        private static void Expect(string s, ref int i, string literal)
        {
            if (i + literal.Length > s.Length || s.Substring(i, literal.Length) != literal)
                throw new FormatException("Invalid JSON literal near position " + i);
            i += literal.Length;
        }

        private static Dictionary<string, object> ParseObject(string s, ref int i)
        {
            var dict = new Dictionary<string, object>();
            i++; // consume '{'
            SkipWhitespace(s, ref i);
            if (i < s.Length && s[i] == '}') { i++; return dict; }
            while (true)
            {
                SkipWhitespace(s, ref i);
                string key = ParseString(s, ref i);
                SkipWhitespace(s, ref i);
                if (s[i] != ':') throw new FormatException("Expected ':' at position " + i);
                i++;
                object val = ParseValue(s, ref i);
                dict[key] = val;
                SkipWhitespace(s, ref i);
                if (i >= s.Length) throw new FormatException("Unexpected end of object");
                if (s[i] == ',') { i++; continue; }
                if (s[i] == '}') { i++; break; }
                throw new FormatException("Expected ',' or '}' at position " + i);
            }
            return dict;
        }

        private static List<object> ParseArray(string s, ref int i)
        {
            var list = new List<object>();
            i++; // consume '['
            SkipWhitespace(s, ref i);
            if (i < s.Length && s[i] == ']') { i++; return list; }
            while (true)
            {
                object val = ParseValue(s, ref i);
                list.Add(val);
                SkipWhitespace(s, ref i);
                if (i >= s.Length) throw new FormatException("Unexpected end of array");
                if (s[i] == ',') { i++; continue; }
                if (s[i] == ']') { i++; break; }
                throw new FormatException("Expected ',' or ']' at position " + i);
            }
            return list;
        }

        private static string ParseString(string s, ref int i)
        {
            if (s[i] != '"') throw new FormatException("Expected '\"' at position " + i);
            i++;
            var sb = new StringBuilder();
            while (true)
            {
                if (i >= s.Length) throw new FormatException("Unterminated string");
                char c = s[i++];
                if (c == '"') break;
                if (c == '\\')
                {
                    if (i >= s.Length) throw new FormatException("Unterminated escape");
                    char e = s[i++];
                    switch (e)
                    {
                        case '"': sb.Append('"'); break;
                        case '\\': sb.Append('\\'); break;
                        case '/': sb.Append('/'); break;
                        case 'b': sb.Append('\b'); break;
                        case 'f': sb.Append('\f'); break;
                        case 'n': sb.Append('\n'); break;
                        case 'r': sb.Append('\r'); break;
                        case 't': sb.Append('\t'); break;
                        case 'u':
                            string hex = s.Substring(i, 4);
                            i += 4;
                            sb.Append((char)int.Parse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                            break;
                        default:
                            throw new FormatException("Invalid escape sequence \\" + e);
                    }
                }
                else
                {
                    sb.Append(c);
                }
            }
            return sb.ToString();
        }

        private static double ParseNumber(string s, ref int i)
        {
            int start = i;
            if (i < s.Length && (s[i] == '-' || s[i] == '+')) i++;
            while (i < s.Length && char.IsDigit(s[i])) i++;
            if (i < s.Length && s[i] == '.')
            {
                i++;
                while (i < s.Length && char.IsDigit(s[i])) i++;
            }
            if (i < s.Length && (s[i] == 'e' || s[i] == 'E'))
            {
                i++;
                if (i < s.Length && (s[i] == '-' || s[i] == '+')) i++;
                while (i < s.Length && char.IsDigit(s[i])) i++;
            }
            string numStr = s.Substring(start, i - start);
            return double.Parse(numStr, CultureInfo.InvariantCulture);
        }

        private static void SkipWhitespace(string s, ref int i)
        {
            while (i < s.Length && char.IsWhiteSpace(s[i])) i++;
        }

        public static string Write(object value)
        {
            var sb = new StringBuilder();
            WriteValue(sb, value);
            return sb.ToString();
        }

        private static void WriteValue(StringBuilder sb, object value)
        {
            if (value == null) { sb.Append("null"); return; }

            if (value is string s) { WriteString(sb, s); return; }
            if (value is bool b) { sb.Append(b ? "true" : "false"); return; }

            if (value is double d) { WriteNumber(sb, d); return; }
            if (value is float f) { WriteNumber(sb, f); return; }
            if (value is int || value is long || value is short)
            {
                sb.Append(Convert.ToInt64(value).ToString(CultureInfo.InvariantCulture));
                return;
            }

            if (value is IDictionary<string, object> dict)
            {
                sb.Append('{');
                bool first = true;
                foreach (var kv in dict)
                {
                    if (!first) sb.Append(',');
                    first = false;
                    WriteString(sb, kv.Key);
                    sb.Append(':');
                    WriteValue(sb, kv.Value);
                }
                sb.Append('}');
                return;
            }

            if (value is System.Collections.IEnumerable list)
            {
                sb.Append('[');
                bool first = true;
                foreach (var item in list)
                {
                    if (!first) sb.Append(',');
                    first = false;
                    WriteValue(sb, item);
                }
                sb.Append(']');
                return;
            }

            // Fallback: treat as string
            WriteString(sb, value.ToString());
        }

        private static void WriteNumber(StringBuilder sb, double d)
        {
            if (d == Math.Floor(d) && !double.IsInfinity(d) && Math.Abs(d) < 1e15)
            {
                sb.Append(((long)d).ToString(CultureInfo.InvariantCulture));
            }
            else
            {
                sb.Append(d.ToString("R", CultureInfo.InvariantCulture));
            }
        }

        private static void WriteString(StringBuilder sb, string s)
        {
            sb.Append('"');
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < ' ')
                            sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        else
                            sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
        }
    }

    public struct JNode
    {
        private readonly object _value;

        public JNode(object value) { _value = value; }

        public bool IsNull { get { return _value == null; } }

        public JNode this[string key]
        {
            get
            {
                var dict = _value as IDictionary<string, object>;
                if (dict == null) return new JNode(null);
                object v;
                return dict.TryGetValue(key, out v) ? new JNode(v) : new JNode(null);
            }
        }

        public JNode this[int index]
        {
            get
            {
                var list = _value as List<object>;
                if (list == null || index < 0 || index >= list.Count) return new JNode(null);
                return new JNode(list[index]);
            }
        }

        public bool Has(string key)
        {
            var dict = _value as IDictionary<string, object>;
            return dict != null && dict.ContainsKey(key);
        }

        public IEnumerable<JNode> Items()
        {
            var list = _value as List<object>;
            if (list == null) yield break;
            foreach (var item in list) yield return new JNode(item);
        }

        public int Count
        {
            get
            {
                var list = _value as List<object>;
                return list == null ? 0 : list.Count;
            }
        }

        public string AsString(string def = null)
        {
            if (_value == null) return def;
            if (_value is string s) return s;
            if (_value is double d) return Json.Write(d);
            if (_value is bool b) return b ? "true" : "false";
            return def;
        }

        public long AsLong(long def = 0)
        {
            if (_value is double d) return (long)d;
            if (_value is string s)
            {
                long parsed;
                if (long.TryParse(s, out parsed)) return parsed;
            }
            return def;
        }

        public double AsDouble(double def = 0)
        {
            if (_value is double d) return d;
            if (_value is string s)
            {
                double parsed;
                if (double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out parsed)) return parsed;
            }
            return def;
        }

        public bool AsBool(bool def = false)
        {
            if (_value is bool b) return b;
            return def;
        }

        public object Raw { get { return _value; } }
    }
}
