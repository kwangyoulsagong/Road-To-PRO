using RoadToPro.MatchEngine.Actions;

namespace RoadToPro.MatchEngine.Referee
{
    /// <summary>
    /// 핸드볼 판정 (specs/012 FR-50·51, IFAB Law 12). 상태 없음·RNG 없음 — 진짜 판정만 정하고 심판 인지는 <see cref="Perception"/>.
    /// 점수 = 팔 실루엣 × 반응 항(min(1, 반응 시간 / 기준)). 자기 몸 굴절, 근거리 + 반응 부족은 면제.
    /// 공격 측이 득점 직전에 우연히 팔에 맞으면 실루엣과 무관하게 반칙(득점 취소, FR-50).
    /// </summary>
    public static class HandballJudge
    {
        static float C01(float v) => v > 0f ? (v < 1f ? v : 1f) : 0f;

        public static bool Exempt(RefereeParams p, in ContactDetail c) =>
            !c.HasHand || c.HandSelfDeflect || (c.HandBallDist < p.HandNear && c.HandReactTime < p.HandReactFull);

        public static float Score(RefereeParams p, in ContactDetail c)
        {
            if (Exempt(p, in c)) return 0f;
            var react = p.HandReactFull > 1e-3f ? C01(c.HandReactTime / p.HandReactFull) : 1f;
            return C01(c.HandSilhouette) * react;
        }

        public static bool IsOffence(RefereeParams p, in ContactDetail c) => Score(p, in c) > p.HandThreshold;

        /// <summary>공격 측 우연한 핸드볼 직후 득점 → 취소 (면제 규칙과 무관, 팔에 맞았으면).</summary>
        public static bool DisallowsAttackGoal(in ContactDetail c) => c.HasHand;
    }
}
