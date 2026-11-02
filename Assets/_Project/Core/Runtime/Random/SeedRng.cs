using System.Runtime.CompilerServices;
using System;
using System.Collections.Generic;
using RoadToPro.Core.Math;

namespace RoadToPro.Core.Random
{
    /// <summary>
    /// 시드 RNG (specs/001 FR-30~34). xoshiro256** (Blackman·Vigna 2018) + 분포 함수.
    /// 전역·정적으로 두지 않고 인자로 주입한다. 스트림은 <see cref="RngStreams"/>로 만든다.
    /// </summary>
    public sealed class SeedRng
    {
        /// <summary>softmax 온도가 이 값 이하면 최댓값 선택 (동점이면 낮은 인덱스).</summary>
        public const float MinTemperature = 1e-6f;

        ulong _s0, _s1, _s2, _s3;

        public SeedRng(ulong seed)
        {
            var sm = seed;
            _s0 = SplitMix64.Next(ref sm);
            _s1 = SplitMix64.Next(ref sm);
            _s2 = SplitMix64.Next(ref sm);
            _s3 = SplitMix64.Next(ref sm);
            if ((_s0 | _s1 | _s2 | _s3) == 0) _s0 = 1; // 전부 0이면 고정점 — SplitMix에서는 사실상 불가능
        }

        public SeedRng(RngState state) => RestoreState(state);

        public RngState SaveState() => new RngState(_s0, _s1, _s2, _s3);

        public void RestoreState(RngState s)
        {
            if ((s.S0 | s.S1 | s.S2 | s.S3) == 0) throw new ArgumentException("RNG 상태가 전부 0이다");
            _s0 = s.S0;
            _s1 = s.S1;
            _s2 = s.S2;
            _s3 = s.S3;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ulong NextU64()
        {
            unchecked
            {
                var result = Rotl(_s1 * 5, 7) * 9;
                var t = _s1 << 17;
                _s2 ^= _s0;
                _s3 ^= _s1;
                _s1 ^= _s2;
                _s0 ^= _s3;
                _s2 ^= t;
                _s3 = Rotl(_s3, 45);
                return result;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public uint NextU32() => (uint)(NextU64() >> 32);

        /// <summary>균등 실수 [0, 1). 상위 24비트 사용.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public float NextFloat() => (NextU64() >> 40) * (1f / 16777216f);

        /// <summary>균등 실수 [min, max).</summary>
        public float Range(float min, float max)
        {
            var v = min + (max - min) * NextFloat();
            // float 반올림으로 max가 나오면 max 바로 아래 값 (결정론 리뷰 #1)
            return v < max || !(max > min) ? v : BitDecrement(max);
        }

        static float BitDecrement(float x)
        {
            if (x == 0f) return -float.Epsilon;
            var bits = BitConverter.SingleToInt32Bits(x);
            return BitConverter.Int32BitsToSingle(x > 0f ? bits - 1 : bits + 1);
        }

        /// <summary>균등 정수 [min, max] (양끝 포함, 편향 없음 — Lemire 2019). min > max면 예외.</summary>
        public int Range(int min, int max)
        {
            if (min > max) throw new ArgumentException($"범위 오류: {min} > {max}");
            var span = (ulong)((long)max - min + 1);
            if (span == 1UL << 32) return unchecked((int)NextU32());
            var s = (uint)span;
            var m = (ulong)NextU32() * s;
            var l = (uint)m;
            if (l < s)
            {
                var threshold = unchecked(0u - s) % s;
                while (l < threshold)
                {
                    m = (ulong)NextU32() * s;
                    l = (uint)m;
                }
            }
            return (int)((long)min + (long)(m >> 32));
        }

        /// <summary>확률 p로 true. p≤0이면 항상 false, p≥1이면 항상 true (난수는 1회 소비).</summary>
        public bool Bernoulli(float p) => NextFloat() < p;

        /// <summary>정규분포 (Box–Muller, 캐시 없음 — 상태 = 4×ulong만). 꼬리값은 클램프하지 않는다 (호출자 몫).</summary>
        public float Normal(float mean = 0f, float stdDev = 1f)
        {
            var u1 = 1f - NextFloat(); // (0, 1]
            var u2 = NextFloat();
            var z = DMath.Sqrt(-2f * DMath.Log(u1)) * DMath.Cos(DMath.TwoPi * u2);
            return mean + stdDev * z;
        }

        /// <summary>로그정규: exp(N(mu, sigma)).</summary>
        public float LogNormal(float mu, float sigma) => DMath.Exp(Normal(mu, sigma));

        /// <summary>가중 선택. 가중치는 0 이상 유한, 합 &gt; 0이어야 한다 (아니면 ArgumentException).</summary>
        public int PickWeighted(ReadOnlySpan<float> weights)
        {
            if (weights.Length == 0) throw new ArgumentException("가중치가 비어 있다");
            float sum = 0f;
            for (int i = 0; i < weights.Length; i++)
            {
                var w = weights[i];
                if (float.IsNaN(w) || float.IsInfinity(w) || w < 0f)
                    throw new ArgumentException(FormattableString.Invariant($"가중치 [{i}] = {w}: 0 이상 유한해야 한다"));
                sum += w;
            }
            if (!(sum > 0f) || float.IsInfinity(sum)) throw new ArgumentException("가중치 합이 0이거나 float 범위를 넘는다");

            var r = NextFloat() * sum;
            float cum = 0f;
            var last = -1;
            for (int i = 0; i < weights.Length; i++)
            {
                if (weights[i] <= 0f) continue;
                cum += weights[i];
                last = i;
                if (r < cum) return i;
            }
            return last; // 부동소수점 반올림으로 r == sum인 경우
        }

        public int PickWeighted(float[] weights) => PickWeighted((ReadOnlySpan<float>)weights);

        /// <summary>
        /// softmax(온도 τ) 선택: P(i) ∝ exp((u_i − max)/τ). τ ≤ <see cref="MinTemperature"/>면 최댓값(동점 낮은 인덱스),
        /// 이 경우 난수를 소비하지 않는다. 할당 없음 (exp를 두 번 계산).
        /// </summary>
        public int PickSoftmax(ReadOnlySpan<float> utilities, float temperature)
        {
            if (utilities.Length == 0) throw new ArgumentException("후보가 비어 있다");
            if (float.IsNaN(temperature) || temperature < 0f) throw new ArgumentException(FormattableString.Invariant($"온도 {temperature}: 0 이상이어야 한다"));
            var maxI = 0;
            for (int i = 0; i < utilities.Length; i++)
            {
                var u = utilities[i];
                if (float.IsNaN(u) || float.IsInfinity(u)) throw new ArgumentException(FormattableString.Invariant($"효용 [{i}] = {u}: 유한해야 한다"));
                if (u > utilities[maxI]) maxI = i;
            }
            if (temperature <= MinTemperature) return maxI;

            var max = utilities[maxI];
            float sum = 0f;
            for (int i = 0; i < utilities.Length; i++) sum += DMath.Exp((utilities[i] - max) / temperature);
            var r = NextFloat() * sum;
            float cum = 0f;
            for (int i = 0; i < utilities.Length; i++)
            {
                cum += DMath.Exp((utilities[i] - max) / temperature);
                if (r < cum) return i;
            }
            return maxI;
        }

        public int PickSoftmax(float[] utilities, float temperature) => PickSoftmax((ReadOnlySpan<float>)utilities, temperature);

        /// <summary>Fisher–Yates 셔플.</summary>
        public void Shuffle<T>(Span<T> items)
        {
            for (int i = items.Length - 1; i > 0; i--)
            {
                var j = Range(0, i);
                var tmp = items[i];
                items[i] = items[j];
                items[j] = tmp;
            }
        }

        public void Shuffle<T>(T[] items) => Shuffle((Span<T>)items);

        public void Shuffle<T>(IList<T> items)
        {
            for (int i = items.Count - 1; i > 0; i--)
            {
                var j = Range(0, i);
                var tmp = items[i];
                items[i] = items[j];
                items[j] = tmp;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static ulong Rotl(ulong x, int k) => (x << k) | (x >> (64 - k));
    }
}
