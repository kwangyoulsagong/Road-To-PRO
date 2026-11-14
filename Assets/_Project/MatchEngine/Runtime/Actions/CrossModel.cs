using RoadToPro.Core.Math;
using RoadToPro.Core.Random;

namespace RoadToPro.MatchEngine.Actions
{
    /// <summary>크로스 4타입 (specs/071 FR-01). <c>BallAction.CrossType</c>에는 값 + 1을 싣는다(0 = 크로스 아님).</summary>
    public enum CrossType : byte
    {
        Early = 0,
        Driven = 1,
        Lofted = 2,
        Cutback = 3,
    }

    /// <summary>
    /// 크로스 모델 (specs/071 FR-01·02·20). 타입 파라미터·타입 선택(상황 + 성향 대체 가중, 결정 RNG 균등 1개는 호출자가 넘김)·
    /// 착지 오차 σ_landing(RNG 정확히 균등 4개). 평가 함수는 RNG 없음.
    /// </summary>
    public static class CrossModel
    {
        public const int RngPerLanding = 4;
        public const int Types = 4;

        public static void Speed(ActionParams p, CrossType t, out float lo, out float hi)
        {
            switch (t)
            {
                case CrossType.Early: lo = p.CrossEarlyVLo; hi = p.CrossEarlyVHi; break;
                case CrossType.Driven: lo = p.CrossDrivenVLo; hi = p.CrossDrivenVHi; break;
                case CrossType.Lofted: lo = p.CrossLoftedVLo; hi = p.CrossLoftedVHi; break;
                default: lo = p.CrossCutbackVLo; hi = p.CrossCutbackVHi; break;
            }
        }

        /// <summary>최고점 범위 (m). 컷백은 땅볼 0.</summary>
        public static void Apex(ActionParams p, CrossType t, out float lo, out float hi)
        {
            switch (t)
            {
                case CrossType.Early: lo = p.CrossEarlyApexLo; hi = p.CrossEarlyApexHi; break;
                case CrossType.Driven: lo = p.CrossDrivenApexLo; hi = p.CrossDrivenApexHi; break;
                case CrossType.Lofted: lo = p.CrossLoftedApexLo; hi = p.CrossLoftedApexHi; break;
                default: lo = 0f; hi = 0f; break;
            }
        }

        /// <summary>completion 목표 (FR-01, 017 비교 기준).</summary>
        public static float Target(ActionParams p, CrossType t) => t switch
        {
            CrossType.Early => p.CrossEarlyTarget,
            CrossType.Driven => p.CrossDrivenTarget,
            CrossType.Lofted => p.CrossLoftedTarget,
            _ => p.CrossCutbackTarget,
        };

        /// <summary>뜬 공 타입 (착지 오차·공중볼 경합 대상). Driven은 낮지만 008 패스 모델로 처리(FR-02).</summary>
        public static bool IsAerial(CrossType t) => t == CrossType.Early || t == CrossType.Lofted;

        /// <summary>
        /// 거리 dist에서 최고점 범위 중앙을 지나는 발사각(rad, 공기저항 무시 근사 tan e = 4h/R). 컷백은 0.
        /// 실제 초속은 실행 때 004 역산(<c>BallInverse.LoftedSpeedFor</c>)으로 정한다.
        /// </summary>
        public static float Elevation(ActionParams p, CrossType t, float dist)
        {
            Apex(p, t, out var lo, out var hi);
            var h = 0.5f * (lo + hi);
            if (h <= 0f || dist < 0.5f) return 0f;
            return DMath.Atan2(4f * h, dist);
        }

        /// <summary>FR-02 σ_landing(m) = lerp(4.5, 1.2, a_Crossing) × (1 + 0.6·pressure).</summary>
        public static float SigmaLanding(ActionParams p, float aCrossing, float pressure)
        {
            var pr = pressure < 0f ? 0f : (pressure > 1f ? 1f : pressure);
            return ActionMath.Lerp(p.CrossSigmaHi, p.CrossSigmaLo, aCrossing) * (1f + p.CrossPressK * pr);
        }

        /// <summary>착지점 2D 정규 오차 (RNG 정확히 균등 4개).</summary>
        public static Vec2 ApplyLanding(Vec2 aim, float sigma, SeedRng rng, out Vec2 err)
        {
            var nx = ActionMath.StdNormal(rng);
            var ny = ActionMath.StdNormal(rng);
            err = new Vec2(nx * sigma, ny * sigma);
            return aim + err;
        }

        /// <summary>
        /// 타입 가중치 (FR-01·20, [대체]): 크로서가 골라인 cutbackLine 안이고 받는 선수가 더 뒤(골라인에서 2 m 이상 멀다) → 컷백 3.
        /// 받는 선수가 골라인 11 m 안 → Driven 1.0(아니면 0.4). 크로서가 골라인에서 18 m 이상 → Early 1.0(아니면 0.3). Lofted 1.0.
        /// 성향 대체: 크로스 머신 레벨 × styleK를 Lofted·Early에 더한다(FR-20 매핑 [미정]). 거리·라인 = 공격 방향 골라인까지 x 거리.
        /// </summary>
        public static void Weights(ActionParams p, float crosserToLine, float receiverToLine, byte crossLevel, float[] w)
        {
            w[(int)CrossType.Cutback] = crosserToLine <= p.CrossCutbackLine && receiverToLine >= crosserToLine + 2f ? 3f : 0f;
            w[(int)CrossType.Driven] = receiverToLine <= 11f ? 1f : 0.4f;
            w[(int)CrossType.Early] = (crosserToLine >= 18f ? 1f : 0.3f) + p.CrossStyleK * crossLevel;
            w[(int)CrossType.Lofted] = 1f + p.CrossStyleK * crossLevel;
        }

        /// <summary>균등 u ∈ [0, 1)로 가중 선택 (결정 RNG 1개는 호출자 몫). 합 0이면 Lofted.</summary>
        public static CrossType Pick(float[] w, float u)
        {
            var sum = 0f;
            for (int i = 0; i < Types; i++) sum += w[i] > 0f ? w[i] : 0f;
            if (!(sum > 0f)) return CrossType.Lofted;
            var x = u * sum;
            var acc = 0f;
            for (int i = 0; i < Types; i++)
            {
                if (!(w[i] > 0f)) continue;
                acc += w[i];
                if (x < acc) return (CrossType)i;
            }
            for (int i = Types - 1; i >= 0; i--) if (w[i] > 0f) return (CrossType)i;
            return CrossType.Lofted;
        }
    }
}
