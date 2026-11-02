namespace RoadToPro.Core.Random
{
    /// <summary>
    /// 시드 하나에서 독립 스트림 파생 (specs/001 FR-30·31). 한 스트림의 호출 횟수는 다른 스트림에 영향이 없다.
    /// 하위 스트림 id는 선수 고유 id·슬롯 번호 같은 안정 값만 쓴다 (런타임 GetHashCode 금지).
    /// </summary>
    public static class RngStreams
    {
        // 시드와 id를 각각 섞은 뒤 더해서 다시 섞는다. XOR 결합(seed ^ f(id))은 선형이라
        // seed2 = seed1 ^ f(1) ^ f(2)이면 두 시드의 다른 스트림이 같아진다 (결정론 리뷰 #7).
        public static ulong DeriveSeed(ulong seed, RngStreamId stream) =>
            SplitMix64.Mix(unchecked(SplitMix64.Mix(seed) + SplitMix64.Mix((ulong)stream * SplitMix64.Gamma)));

        public static ulong DeriveSeed(ulong seed, RngStreamId stream, ulong subId) =>
            SplitMix64.Mix(unchecked(SplitMix64.Mix(DeriveSeed(seed, stream)) + SplitMix64.Mix((subId + 1) * SplitMix64.Gamma)));

        public static SeedRng Create(ulong seed, RngStreamId stream) => new SeedRng(DeriveSeed(seed, stream));

        public static SeedRng Create(ulong seed, RngStreamId stream, ulong subId) => new SeedRng(DeriveSeed(seed, stream, subId));
    }
}
