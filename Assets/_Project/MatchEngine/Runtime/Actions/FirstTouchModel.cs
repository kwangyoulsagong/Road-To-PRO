using RoadToPro.Core.Math;
using RoadToPro.Core.Random;

namespace RoadToPro.MatchEngine.Actions
{
    /// <summary>첫 터치 결과 (FR-67).</summary>
    public struct FirstTouchResult
    {
        /// <summary>공 진행 방향 기준 부호 있는 이격 (m). 음수 = 몸 앞에서 튀어 돌아옴.</summary>
        public float Offset;
        public float Distance;
        /// <summary>이격 방향 (단위 벡터).</summary>
        public Vec2 Direction;
        public bool Heavy;
        public float Sigma;
    }

    /// <summary>
    /// 첫 터치 실행 (specs/008 FR-67) — 계수 [대체] 리서치 03 B2 "FirstTouch 이격 σ 1.8 − 1.4s m".
    /// σ = (base − slope·a_FT) / λ_recv(v, a_FT) × (1 + pressK·pressure). 오프셋 x ~ N(0, σ)(진행 방향 성분), 방향 = 공 진행 방향을 U(−A, A)만큼 돌림(x < 0이면 반대).
    /// 이격 |x| > 1.5 m면 불안한 터치(070 FR-14). 정확히 균등 난수 3개 소비 (FR-61).
    /// </summary>
    public static class FirstTouchModel
    {
        public const int RngPerTouch = 3;

        public static float Sigma(ActionParams p, float aFirstTouch, float ballSpeed, float pressure)
        {
            var b = p.TouchSigmaBase - p.TouchSigmaSlope * aFirstTouch;
            if (b < 0f) b = 0f;
            return b / PassModel.RecvDecay(p, ballSpeed, aFirstTouch) * (1f + p.TouchPressK * pressure);
        }

        public static FirstTouchResult Sample(ActionParams p, float aFirstTouch, float ballSpeed, float pressure, Vec2 ballDir, SeedRng rng)
        {
            var r = default(FirstTouchResult);
            r.Sigma = Sigma(p, aFirstTouch, ballSpeed, pressure);
            r.Offset = ActionMath.StdNormal(rng) * r.Sigma;
            var ang = (rng.NextFloat() * 2f - 1f) * p.TouchAngle * ActionMath.DegToRad;
            r.Distance = DMath.Abs(r.Offset);
            var dir = ballDir.LengthSq() > 1e-8f ? ballDir.Normalized() : new Vec2(1f, 0f);
            dir = ActionMath.Rotate(dir, ang);
            r.Direction = r.Offset < 0f ? -dir : dir;
            r.Heavy = r.Distance > p.TouchHeavy;
            return r;
        }
    }
}
