using RoadToPro.Core.Math;
using RoadToPro.Core.Random;
using RoadToPro.MatchEngine.Actions;
using RoadToPro.MatchEngine.Kinematics;

namespace RoadToPro.MatchEngine.Goalkeeper
{
    /// <summary>크로스 처리 평가 결과 (specs/073 FR-10~12).</summary>
    public struct CrossEval
    {
        /// <summary>잡기·펀칭 후보가 있다 (착지점이 자기 박스 안·골문 밖, 손 도달 높이 안, 착지 전 도착 가능).</summary>
        public bool Has;
        public Vec2 Landing;
        public float TLand, TGk, TOpp, Dt;
        public int Crowd;
        public float PClaim, PPunch, UClaim, UPunch;
        /// <summary>착지점이 골문 안(골라인 너머) — 클레임 대신 011 선방 판정.</summary>
        public bool InGoal;
    }

    /// <summary>
    /// 크로스 처리 (specs/073 FR-10~12, 리서치 04 § A5·A7). 착지점 L·시각은 071 공중볼 예측(<see cref="AerialSystem.TryLanding"/>)을 받는다.
    /// GK 도착 = 반응 없는 이동 시간 + 반응(011 FR-70) + 결단 지연 0.2·(1 − n(CommandArea)) (FR-12).
    /// Δt = 상대 최단 도착 − GK 도착. p_claim = σ(k1·Δt + k2·n(CA) − k3·혼잡도(반경 2 m, GK 제외)), p_punch = σ(k1·Δt + k0) (혼잡도 영향 없음 [대체]).
    /// 효용: U = p·성공 가치 − (1 − p)·실패 비용, 잡기 실패 비용이 가장 크다(빈 골문). 포기 = 위치 선정 후보(선택기가 비교). RNG는 결단 노이즈만(호출자).
    /// </summary>
    public sealed class CrossClaimModel
    {
        readonly GoalkeeperParams _p;

        public CrossClaimModel(GoalkeeperParams p) => _p = p;

        public static float Sigmoid(float x) => KeeperPositioner.Sigmoid(x);

        /// <summary>손 도달 높이 (점프) = 기준(키 비례) + Jumping 보정 (FR-10).</summary>
        public float JumpReach(float heightM, in KeeperProfile prof) => _p.CrossJumpReach * (heightM / _p.ReachRefHeight) + prof.JumpBonus;

        /// <param name="contactZ">공을 맞이하는 높이(071 경합 접촉 높이).</param>
        /// <param name="ttiSlack">착지 뒤 도착 허용 (071 경합 참여 기준과 같은 값).</param>
        public CrossEval Evaluate(ActionWorld w, int team, in KeeperProfile prof, float heightM, Vec2 goal, float dirIn, Vec2 landing, float tLand, float contactZ, float ttiSlack)
        {
            var e = new CrossEval { Landing = landing, TLand = tLand };
            var gk = team * ActionWorld.PerTeam;
            if (!w.Active[gk]) return e;
            var depth = (landing.X - goal.X) * dirIn;
            if (depth < 0f) { e.InGoal = true; return e; } // 경계: 착지점이 골문 안 → 011 선방
            if (!(depth <= Match.Stages.PhysicsStage.BoxDepth && DMath.Abs(landing.Y - goal.Y) <= Match.Stages.PhysicsStage.BoxHalfWidth)) return e;
            if (JumpReach(heightM, in prof) < contactZ) return e;
            var tGk = TimeToIntercept.EstimateNoReaction(in w.Tti[gk], landing.X, landing.Y) + prof.ReactTime + prof.CommandDelay;
            if (!(tGk <= tLand + ttiSlack)) return e;
            var tOpp = float.MaxValue;
            var opp = 1 - team;
            for (int s = opp * ActionWorld.PerTeam; s < (opp + 1) * ActionWorld.PerTeam; s++)
            {
                if (!w.Active[s]) continue;
                var t = TimeToIntercept.Estimate(in w.Tti[s], landing.X, landing.Y);
                if (t < tOpp) tOpp = t;
            }
            var r2 = _p.CrossCrowdRadius * _p.CrossCrowdRadius;
            var crowd = 0;
            for (int s = 0; s < ActionWorld.N; s++)
                if (s != gk && w.Active[s] && (w.Pos[s] - landing).LengthSq() < r2) crowd++;
            // 상대가 없으면(도달 불가) Δt는 착지 시각 여유로 본다 — 무한대 없음
            var dt = tOpp < float.MaxValue ? tOpp - tGk : tLand + ttiSlack - tGk;
            e.Has = true;
            e.TGk = tGk;
            e.TOpp = tOpp;
            e.Dt = dt;
            e.Crowd = crowd;
            e.PClaim = Sigmoid(_p.CrossK1 * dt + _p.CrossK2 * prof.CommandN - _p.CrossK3 * crowd);
            e.PPunch = Sigmoid(_p.CrossK1 * dt + _p.CrossPunchK0);
            e.UClaim = e.PClaim * _p.CrossClaimWin - (1f - e.PClaim) * _p.CrossClaimFail;
            e.UPunch = e.PPunch * _p.CrossPunchWin - (1f - e.PPunch) * _p.CrossPunchFail;
            return e;
        }

        /// <summary>결단 지연 = 0.2·(1 − n) + N(0, σ·(1 − n)), 0 이상 (FR-12). RNG 1회(노이즈 σ가 0이면 0회).</summary>
        public float Delay(in KeeperProfile prof, SeedRng rng)
        {
            var sigma = _p.CrossDelayNoise * (1f - prof.CommandN);
            var d = prof.CommandDelay + (sigma > 0f ? rng.Normal(0f, sigma) : 0f);
            return d < 0f ? 0f : d;
        }
    }
}
