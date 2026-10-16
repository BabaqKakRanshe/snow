using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace SkyIsland.EditorTools
{
    /// <summary>
    /// Minimal JSON reader for the parts of the data contract that JsonUtility cannot express (objects keyed by
    /// name, e.g. giants[].bones). Objects -> <see cref="JsonObject"/> (keeps the key order), arrays -> List&lt;object&gt;,
    /// numbers -> double, strings, bools, null.
    /// </summary>
    public static class SkyIslandJson
    {
        public sealed class JsonObject : List<KeyValuePair<string, object>>
        {
            /// <summary>Value of the first entry with this key (null if absent).</summary>
            public object Get(string key)
            {
                foreach (var kv in this)
                    if (kv.Key == key) return kv.Value;
                return null;
            }
        }

        /// <summary>Parses a whole JSON document.</summary>
        public static object Parse(string json)
        {
            var r = new Reader(json);
            r.SkipWhitespace();
            var v = r.ReadValue();
            r.SkipWhitespace();
            if (!r.End) throw r.Error("unexpected characters after the value");
            return v;
        }

        /// <summary>
        /// Parses only the value of one key of the top-level object; the other values are skipped without being built
        /// (fast on large layout files). Returns false when the key is absent.
        /// </summary>
        public static bool TryParseTopLevel(string json, string key, out object value)
        {
            value = null;
            var r = new Reader(json);
            r.SkipWhitespace();
            if (!r.TryConsume('{')) return false;
            r.SkipWhitespace();
            if (r.TryConsume('}')) return false;
            while (true)
            {
                r.SkipWhitespace();
                string k = r.ReadString();
                r.SkipWhitespace();
                r.Expect(':');
                r.SkipWhitespace();
                if (k == key)
                {
                    value = r.ReadValue();
                    return true;
                }
                r.SkipValue();
                r.SkipWhitespace();
                if (r.TryConsume(',')) continue;
                r.Expect('}');
                return false;
            }
        }

        // ------------------------------------------------------------------ conversions
        public static float ToFloat(object o, float def = 0f)
        {
            if (o is double d) return (float)d;
            if (o is bool b) return b ? 1f : 0f;
            return def;
        }

        public static float[] ToFloats(object o)
        {
            if (!(o is List<object> list)) return null;
            var a = new float[list.Count];
            for (int i = 0; i < a.Length; i++) a[i] = ToFloat(list[i]);
            return a;
        }

        public static string ToStr(object o) => o as string;

        public static string[] ToStrings(object o)
        {
            if (!(o is List<object> list)) return null;
            var a = new List<string>(list.Count);
            foreach (var v in list)
                if (v is string s) a.Add(s);
            return a.ToArray();
        }

        public static bool ToBool(object o) => o is bool b && b;

        // ------------------------------------------------------------------ reader
        sealed class Reader
        {
            readonly string _s;
            int _i;

            public Reader(string s) { _s = s ?? string.Empty; }

            public bool End => _i >= _s.Length;

            public Exception Error(string msg) => new FormatException($"[SkyIsland] JSON: {msg} at offset {_i}.");

            public void SkipWhitespace()
            {
                while (_i < _s.Length)
                {
                    char c = _s[_i];
                    if (c != ' ' && c != '\t' && c != '\n' && c != '\r') break;
                    _i++;
                }
            }

            public bool TryConsume(char c)
            {
                if (_i < _s.Length && _s[_i] == c) { _i++; return true; }
                return false;
            }

            public void Expect(char c)
            {
                if (!TryConsume(c)) throw Error($"'{c}' expected");
            }

            public object ReadValue()
            {
                if (End) throw Error("value expected");
                switch (_s[_i])
                {
                    case '{': return ReadObject();
                    case '[': return ReadArray();
                    case '"': return ReadString();
                    case 't': ExpectWord("true"); return true;
                    case 'f': ExpectWord("false"); return false;
                    case 'n': ExpectWord("null"); return null;
                    default: return ReadNumber();
                }
            }

            JsonObject ReadObject()
            {
                Expect('{');
                var o = new JsonObject();
                SkipWhitespace();
                if (TryConsume('}')) return o;
                while (true)
                {
                    SkipWhitespace();
                    string k = ReadString();
                    SkipWhitespace();
                    Expect(':');
                    SkipWhitespace();
                    o.Add(new KeyValuePair<string, object>(k, ReadValue()));
                    SkipWhitespace();
                    if (TryConsume(',')) continue;
                    Expect('}');
                    return o;
                }
            }

            List<object> ReadArray()
            {
                Expect('[');
                var list = new List<object>();
                SkipWhitespace();
                if (TryConsume(']')) return list;
                while (true)
                {
                    SkipWhitespace();
                    list.Add(ReadValue());
                    SkipWhitespace();
                    if (TryConsume(',')) continue;
                    Expect(']');
                    return list;
                }
            }

            public string ReadString()
            {
                Expect('"');
                StringBuilder sb = null;
                int start = _i;
                while (true)
                {
                    if (End) throw Error("unterminated string");
                    char c = _s[_i];
                    if (c == '"')
                    {
                        string result = sb == null ? _s.Substring(start, _i - start) : sb.Append(_s, start, _i - start).ToString();
                        _i++;
                        return result;
                    }
                    if (c != '\\') { _i++; continue; }
                    if (sb == null) sb = new StringBuilder();
                    sb.Append(_s, start, _i - start);
                    _i++;
                    if (End) throw Error("bad escape");
                    char e = _s[_i++];
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
                            if (_i + 4 > _s.Length) throw Error("bad \\u escape");
                            sb.Append((char)int.Parse(_s.Substring(_i, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                            _i += 4;
                            break;
                        default: throw Error("bad escape");
                    }
                    start = _i;
                }
            }

            double ReadNumber()
            {
                int start = _i;
                while (_i < _s.Length && "+-0123456789.eE".IndexOf(_s[_i]) >= 0) _i++;
                if (_i == start) throw Error($"unexpected character '{_s[_i]}'");
                string token = _s.Substring(start, _i - start);
                if (!double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out double d))
                    throw Error($"bad number '{token}'");
                return d;
            }

            void ExpectWord(string w)
            {
                if (_i + w.Length > _s.Length || string.CompareOrdinal(_s, _i, w, 0, w.Length) != 0) throw Error(w + " expected");
                _i += w.Length;
            }

            /// <summary>Moves past one value without building it.</summary>
            public void SkipValue()
            {
                if (End) throw Error("value expected");
                char c = _s[_i];
                if (c == '"') { SkipString(); return; }
                if (c == '{' || c == '[')
                {
                    int depth = 0;
                    while (_i < _s.Length)
                    {
                        char ch = _s[_i];
                        if (ch == '"') { SkipString(); continue; }
                        if (ch == '{' || ch == '[') depth++;
                        else if (ch == '}' || ch == ']')
                        {
                            depth--;
                            if (depth == 0) { _i++; return; }
                        }
                        _i++;
                    }
                    throw Error("unterminated object or array");
                }
                // number / true / false / null
                while (_i < _s.Length)
                {
                    char ch = _s[_i];
                    if (ch == ',' || ch == '}' || ch == ']' || ch == ' ' || ch == '\t' || ch == '\n' || ch == '\r') break;
                    _i++;
                }
            }

            void SkipString()
            {
                _i++; // opening quote
                while (_i < _s.Length)
                {
                    char c = _s[_i++];
                    if (c == '\\') { _i++; continue; }
                    if (c == '"') return;
                }
                throw Error("unterminated string");
            }
        }
    }
}
