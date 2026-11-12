using RoadToPro.Core.Math;
using RoadToPro.MatchEngine.Ball;
using RoadToPro.MatchEngine.Kinematics;

namespace RoadToPro.MatchEngine.Actions
{
    /// <summary>물리 xPass 결과 (FR-10): 확률 4종 합 = 1.</summary>
    public struct XPassResult
    {
        public float Receiver, Teammate, Opponent, Out;
    }

    /// <summary>
    /// 물리 기반 xPass (specs/008 FR-10) — 004 <see cref="BallStepper"/>를 상태 복사본으로 돌려 궤적을 적분하고, 샘플마다 공 위치에 도달한(005 TTI)
    /// 선수가 처음 도달한 순간 <see cref="BallControl"/> 판정 확률로 남은 확률 질량을 가져간다(선수 id 순). 라인 밖으로 나가면 남은 질량은 아웃,
    /// 멈추거나 상한 시간이면 그 지점에 가장 빨리 닿는 선수에게. RNG 없음, 할당 없음(인스턴스 버퍼 재사용 — 경기당 1개).
    /// 틱 기본 경로에서는 쓰지 않는다(키 모먼트 019·계수 피팅·디버그용, FR-11).
    /// </summary>
    public sealed class PhysicsXPass
    {
        readonly BallEventBuffer _ev = new BallEventBuffer(32);
        readonly bool[] _arrived = new bool[ActionWorld.N];

        public XPassResult Evaluate(ActionParams p, BallParams bp, ActionWorld w, int passer, int receiver, in BallState start)
        {
            System.Array.Clear(_arrived, 0, _arrived.Length);
            var s = start;
            var team = ActionWorld.TeamOf(passer);
            var every = (int)(p.XpSampleDt / bp.Dt + 0.5f);
            if (every < 1) every = 1;
            var steps = (int)(p.XpHorizon / bp.Dt + 0.5f);
            var mass = 1f;
            var res = default(XPassResult);
            for (int i = 1; i <= steps && mass > 0f; i++)
            {
                _ev.Clear();
                BallStepper.Step(ref s, bp, _ev, out _);
                for (int k = 0; k < _ev.Count; k++)
                    if (_ev[k].Kind == BallEventKind.BoundaryCrossed) { res.Out += mass; return res; }
                if (i % every != 0 && s.Phase != BallPhase.Rest) continue;
                var t = i * bp.Dt;
                var pos = s.Position;
                var speed = s.Velocity.Length();
                for (int j = 0; j < ActionWorld.N && mass > 0f; j++)
                {
                    if (!w.Active[j] || _arrived[j] || j == passer) continue;
                    if (pos.Z > p.CtrlMaxZ) continue;
                    var tti = TimeToIntercept.Estimate(in w.Tti[j], pos.X, pos.Y);
                    if (tti > t + p.CtrlRadius / DMath.Max(w.Profile[j].MaxSpeed, 0.1f)) continue;
                    _arrived[j] = true;
                    var opp = ActionWorld.TeamOf(j) != team;
                    var c = BallControl.Prob(p, 0f, speed, pos.Z, w.FirstTouch[j], 0f, opp, false);
                    Credit(ref res, j, receiver, team, mass * c);
                    mass *= 1f - c;
                }
                if (s.Phase == BallPhase.Rest) break;
            }
            if (mass > 0f)
            {
                // 멈춘 공·상한 시간: 그 지점에 가장 빨리 닿는 선수 (동률은 낮은 id)
                var best = -1;
                var bt = float.MaxValue;
                for (int j = 0; j < ActionWorld.N; j++)
                {
                    if (!w.Active[j] || j == passer) continue;
                    var tti = TimeToIntercept.Estimate(in w.Tti[j], s.Position.X, s.Position.Y);
                    if (tti < bt) { bt = tti; best = j; }
                }
                if (best < 0) res.Out += mass;
                else Credit(ref res, best, receiver, team, mass);
            }
            return res;
        }

        static void Credit(ref XPassResult r, int j, int receiver, int team, float m)
        {
            if (j == receiver) r.Receiver += m;
            else if (ActionWorld.TeamOf(j) == team) r.Teammate += m;
            else r.Opponent += m;
        }
    }
}
