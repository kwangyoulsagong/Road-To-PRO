using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace RoadToPro.Core.Serialization
{
    /// <summary>JSON 해석 오류. 줄·열은 1부터.</summary>
    public sealed class JsonException : Exception
    {
        public readonly int Line;
        public readonly int Column;

        public JsonException(string message, int line, int column) : base($"{message} (줄 {line}, 열 {column})")
        {
            Line = line;
            Column = column;
        }
    }

    /// <summary>JSON 객체. 키 순서를 보존하고 중복 키도 그대로 남긴다 (검증은 호출자가).</summary>
    public sealed class JsonObject
    {
        public readonly List<KeyValuePair<string, object>> Members = new List<KeyValuePair<string, object>>();

        public bool TryGet(string key, out object value)
        {
            for (int i = 0; i < Members.Count; i++)
                if (string.Equals(Members[i].Key, key, StringComparison.Ordinal))
                {
                    value = Members[i].Value;
                    return true;
                }
            value = null;
            return false;
        }
    }

    /// <summary>
    /// 최소 JSON 리더 (Unity JsonUtility 대신 — 코어는 엔진 비참조). 튜닝 데이터 로드용.
    /// 결과: 객체 <see cref="JsonObject"/>, 배열 List&lt;object&gt;, 문자열 string, 숫자 double(InvariantCulture), bool, null.
    /// </summary>
    public static class JsonReader
    {
        public static object Parse(string text)
        {
            if (text == null) throw new ArgumentNullException(nameof(text));
            var p = new Parser(text);
            p.SkipWs();
            var v = p.ReadValue();
            p.SkipWs();
            if (!p.End) throw p.Error("값 뒤에 남은 문자");
            return v;
        }

        sealed class Parser
        {
            /// <summary>중첩 깊이 상한 (커뮤니티 모드 JSON의 스택 넘침 방지).</summary>
            const int MaxDepth = 64;

            readonly string _s;
            int _i;
            int _depth;

            public Parser(string s)
            {
                _s = s;
            }

            public bool End => _i >= _s.Length;

            public JsonException Error(string msg)
            {
                int line = 1, col = 1;
                for (int k = 0; k < _i && k < _s.Length; k++)
                {
                    if (_s[k] == '\n')
                    {
                        line++;
                        col = 1;
                    }
                    else col++;
                }
                return new JsonException(msg, line, col);
            }

            public void SkipWs()
            {
                while (!End && (_s[_i] == ' ' || _s[_i] == '\t' || _s[_i] == '\n' || _s[_i] == '\r')) _i++;
            }

            public object ReadValue()
            {
                if (End) throw Error("값이 없다");
                var c = _s[_i];
                switch (c)
                {
                    case '{': return ReadObject();
                    case '[': return ReadArray();
                    case '"': return ReadString();
                    case 't': Expect("true"); return true;
                    case 'f': Expect("false"); return false;
                    case 'n': Expect("null"); return null;
                    default:
                        if (c == '-' || (c >= '0' && c <= '9')) return ReadNumber();
                        throw Error($"예상하지 못한 문자 '{c}'");
                }
            }

            void Expect(string word)
            {
                if (string.CompareOrdinal(_s, _i, word, 0, word.Length) != 0) throw Error($"'{word}' 기대");
                _i += word.Length;
            }

            void Enter()
            {
                if (++_depth > MaxDepth) throw Error($"중첩이 {MaxDepth}단계를 넘는다");
            }

            JsonObject ReadObject()
            {
                Enter();
                var o = new JsonObject();
                _i++;
                SkipWs();
                if (!End && _s[_i] == '}')
                {
                    _i++;
                    _depth--;
                    return o;
                }
                while (true)
                {
                    SkipWs();
                    if (End || _s[_i] != '"') throw Error("객체 키(문자열) 기대");
                    var key = ReadString();
                    SkipWs();
                    if (End || _s[_i] != ':') throw Error("':' 기대");
                    _i++;
                    SkipWs();
                    var v = ReadValue();
                    o.Members.Add(new KeyValuePair<string, object>(key, v));
                    SkipWs();
                    if (End) throw Error("객체가 닫히지 않았다");
                    if (_s[_i] == ',')
                    {
                        _i++;
                        continue;
                    }
                    if (_s[_i] == '}')
                    {
                        _i++;
                        _depth--;
                        return o;
                    }
                    throw Error("',' 또는 '}' 기대");
                }
            }

            List<object> ReadArray()
            {
                Enter();
                var a = new List<object>();
                _i++;
                SkipWs();
                if (!End && _s[_i] == ']')
                {
                    _i++;
                    _depth--;
                    return a;
                }
                while (true)
                {
                    SkipWs();
                    a.Add(ReadValue());
                    SkipWs();
                    if (End) throw Error("배열이 닫히지 않았다");
                    if (_s[_i] == ',')
                    {
                        _i++;
                        continue;
                    }
                    if (_s[_i] == ']')
                    {
                        _i++;
                        _depth--;
                        return a;
                    }
                    throw Error("',' 또는 ']' 기대");
                }
            }

            string ReadString()
            {
                _i++; // "
                var sb = new StringBuilder();
                while (true)
                {
                    if (End) throw Error("문자열이 닫히지 않았다");
                    var c = _s[_i++];
                    if (c == '"') return sb.ToString();
                    if (c < 0x20) throw Error("문자열 안의 제어 문자");
                    if (c != '\\')
                    {
                        sb.Append(c);
                        continue;
                    }
                    if (End) throw Error("잘린 이스케이프");
                    var e = _s[_i++];
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
                            if (_i + 4 > _s.Length ||
                                !int.TryParse(_s.Substring(_i, 4), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var cp))
                                throw Error("잘못된 \\u 이스케이프");
                            sb.Append((char)cp);
                            _i += 4;
                            break;
                        default: throw Error($"알 수 없는 이스케이프 '\\{e}'");
                    }
                }
            }

            double ReadNumber()
            {
                var start = _i;
                if (_s[_i] == '-') _i++;
                while (!End && (char.IsDigit(_s[_i]) || _s[_i] == '.' || _s[_i] == 'e' || _s[_i] == 'E' || _s[_i] == '+' || _s[_i] == '-')) _i++;
                var t = _s.Substring(start, _i - start);
                if (!double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) || double.IsInfinity(v))
                {
                    _i = start;
                    throw Error($"잘못된 숫자 '{t}'");
                }
                return v;
            }
        }
    }
}
