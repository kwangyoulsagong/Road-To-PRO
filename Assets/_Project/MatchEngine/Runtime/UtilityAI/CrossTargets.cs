using RoadToPro.Core.Math;
using RoadToPro.MatchEngine.Kinematics;

namespace RoadToPro.MatchEngine.UtilityAI
{
    /// <summary>
    /// 크로스 박스 지점 (specs/081 FR-23): 측면 구역 소유자는 박스 안 동료가 없어도 니어 포스트·파 포스트·페널티 스폿 지점으로 크로스 후보를 낸다.
    /// 지점마다 TTI가 가장 짧은 동료(GK 제외)를 받는 선수로 지정한다 — 상대와의 경합은 071 공중볼 판정 몫(플랜 해석 2).
    /// TTI가 공 비행 근사(거리 ÷ 낮은 크로스 속도) + 여유보다 길면 받는 선수 없음(빈 지점으로 차지 않는다). 순수 함수, RNG 없음, 할당 0.
    /// </summary>
    public static class CrossTargets
    {
        public const int Count = 3;
        static float GoalHalfWidth => Ball.BallParams.GoalWidth * 0.5f;

        /// <summary>지점 k (0 니어 · 1 파 · 2 스폿). 니어/파는 크로서가 있는 쪽 측면 기준.</summary>
        public static Vec2 Point(DecisionParams p, DecisionWorld w, int team, Vec2 crosser, int k)
        {
            var goalX = w.OppGoal(team).X;
            var dir = w.AttackDir[team];
            var cy = w.PitchW * 0.5f;
            var side = crosser.Y >= cy ? 1f : -1f;
            switch (k)
            {
                case 0: return new Vec2(goalX - dir * p.OnBallCrossNearX, cy + side * GoalHalfWidth);
                case 1: return new Vec2(goalX - dir * p.OnBallCrossFarX, cy - side * GoalHalfWidth);
                default: return new Vec2(goalX - dir * p.OnBallCrossSpotX, cy);
            }
        }

        /// <summary>
        /// 지점에 TTI가 가장 짧은 동료 (소유자·GK·비활성 제외, 동점 = 낮은 슬롯). flight = 공 비행 근사(s).
        /// TTI ≤ flight + 여유인 동료가 없으면 −1.
        /// </summary>
        public static int Receiver(DecisionParams p, DecisionWorld w, int owner, Vec2 point, float flight, out float tti)
        {
            var t = DecisionWorld.TeamOf(owner);
            var best = -1;
            tti = float.MaxValue;
            for (int o = t * DecisionWorld.PerTeam; o < (t + 1) * DecisionWorld.PerTeam; o++)
            {
                if (o == owner || !w.Active[o] || DecisionWorld.IsKeeper(o)) continue;
                var x = TimeToIntercept.Estimate(in w.Tti[o], point.X, point.Y);
                if (x < tti) { tti = x; best = o; }
            }
            if (best >= 0 && tti > flight + p.OnBallCrossTtiSlack) best = -1;
            return best;
        }
    }
}
