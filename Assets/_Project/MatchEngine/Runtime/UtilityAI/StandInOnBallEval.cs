using RoadToPro.Core.Math;
using RoadToPro.MatchEngine.Match.Stages;

namespace RoadToPro.MatchEngine.UtilityAI
{
    /// <summary>
    /// 070·071·012 전 대체 평가 (specs/072 플랜 대체 1·2). 모든 계수는 decision.json `dec.onball.*` [대체] — 070·071 구현 때 교체하고 AC-13으로 재확인.
    /// 돌파 = σ(b0 + b1·(Dribbling − 수비 Tackling) − b2·압박), xG = 071 ShotModel(FR-10, 2026-11-14 교체),
    /// 크로스 = 뜬공 xPass × (base + slope·Crossing). 능력치는 0~1 정규화.
    /// </summary>
    public sealed class StandInOnBallEval : IOnBallEvaluator
    {
        readonly DecisionParams _p;
        readonly Actions.ActionParams _ap;

        /// <param name="ap">071 xG 모델 계수 (null = 코드 기본값). FR-16: xG는 071 <see cref="Actions.ShotModel"/>이 소유한다.</param>
        public StandInOnBallEval(DecisionParams p, Actions.ActionParams ap = null)
        {
            _p = p;
            _ap = ap ?? Actions.ActionParams.Defaults;
        }

        static float N(float v) => DecisionAttributes.N(v);
        static float Sigmoid(float x) => x > 30f ? 1f : (x < -30f ? 0f : 1f / (1f + DMath.Exp(-x)));
        float Clamp(float v) => v < _p.OnBallProbMin ? _p.OnBallProbMin : (v > _p.OnBallProbMax ? _p.OnBallProbMax : v);

        public float DribbleSuccess(DecisionWorld w, int s, int defender, float pressure)
        {
            var me = N(w.Tech[s].OrAverage().Dribbling);
            var def = defender >= 0 ? N(w.Tech[defender].OrAverage().Tackling) : 0f;
            return Clamp(Sigmoid(_p.OnBallDribB0 + _p.OnBallDribB1 * (me - def) - _p.OnBallDribB2 * pressure));
        }

        /// <summary>071 FR-10·16: <see cref="Actions.ShotModel.Xg"/> (결정 월드 위치로 콘 블로커·GK 이탈을 채운다). 어시스트·바운드는 결정 시점에 모름 → 0.</summary>
        public float ShotXg(DecisionWorld w, int s, Vec2 from, float pressure)
        {
            var team = DecisionWorld.TeamOf(s);
            var c = Actions.ShotContextBuilder.Build(_ap, w.Pos, w.Active, w.OppGoal(team), w.AttackDir[team], 1 - team, from, pressure);
            return Actions.ShotModel.Xg(_ap, in c);
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
