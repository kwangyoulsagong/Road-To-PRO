using RoadToPro.Core.Math;
using RoadToPro.MatchEngine.Actions;
using RoadToPro.MatchEngine.Kinematics;
using RoadToPro.MatchEngine.PitchControl;

namespace RoadToPro.MatchEngine.Goalkeeper
{
    /// <summary>스위퍼 게이트 결과 (specs/073 FR-01).</summary>
    public struct SweepGate
    {
        public bool Has;
        /// <summary>첫 접촉점 x* (상대 공격수 TTI ≤ 공 도착 시간인 첫 경로 샘플).</summary>
        public Vec2 Point;
        /// <summary>지금부터 공이 x*에 오는 시간 (s).</summary>
        public float BallTime;
        public float TAtt, TGk, Delta, Margin;
        public int Attacker;
        public bool OutsideBox;
        /// <summary>Δ > 마진 — 돌진 후보 (EU는 따로).</summary>
        public bool Pass;
    }

    /// <summary>
    /// 스위퍼 돌진 판정 (specs/073 FR-01~03, 리서치 04 § A4·A7). 경로 샘플을 시각 순으로 훑어 첫 접촉점 x*를 찾고
    /// Δ = TTI_att(x*) − TTI_gk(x*)를 마진 m(Sweeping) + κ·[박스 밖] + 역할 보정과 비교한다(게이트, O(샘플 × 11)).
    /// 게이트를 통과했을 때만 007 지점 질의로 p_win(GK의 컨트롤 몫)을 구한다 — 박스 밖이면 손을 못 쓰므로 GK 배수(λ_gk) 없이(2026-11-19 플랜 Q3).
    /// EU = p_win·V_clear − (1 − p_win)·failScale·(빈 골문 xG − 현재 xG). RNG 없음, 할당 없음.
    /// TTI는 005 함수: 상대는 자기 반응 포함, GK는 반응 없는 이동 시간 + 011 FR-70 반응 시간.
    /// </summary>
    public sealed class SweeperModel
    {
        readonly GoalkeeperParams _p;

        public SweeperModel(GoalkeeperParams p) => _p = p;

        /// <summary>마진 m = 0.4 − 0.5n(Sweeping) + κ·[박스 밖] + 역할 보정 (FR-01·03).</summary>
        public float Margin(in KeeperProfile prof, KeeperRole role, bool outsideBox)
        {
            var m = prof.SweepMargin + (outsideBox ? _p.SweepKappa : 0f);
            if (role == KeeperRole.Sweeper) m += _p.SweepRoleSweeper;
            else if (role == KeeperRole.Traditional) m += _p.SweepRoleTraditional;
            return m;
        }

        /// <param name="times">경로 샘플 시각 (예측 시작 기준, s)</param>
        /// <param name="elapsed">예측 시작 뒤 지난 시간 (s) — 이보다 이른 샘플은 건너뛴다.</param>
        /// <param name="maxZ">발·머리로 닿을 수 있는 공 높이 상한 (m). 더 높은 샘플은 접촉점이 아니다.</param>
        public SweepGate Gate(ActionWorld w, int team, in KeeperProfile prof, KeeperRole role, Vec2 goal, float dirIn,
            float[] times, Vec3[] pos, int count, float elapsed, float maxZ)
        {
            var g = default(SweepGate);
            g.Attacker = -1;
            var gk = team * ActionWorld.PerTeam;
            if (!w.Active[gk]) return g;
            var opp = 1 - team;
            for (int i = 0; i < count; i++)
            {
                var tb = times[i] - elapsed;
                if (tb < 0f) continue;
                if (tb > _p.SweepHorizon) break;
                var b = pos[i];
                if (!(b.X >= 0f && b.X <= w.PitchL && b.Y >= 0f && b.Y <= w.PitchW)) return g; // 경계: 첫 접촉점 전에 피치·골라인 밖 → 후보 없음
                if (b.Z > maxZ) continue;
                var best = float.MaxValue;
                var who = -1;
                for (int s = opp * ActionWorld.PerTeam; s < (opp + 1) * ActionWorld.PerTeam; s++)
                {
                    if (!w.Active[s]) continue;
                    // 가지치기 (AC-06, 결과 동일): TTI ≥ 반응 + 직선 거리/Vm (005 x(t) ≤ Vm·t) — 이 하한이 tb보다 크면 이 샘플의 첫 접촉자가 될 수 없다.
                    // 첫 접촉점에서는 best ≤ tb인 선수만 의미가 있고 그중 최솟값을 정확히 구한다 (하한 > tb인 선수는 정확한 TTI도 > tb).
                    ref readonly var k = ref w.Tti[s];
                    var run = tb - k.Reaction;
                    if (run < 0f) continue;
                    var dx = b.X - k.StartX;
                    var dy = b.Y - k.StartY;
                    var r = run * k.Vm;
                    if (dx * dx + dy * dy > r * r) continue;
                    var t = TimeToIntercept.Estimate(in k, b.X, b.Y);
                    if (t < best) { best = t; who = s; }
                }
                if (who < 0 || !(best <= tb)) continue;
                var tGk = TimeToIntercept.EstimateNoReaction(in w.Tti[gk], b.X, b.Y) + prof.ReactTime;
                if (!(tGk < float.MaxValue) || !(best < float.MaxValue)) return g; // 경계: 도달 불가 — NaN 없음
                var depth = (b.X - goal.X) * dirIn;
                var outside = !(depth >= 0f && depth <= Match.Stages.PhysicsStage.BoxDepth && DMath.Abs(b.Y - goal.Y) <= Match.Stages.PhysicsStage.BoxHalfWidth);
                g.Has = true;
                g.Point = new Vec2(b.X, b.Y);
                g.BallTime = tb;
                g.TAtt = best;
                g.TGk = tGk;
                g.Delta = best - tGk;
                g.Attacker = who;
                g.OutsideBox = outside;
                g.Margin = Margin(in prof, role, outside);
                g.Pass = g.Delta > g.Margin;
                return g;
            }
            return g;
        }

        /// <summary>
        /// p_win = 007 지점 질의에서 GK의 컨트롤 몫 (FR-02). 박스 밖이면 GK 배수 없이(손 금지) — 질의 동안만 GK 표시를 끈다.
        /// 호출자는 <paramref name="pl"/>을 같은 틱 월드로 맞춘 뒤 부른다.
        /// </summary>
        public static float PWin(PitchControlQuery q, ControlPlayers pl, int team, Vec2 point, float ballTime, bool outsideBox)
        {
            var gk = team * ControlPlayers.PerTeam;
            var keeper = pl.IsKeeper[gk];
            float c;
            try
            {
                if (outsideBox) pl.IsKeeper[gk] = false;
                q.Point(pl, 1 - team, point, ballTime);
                c = q.PlayerControl(gk);
            }
            finally { pl.IsKeeper[gk] = keeper; } // 결정론 리뷰 L2: 예외에도 GK 표시 복원
            return c < 0f ? 0f : (c > 1f ? 1f : c);
        }

        /// <summary>빈 골문 xG (FR-02, [대체]) = base − slope·거리, 0~1.</summary>
        public float EmptyXg(Vec2 from, Vec2 goal)
        {
            var v = _p.SweepEmptyXgBase - _p.SweepEmptyXgSlope * (from - goal).Length();
            return v < 0f ? 0f : (v > 1f ? 1f : v);
        }

        /// <summary>EU = p_win·V_clear − (1 − p_win)·failScale·max(0, 빈 골문 xG − 현재 xG) (FR-02).</summary>
        public float Utility(float pWin, float xgNow, float xgEmpty)
        {
            var fail = xgEmpty - xgNow;
            if (fail < 0f) fail = 0f;
            return pWin * _p.SweepVClear - (1f - pWin) * _p.SweepFailScale * fail;
        }
    }
}
