using RoadToPro.Core.Math;
using RoadToPro.Core.Random;

namespace RoadToPro.MatchEngine.Actions
{
    /// <summary>
    /// 액션 공통 수학 (specs/008 FR-01·61, 경계 상황). 순수 함수. 확률 보정은 logit 공간에서 더하고(포화 방지),
    /// 시그모이드는 큰 값에서 넘치지 않게 부호로 나눠 계산하며, 최종 확률은 [ProbMin, ProbMax]로 자른다.
    /// </summary>
    public static class ActionMath
    {
        public const float DegToRad = DMath.Pi / 180f;

        /// <summary>FR-01 정규화 a = 내부/100, 0~1로 자름 (002 FR-14).</summary>
        public static float Norm(float internalValue)
        {
            var a = internalValue / 100f;
            return a < 0f ? 0f : (a > 1f ? 1f : a);
        }

        /// <summary>FR-01: logit += k·(a − 0.5).</summary>
        public static float LogitAdd(float logit, float k, float a) => logit + k * (a - 0.5f);

        /// <summary>오버플로 없는 시그모이드: exp 인자는 항상 0 이하.</summary>
        public static float Sigmoid(float x)
        {
            if (x >= 0f)
            {
                var e = DMath.Exp(-x);
                return 1f / (1f + e);
            }
            var ep = DMath.Exp(x);
            return ep / (1f + ep);
        }

        /// <summary>확률을 [min, max]로 자른다. NaN은 min.</summary>
        public static float ClampProb(float p, float min, float max)
        {
            if (!(p >= min)) return min;
            return p > max ? max : p;
        }

        /// <summary>시그모이드 + 자르기.</summary>
        public static float Prob(float logit, ActionParams p) => ClampProb(Sigmoid(logit), p.ProbMin, p.ProbMax);

        public static float Lerp(float a, float b, float t)
        {
            if (t < 0f) t = 0f;
            else if (t > 1f) t = 1f;
            return a + (b - a) * t;
        }

        /// <summary>표준 정규 표본. 정확히 균등 난수 2개 소비 (FR-61 고정 소비).</summary>
        public static float StdNormal(SeedRng rng) => rng.Normal(0f, 1f);

        /// <summary>
        /// 표준 정규 누적분포 Φ(x) (specs/071 FR-13 P_onTarget). Abramowitz–Stegun 7.1.26 erf 근사(최대 오차 1.5e-7), RNG 없음.
        /// </summary>
        public static float NormalCdf(float x)
        {
            if (!(x == x)) return 0.5f;
            var z = DMath.Abs(x) * 0.70710678f;
            var t = 1f / (1f + 0.3275911f * z);
            var poly = t * (0.254829592f + t * (-0.284496736f + t * (1.421413741f + t * (-1.453152027f + t * 1.061405429f))));
            var erf = 1f - poly * DMath.Exp(-z * z);
            return x >= 0f ? 0.5f * (1f + erf) : 0.5f * (1f - erf);
        }

        /// <summary>2D 벡터를 각도(rad)만큼 회전.</summary>
        public static Vec2 Rotate(Vec2 v, float rad)
        {
            var c = DMath.Cos(rad);
            var s = DMath.Sin(rad);
            return new Vec2(v.X * c - v.Y * s, v.X * s + v.Y * c);
        }
    }
}
