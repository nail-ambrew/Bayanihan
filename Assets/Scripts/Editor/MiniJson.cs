using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

/// <summary>
/// Reads JSON into plain objects: Dictionary&lt;string, object&gt;, List&lt;object&gt;, string, double, bool or null.
/// Enough for GeoJSON, which Unity's JsonUtility cannot read (its coordinates are arrays of arrays). Numbers are read
/// the same way whatever the machine's language settings, so a comma-decimal locale cannot misread a coordinate.
/// </summary>
internal static class MiniJson
{
    public static object Parse(string json)
    {
        var reader = new Reader(json);
        object value = reader.Value();
        reader.SkipSpace();
        if (!reader.AtEnd) throw reader.Error("unexpected text after the end");
        return value;
    }

    class Reader
    {
        readonly string s;
        int i;

        public Reader(string text) { s = text ?? ""; }

        public bool AtEnd => i >= s.Length;

        public FormatException Error(string what) => new FormatException(what + " at character " + i);

        public void SkipSpace()
        {
            while (i < s.Length && char.IsWhiteSpace(s[i])) i++;
        }

        char Peek()
        {
            SkipSpace();
            if (AtEnd) throw Error("unexpected end of the text");
            return s[i];
        }

        public object Value()
        {
            switch (Peek())
            {
                case '{': return Object();
                case '[': return Array();
                case '"': return String();
                case 't': Word("true"); return true;
                case 'f': Word("false"); return false;
                case 'n': Word("null"); return null;
                default: return Number();
            }
        }

        Dictionary<string, object> Object()
        {
            i++;
            var d = new Dictionary<string, object>();
            if (Peek() == '}') { i++; return d; }
            while (true)
            {
                if (Peek() != '"') throw Error("expected a name in quotes");
                string key = String();
                if (Peek() != ':') throw Error("expected ':'");
                i++;
                d[key] = Value();
                char c = Peek();
                i++;
                if (c == ',') continue;
                if (c == '}') return d;
                throw Error("expected ',' or '}'");
            }
        }

        List<object> Array()
        {
            i++;
            var list = new List<object>();
            if (Peek() == ']') { i++; return list; }
            while (true)
            {
                list.Add(Value());
                char c = Peek();
                i++;
                if (c == ',') continue;
                if (c == ']') return list;
                throw Error("expected ',' or ']'");
            }
        }

        string String()
        {
            i++;
            var sb = new StringBuilder();
            while (true)
            {
                if (AtEnd) throw Error("a string is not closed");
                char c = s[i++];
                if (c == '"') return sb.ToString();
                if (c != '\\') { sb.Append(c); continue; }
                if (AtEnd) throw Error("a string is not closed");
                char e = s[i++];
                switch (e)
                {
                    case '"':  sb.Append('"'); break;
                    case '\\': sb.Append('\\'); break;
                    case '/':  sb.Append('/'); break;
                    case 'b':  sb.Append('\b'); break;
                    case 'f':  sb.Append('\f'); break;
                    case 'n':  sb.Append('\n'); break;
                    case 'r':  sb.Append('\r'); break;
                    case 't':  sb.Append('\t'); break;
                    case 'u':
                        if (i + 4 > s.Length) throw Error("a \\u escape is cut short");
                        sb.Append((char)int.Parse(s.Substring(i, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                        i += 4;
                        break;
                    default: throw Error("unknown escape \\" + e);
                }
            }
        }

        double Number()
        {
            int start = i;
            while (i < s.Length && "+-0123456789.eE".IndexOf(s[i]) >= 0) i++;
            if (start == i) throw Error("unexpected character '" + s[start] + "'");
            return double.Parse(s.Substring(start, i - start), NumberStyles.Float, CultureInfo.InvariantCulture);
        }

        void Word(string word)
        {
            if (string.CompareOrdinal(s, i, word, 0, word.Length) != 0) throw Error("expected " + word);
            i += word.Length;
        }
    }
}
