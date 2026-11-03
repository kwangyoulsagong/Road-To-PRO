using RoadToPro.Core.Math;

namespace RoadToPro.MatchEngine.Kinematics
{
    /// <summary>
    /// 우선순위 합성 (specs/005 FR-27): 충돌 회피 > 행동 > 대형 오프셋. 속도 예산(Vmax_eff)을 상위 항이 먼저 쓰고
    /// 남은 만큼 하위 항을 더한다. 가중치는 튜닝 데이터.
    /// </summary>
    public static class SteeringCombiner
    {
        public static Vec2 Combine(Vec2 separation, Vec2 behavior, bool hasFormation, Vec2 formation, float budget, KinematicsParams p)
        {
            Vec2 acc;
            float rem;
            if (separation.X == 0f && separation.Y == 0f) { acc = Vec2.Zero; rem = budget; } // 흔한 경우: 주변에 아무도 없음
            else
            {
                acc = Steering.ClampLength(separation * p.WSeparation, budget);
                rem = budget - acc.Length();
                if (rem <= 0f) return acc;
            }
            if (!hasFormation) return acc + Steering.ClampLength(behavior * p.WBehavior, rem);
            var b = Steering.ClampLength(behavior * p.WBehavior, rem);
            acc += b;
            rem -= b.Length();
            if (rem <= 0f) return acc;
            return acc + Steering.ClampLength(formation * p.WFormation, rem);
        }
    }
}
