using RoadToPro.Core.Math;
using RoadToPro.MatchEngine.Match.Stages;

namespace RoadToPro.MatchEngine.UtilityAI
{
    /// <summary>
    /// 070·071·012 전 대체 평가 (specs/072 플랜 대체 1·2). 모든 계수는 decision.json `dec.onball.*` [대체] — 070·071 구현 때 교체하고 AC-13으로 재확인.
    /// 돌파 = σ(b0 + b1·(Dribbling − 수비 Tackling) − b2·압박), xG = σ(b0 − bDist·d + bAngle·골문각 + bSkill·(능력 − 0.5) − bPress·압박),
    /// 크로스 = 뜬공 xPass × (base + slope·Crossing). 능력치는 0~1 정규화.
    /// </summary>
    public sealed class StandInOnBallEval : IOnBallEvaluator
    {
        readonly DecisionParams _p;

        public StandInOnBallEval(DecisionParams p) => _p = p;

        static float N(float v) => DecisionAttributes.N(v);
        static float Sigmoid(float x) => x > 30f ? 1f : (x < -30f ? 0f : 1f / (1f + DMath.Exp(-x)));
        float Clamp(float v) => v < _p.OnBallProbMin ? _p.OnBallProbMin : (v > _p.OnBallProbMax ? _p.OnBallProbMax : v);

        public float DribbleSuccess(DecisionWorld w, int s, int defender, float pressure)
        {
            var me = N(w.Tech[s].OrAverage().Dribbling);
            var def = defender >= 0 ? N(w.Tech[defender].OrAverage().Tackling) : 0f;
            return Clamp(Sigmoid(_p.OnBallDribB0 + _p.OnBallDribB1 * (me - def) - _p.OnBallDribB2 * pressure));
        }

        public float ShotXg(DecisionWorld w, int s, Vec2 from, float pressure)
        {
            var team = DecisionWorld.TeamOf(s);
            var goal = w.OppGoal(team);
            var d = (goal - from).Length();
            var half = Ball.BallParams.GoalWidth * 0.5f;
            // 골문 시야각 = 두 골포스트 방향 사이 각
            var a = new Vec2(goal.X - from.X, goal.Y - half - from.Y);
            var b = new Vec2(goal.X - from.X, goal.Y + half - from.Y);
            var angle = DMath.Atan2(DMath.Abs(a.X * b.Y - a.Y * b.X), a.X * b.X + a.Y * b.Y);
            var t = w.Tech[s].OrAverage();
            var skill = N(d > _p.OnBallLongShotDist ? t.LongShots : t.Finishing);
            var x = _p.OnBallShotB0 - _p.OnBallShotBDist * d + _p.OnBallShotBAngle * angle + _p.OnBallShotBSkill * (skill - 0.5f) - _p.OnBallShotBPress * pressure;
            var xg = Sigmoid(x);
            return xg > _p.OnBallXgMax ? _p.OnBallXgMax : xg;
        }

        public float CrossSuccess(DecisionWorld w, int s, float loftedXPass)
        {
            var k = _p.OnBallCrossBase + _p.OnBallCrossSlope * N(w.Tech[s].OrAverage().Crossing);
            var v = loftedXPass * k;
            return v < 0f ? 0f : (v > 1f ? 1f : v);
        }

        public float FoulValue(DecisionWorld w, int s) =>
            InOppBox(w, DecisionWorld.TeamOf(s), w.Pos[s]) ? _p.OnBallDivePkValue : _p.OnBallDiveFkValue;

        public float OffsideFactor(DecisionWorld w, int passer, int receiver)
        {
            var team = DecisionWorld.TeamOf(passer);
            var dir = w.AttackDir[team];
            var r = w.Pos[receiver];
            var half = w.PitchL * 0.5f;
            var inOppHalf = (r.X - half) * dir > 0f;
            var beyondLine = (r.X - w.OffsideLineX[1 - team]) * dir > 0f;
            var aheadOfBall = (r.X - w.Ball.X) * dir > 0f;
            if (!(inOppHalf && beyondLine && aheadOfBall)) return 1f;
            var m = w.Mind[passer];
            var avoid = _p.OnBallOffsideAvoidBase + _p.OnBallOffsideAvoidSlope * (N(m.Vision) + N(m.Decisions)) * 0.5f;
            avoid = avoid < 0f ? 0f : (avoid > 1f ? 1f : avoid);
            return 1f - avoid;
        }

        public bool InOppBox(DecisionWorld w, int team, Vec2 p)
        {
            var goal = w.OppGoal(team);
            var depth = (goal.X - p.X) * w.AttackDir[team];
            return depth >= 0f && depth <= PhysicsStage.BoxDepth && DMath.Abs(p.Y - goal.Y) <= PhysicsStage.BoxHalfWidth;
        }
    }
}
