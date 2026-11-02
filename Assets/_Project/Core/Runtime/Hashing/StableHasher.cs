using System.Runtime.CompilerServices;
using System;
using System.Globalization;

namespace RoadToPro.Core.Hashing
{
    /// <summary>
    /// 안정 해시 (specs/001 FR-60·62). FNV-1a 64비트, 모든 정수는 리틀엔디언 바이트로 넣는다.
    /// 프로세스·실행·런타임 버전과 무관하게 같은 값. 런타임 GetHashCode를 쓰지 않는다.
    /// 실수: −0 → +0, 모든 NaN → 표준 NaN 비트로 정규화한 뒤 비트를 해시 (그 외 1 ulp 차이도 다른 해시).
    /// 문자열: 길이(UTF-8 바이트 수) 접두 + UTF-8 — "ab"+"c"와 "a"+"bc"가 다르다. 할당 없음.
    /// 가변 struct이므로 지역 변수나 ref로만 다룬다.
    /// </summary>
    public struct StableHasher
    {
        public const ulong OffsetBasis = 14695981039346656037UL;
        public const ulong Prime = 1099511628211UL;
        public const uint CanonicalNaN32 = 0x7FC00000u;
        public const ulong CanonicalNaN64 = 0x7FF8000000000000UL;

        ulong _h;
        bool _started;

        public static StableHasher Create() => new StableHasher { _h = OffsetBasis, _started = true };

        public ulong Value => _started ? _h : OffsetBasis;
        public string Hex => Value.ToString("x16", CultureInfo.InvariantCulture);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Add(byte b)
        {
            if (!_started)
            {
                _h = OffsetBasis;
                _started = true;
            }
            unchecked
            {
                _h ^= b;
                _h *= Prime;
            }
        }

        /// <summary>바이트를 그대로 넣는다 (길이 접두 없음). 가변 길이 필드를 여러 개 넣을 땐 <see cref="AddWithLength"/>.</summary>
        public void Add(ReadOnlySpan<byte> bytes)
        {
            for (int i = 0; i < bytes.Length; i++) Add(bytes[i]);
        }

        /// <summary>길이(uint) 접두 + 바이트 — 연속한 가변 길이 필드의 경계를 구분한다.</summary>
        public void AddWithLength(ReadOnlySpan<byte> bytes)
        {
            Add((uint)bytes.Length);
            Add(bytes);
        }

        public void Add(bool v) => Add(v ? (byte)1 : (byte)0);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Add(uint v)
        {
            Add((byte)v);
            Add((byte)(v >> 8));
            Add((byte)(v >> 16));
            Add((byte)(v >> 24));
        }

        public void Add(int v) => Add(unchecked((uint)v));

        public void Add(ulong v)
        {
            Add((uint)v);
            Add((uint)(v >> 32));
        }

        public void Add(long v) => Add(unchecked((ulong)v));

        public void Add(float v) => Add(FloatBits(v));

        public void Add(double v) => Add(DoubleBits(v));

        public void Add(string s)
        {
            if (s == null)
            {
                Add(uint.MaxValue);
                return;
            }
            Add((uint)Utf8Length(s));
            for (int i = 0; i < s.Length; i++)
            {
                int cp = s[i];
                if (char.IsHighSurrogate(s[i]) && i + 1 < s.Length && char.IsLowSurrogate(s[i + 1]))
                {
                    cp = char.ConvertToUtf32(s[i], s[i + 1]);
                    i++;
                }
                else if (char.IsSurrogate(s[i]))
                {
                    cp = 0xFFFD;
                }
                AddCodePoint(cp);
            }
        }

        /// <summary>정규화된 float 비트.</summary>
        public static uint FloatBits(float v)
        {
            if (float.IsNaN(v)) return CanonicalNaN32;
            if (v == 0f) return 0u;
            return unchecked((uint)BitConverter.SingleToInt32Bits(v));
        }

        public static ulong DoubleBits(double v)
        {
            if (double.IsNaN(v)) return CanonicalNaN64;
            if (v == 0d) return 0UL;
            return unchecked((ulong)BitConverter.DoubleToInt64Bits(v));
        }

        void AddCodePoint(int cp)
        {
            if (cp < 0x80) Add((byte)cp);
            else if (cp < 0x800)
            {
                Add((byte)(0xC0 | (cp >> 6)));
                Add((byte)(0x80 | (cp & 0x3F)));
            }
            else if (cp < 0x10000)
            {
                Add((byte)(0xE0 | (cp >> 12)));
                Add((byte)(0x80 | ((cp >> 6) & 0x3F)));
                Add((byte)(0x80 | (cp & 0x3F)));
            }
            else
            {
                Add((byte)(0xF0 | (cp >> 18)));
                Add((byte)(0x80 | ((cp >> 12) & 0x3F)));
                Add((byte)(0x80 | ((cp >> 6) & 0x3F)));
                Add((byte)(0x80 | (cp & 0x3F)));
            }
        }

        static int Utf8Length(string s)
        {
            int n = 0;
            for (int i = 0; i < s.Length; i++)
            {
                int c = s[i];
                if (char.IsHighSurrogate(s[i]) && i + 1 < s.Length && char.IsLowSurrogate(s[i + 1]))
                {
                    n += 4;
                    i++;
                }
                else if (char.IsSurrogate(s[i])) n += 3; // U+FFFD
                else n += c < 0x80 ? 1 : c < 0x800 ? 2 : 3;
            }
            return n;
        }
    }
}
