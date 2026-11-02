using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace RoadToPro.Verification
{
    /// <summary>
    /// 디바이스팜 시나리오·결과 파일용 작은 JSON 파서/직렬화기.
    /// JsonUtility는 다형 단계·혼합 타입을 못 읽고, 패키지 추가 없이 쓰려고 직접 둔다.
    /// 객체 → Dictionary(string, object), 배열 → List(object), 정수 → long, 실수 → double.
    /// </summary>
    public static class MiniJson
    {
        public static object Parse(string text)
        {
            if (text == null) throw new FormatException("JSON이 null");
            var p = new Parser(text);
            p.SkipWs();
            var v = p.ReadValue();
            p.SkipWs();
            if (!p.End) throw p.Error("값 뒤에 남은 문자");
            return v;
        }

        sealed class Parser
        {
            readonly string _s;
            int _i;
            public Parser(string s) { _s = s; }
            public bool End => _i >= _s.Length;
            public FormatException Error(string msg) => new FormatException($"JSON 오류 (위치 {_i}): {msg}");

            public void SkipWs()
            {
                while (_i < _s.Length && char.IsWhiteSpace(_s[_i])) _i++;
            }

            char Peek() => End ? '\0' : _s[_i];

            public object ReadValue()
            {
                switch (Peek())
                {
                    case '{': return ReadObject();
                    case '[': return ReadArray();
                    case '"': return ReadString();
                    case 't': Expect("true"); return true;
                    case 'f': Expect("false"); return false;
                    case 'n': Expect("null"); return null;
                    default: return ReadNumber();
                }
            }

            void Expect(string word)
            {
                if (string.CompareOrdinal(_s, _i, word, 0, word.Length) != 0) throw Error($"'{word}' 기대");
                _i += word.Length;
            }

            Dictionary<string, object> ReadObject()
            {
                var d = new Dictionary<string, object>();
                _i++;
                SkipWs();
                if (Peek() == '}') { _i++; return d; }
                while (true)
                {
                    SkipWs();
                    if (Peek() != '"') throw Error("키는 문자열");
                    var k = ReadString();
                    SkipWs();
                    if (Peek() != ':') throw Error("':' 기대");
                    _i++;
                    SkipWs();
                    d[k] = ReadValue();
                    SkipWs();
                    var c = Peek();
                    _i++;
                    if (c == ',') continue;
                    if (c == '}') return d;
                    throw Error("',' 또는 '}' 기대");
                }
            }

            List<object> ReadArray()
            {
                var l = new List<object>();
                _i++;
                SkipWs();
                if (Peek() == ']') { _i++; return l; }
                while (true)
                {
                    SkipWs();
                    l.Add(ReadValue());
                    SkipWs();
                    var c = Peek();
                    _i++;
                    if (c == ',') continue;
                    if (c == ']') return l;
                    throw Error("',' 또는 ']' 기대");
                }
            }

            string ReadString()
            {
                var sb = new StringBuilder();
                _i++;
                while (true)
                {
                    if (End) throw Error("문자열이 닫히지 않음");
                    var c = _s[_i++];
                    if (c == '"') return sb.ToString();
                    if (c != '\\') { sb.Append(c); continue; }
                    if (End) throw Error("잘린 이스케이프");
                    var x = _s[_i++];
                    switch (x)
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
                            if (_i + 4 > _s.Length) throw Error("잘린 \\u");
                            sb.Append((char)int.Parse(_s.Substring(_i, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                            _i += 4;
                            break;
                        default: throw Error($"알 수 없는 이스케이프 \\{x}");
                    }
                }
            }

            object ReadNumber()
            {
                int start = _i;
                if (Peek() == '-') _i++;
                bool isFloat = false;
                while (!End)
                {
                    var c = _s[_i];
                    if (c >= '0' && c <= '9') { _i++; continue; }
                    if (c == '.' || c == 'e' || c == 'E' || c == '+' || c == '-') { isFloat = true; _i++; continue; }
                    break;
                }
                var tok = _s.Substring(start, _i - start);
                if (tok.Length == 0 || tok == "-") throw Error("값 기대");
                if (!isFloat && long.TryParse(tok, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var l)) return l;
                if (double.TryParse(tok, NumberStyles.Float, CultureInfo.InvariantCulture, out var d)) return d;
                throw Error($"숫자 형식 '{tok}'");
            }
        }

        public static string Serialize(object v)
        {
            var sb = new StringBuilder();
            Write(sb, v);
            return sb.ToString();
        }

        static void Write(StringBuilder sb, object v)
        {
            switch (v)
            {
                case null: sb.Append("null"); break;
                case bool b: sb.Append(b ? "true" : "false"); break;
                case string s: WriteString(sb, s); break;
                case int i: sb.Append(i.ToString(CultureInfo.InvariantCulture)); break;
                case long l: sb.Append(l.ToString(CultureInfo.InvariantCulture)); break;
                case ulong ul: sb.Append(ul.ToString(CultureInfo.InvariantCulture)); break;
                case float f: WriteDouble(sb, f); break;
                case double d: WriteDouble(sb, d); break;
                case IDictionary<string, object> dict:
                    sb.Append('{');
                    bool first = true;
                    foreach (var kv in dict)
                    {
                        if (!first) sb.Append(',');
                        first = false;
                        WriteString(sb, kv.Key);
                        sb.Append(':');
                        Write(sb, kv.Value);
                    }
                    sb.Append('}');
                    break;
                case System.Collections.IEnumerable list:
                    sb.Append('[');
                    bool f1 = true;
                    foreach (var x in list)
                    {
                        if (!f1) sb.Append(',');
                        f1 = false;
                        Write(sb, x);
                    }
                    sb.Append(']');
                    break;
                default: WriteString(sb, v.ToString()); break;
            }
        }

        static void WriteDouble(StringBuilder sb, double d)
        {
            if (double.IsNaN(d) || double.IsInfinity(d)) { sb.Append("null"); return; }
            sb.Append(d.ToString("R", CultureInfo.InvariantCulture));
        }

        static void WriteString(StringBuilder sb, string s)
        {
            sb.Append('"');
            foreach (var c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
        }
    }
}
