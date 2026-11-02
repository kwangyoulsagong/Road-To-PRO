using System;

namespace RoadToPro.Core.Random
{
    /// <summary>
    /// <see cref="SeedRng"/> 상태 (specs/001 FR-33). 직렬화 v1 = 33바이트: 버전(1) + s0..s3(ulong 리틀엔디언).
    /// 세이브 안의 배치는 022가 정한다.
    /// </summary>
    public readonly struct RngState : IEquatable<RngState>
    {
        public const byte FormatVersion = 1;
        public const int ByteSize = 33;

        public readonly ulong S0, S1, S2, S3;

        public RngState(ulong s0, ulong s1, ulong s2, ulong s3)
        {
            S0 = s0;
            S1 = s1;
            S2 = s2;
            S3 = s3;
        }

        public void WriteTo(Span<byte> dst)
        {
            if (dst.Length < ByteSize) throw new ArgumentException($"버퍼가 {ByteSize}바이트보다 작다", nameof(dst));
            dst[0] = FormatVersion;
            WriteU64(dst.Slice(1), S0);
            WriteU64(dst.Slice(9), S1);
            WriteU64(dst.Slice(17), S2);
            WriteU64(dst.Slice(25), S3);
        }

        public static RngState ReadFrom(ReadOnlySpan<byte> src)
        {
            if (src.Length != ByteSize) throw new ArgumentException($"RNG 상태는 {ByteSize}바이트여야 한다 (받은 길이 {src.Length})", nameof(src));
            if (src[0] != FormatVersion) throw new FormatException($"알 수 없는 RNG 상태 형식 버전 {src[0]}");
            return new RngState(ReadU64(src.Slice(1)), ReadU64(src.Slice(9)), ReadU64(src.Slice(17)), ReadU64(src.Slice(25)));
        }

        static void WriteU64(Span<byte> d, ulong v)
        {
            for (int i = 0; i < 8; i++) d[i] = (byte)(v >> (8 * i));
        }

        static ulong ReadU64(ReadOnlySpan<byte> s)
        {
            ulong v = 0;
            for (int i = 0; i < 8; i++) v |= (ulong)s[i] << (8 * i);
            return v;
        }

        public bool Equals(RngState o) => S0 == o.S0 && S1 == o.S1 && S2 == o.S2 && S3 == o.S3;
        public override bool Equals(object obj) => obj is RngState s && Equals(s);
        public override int GetHashCode() => unchecked((int)(S0 ^ S1 ^ S2 ^ S3));
    }
}
