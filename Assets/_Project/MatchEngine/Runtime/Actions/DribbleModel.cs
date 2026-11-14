using RoadToPro.Core.Math;
using RoadToPro.Core.Random;

namespace RoadToPro.MatchEngine.Actions
{
    /// <summary>드리블 터치 표본 (specs/070 FR-02).</summary>
    public struct DribbleTouch
    {
        /// <summary>밀어놓는 거리 (m, 0 &lt; d ≤ touchMax).</summary>
        public float Distance;
        /// <summary>터치 방향 (단위 벡터, 의도 방향 + 오차).</summary>
        public Vec2 Direction;
        /// <summary>방향 오차 (deg, 부호 있음).</summary>
        public float ErrorDeg;
        public float MeanDistance;
        public float SigmaDeg;
    }

    /// <summary>
    /// 드리블 (specs/070 FR-01~03, 리서치 02 §3). 순수 함수. 평가(속도·평균 거리·σ·탈취 확률)는 RNG 없음,
    /// <see cref="SampleTouch"/>는 정확히 균등 난수 <see cref="RngPerTouch"/>개(방향 정규 2 + 거리 정규 2), 탈취는 <see cref="RngPerSteal"/>개 소비 (FR-61).
    /// </summary>
    public static class DribbleModel
    {
        public const int RngPerTouch = 4, RngPerSteal = 1;

        /// <summary>FR-01: vmax_ball = vmax × lerp(0.78, 0.92, a_Dribbling).</summary>
        public static float VmaxBall(ActionParams p, float vmax, float aDribbling) => vmax * ActionMath.Lerp(p.DribVmaxLo, p.DribVmaxHi, aDribbling);

        /// <summary>FR-02 평균 터치 거리: lerp(0.8, 4.0, speedNorm) × lerp(1.25, 0.8, a_Dribbling·0.6 + a_FirstTouch·0.4), 상한 5.0 m.</summary>
        public static float TouchDist(ActionParams p, float speed, float aDribbling, float aFirstTouch)
        {
            var sn = speed / DMath.Max(p.DribSpeedNormMax, 1e-3f);
            var skill = aDribbling * p.DribWDrib + aFirstTouch * p.DribWTouch;
            var d = ActionMath.Lerp(p.DribTouchLo, p.DribTouchHi, sn) * ActionMath.Lerp(p.DribSkillLo, p.DribSkillHi, skill);
            return d > p.DribTouchMax ? p.DribTouchMax : d;
        }

        /// <summary>FR-02 방향 오차 σ (deg) = lerp(12°, 3°, a_Dribbling) × (1 + pressure).</summary>
        public static float SigmaDeg(ActionParams p, float aDribbling, float pressure)
        {
            var pr = pressure < 0f ? 0f : pressure;
            return ActionMath.Lerp(p.DribSigmaMax, p.DribSigmaMin, aDribbling) * (1f + pr);
        }

        /// <summary>터치 표본. 거리 ~ N(평균, distSigma·평균)을 (0.05, touchMax]로 자른다. 방향 = 의도 방향을 N(0, σ)만큼 돌림. RNG 정확히 4.</summary>
        public static DribbleTouch SampleTouch(ActionParams p, float meanDist, float sigmaDeg, Vec2 intendedDir, SeedRng rng)
        {
            var t = default(DribbleTouch);
            t.MeanDistance = meanDist;
            t.SigmaDeg = sigmaDeg;
            t.ErrorDeg = ActionMath.StdNormal(rng) * sigmaDeg;
            var d = meanDist * (1f + p.DribDistSigma * ActionMath.StdNormal(rng));
            if (!(d > 0.05f)) d = 0.05f;
            if (d > p.DribTouchMax) d = p.DribTouchMax;
            t.Distance = d;
            var u = intendedDir.Normalized();
            if (u == Vec2.Zero) u = new Vec2(1f, 0f);
            t.Direction = ActionMath.Rotate(u, t.ErrorDeg * ActionMath.DegToRad).Normalized();
            return t;
        }

        /// <summary>FR-03 이격 중 탈취 확률: sigmoid(k·(TTI_소유자 − TTI_상대 − 여유)). 상대가 먼저 닿을수록 큼. RNG 없음.</summary>
        public static float StealProb(ActionParams p, float ttiOwner, float ttiOpponent)
            => ActionMath.Prob(p.DribStealK * (ttiOwner - ttiOpponent - p.DribStealMargin), p);
    }
}
