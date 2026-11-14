using RoadToPro.Core.Math;
using RoadToPro.MatchEngine.Ball;

namespace RoadToPro.MatchEngine.Actions
{
    /// <summary>
    /// xG 평가 모델 (specs/071 FR-10·11·16 — 이 스펙이 소유). RNG 없음. 072는 슈팅 후보 평가에, 015는 슈팅 순간 기록에 같은 함수를 쓴다.
    /// 실제 득점은 <see cref="ShotExecution"/> + 004 물리 + 011 GK가 정한다(FR-12).
    /// </summary>
    public static class ShotModel
    {
        /// <summary>
        /// FR-10 각도: atan(7.32·x / (x² + y² − 3.66²)), 음수면 +π. x = 골라인에서 경기장 안쪽 거리(m), y = 골문 중앙 기준 좌우(m).
        /// atan2(7.32·x, x² + y² − 3.66²)와 같다(x ≥ 0에서 0~π, 분모 0 → π/2). x &lt; 0(골라인 밖)이면 0.
        /// </summary>
        public static float Angle(float x, float y)
        {
            if (!(x >= 0f)) return 0f;
            var half = BallParams.GoalWidth * 0.5f;
            var num = BallParams.GoalWidth * x;
            var den = x * x + y * y - half * half;
            if (num == 0f && den >= 0f) return 0f; // 골라인 위 골문 밖
            return DMath.Atan2(num, den);
        }

        /// <summary>FR-10 로지스틱 xG (FR-11 페널티 고정, 골라인 밖 0). 결과는 0.001~0.999 (008 공통 자르기), 골라인 밖만 0.</summary>
        public static float Xg(ActionParams p, in ShotContext c)
        {
            if (c.Behind) return 0f;
            if (c.Penalty) return p.XgPenalty;
            var logit = p.XgC0 - p.XgKDist * c.Dist + p.XgKAngle * c.Angle - p.XgKHeader * c.Header - p.XgKPress * c.Pressure
                        - p.XgKBlock * c.Blockers + p.XgKGkOut * c.GkOut + p.XgKAssist * c.Assist - p.XgKBounce * c.Bouncing;
            return ActionMath.Prob(logit, p);
        }

        /// <summary>
        /// 지점 from에서 골문 goal(골라인 중앙)을 향한 슛의 거리·각도·골라인 밖 여부를 채운다. dirToGoal = 공격 방향(+1: x가 큰 쪽 골).
        /// </summary>
        public static void Geometry(Vec2 from, Vec2 goal, int dirToGoal, ref ShotContext c)
        {
            var x = (goal.X - from.X) * dirToGoal;
            var y = from.Y - goal.Y;
            c.Behind = x < 0f;
            c.Dist = (goal - from).Length();
            c.Angle = Angle(x < 0f ? 0f : x, y);
        }
    }
}
