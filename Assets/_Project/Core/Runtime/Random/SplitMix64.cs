using System.Runtime.CompilerServices;
namespace RoadToPro.Core.Random
{
    /// <summary>SplitMix64 (Steele·Lea·Flood 2014). 시드 혼합·스트림 파생용.</summary>
    public static class SplitMix64
    {
        public const ulong Gamma = 0x9E3779B97F4A7C15UL;

        /// <summary>상태를 Gamma만큼 전진시키고 섞은 값을 돌려준다.</summary>
        public static ulong Next(ref ulong state)
        {
            unchecked
            {
                state += Gamma;
                return Mix(state);
            }
        }

        /// <summary>64비트 최종 혼합(전단사). Mix(0) = 0.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ulong Mix(ulong z)
        {
            unchecked
            {
                z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
                z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
                return z ^ (z >> 31);
            }
        }
    }
}
