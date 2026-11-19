using RoadToPro.Core.Math;
using RoadToPro.Core.Random;
using RoadToPro.MatchEngine.Actions;
using RoadToPro.MatchEngine.Kinematics;

namespace RoadToPro.MatchEngine.Goalkeeper
{
    /// <summary>1v1 평가 결과 (specs/073 FR-20~22).</summary>
    public struct OneOnOneEval
    {
        /// <summary>1v1 상황 (캐리어가 골 가까이 · 캐리어–골 사이를 막는 수비수 없음).</summary>
        public bool Has;
        public int Carrier;
        /// <summary>FR-20: 공에 GK가 유리 — 스마더 후보. 아니면 세트.</summary>
        public bool Smother;
        public float TGk, TAtt, PWin, USmother, USet;
        public Vec2 Ball, SetPoint;
        /// <summary>캐리어가 슈팅 거리에 들어올 때까지 예상 시간 (s, 0 이상).</summary>
        public float TimeToShot;
        public float CarrierDist;
    }

    /// <summary>
    /// 1v1 (specs/073 FR-20~22, 리서치 04 § A5·A7). 캐리어가 자기 골에서 range 안이고 캐리어–골 선분에서 coverWidth 안에 골 쪽 수비수(필드)가
    /// 없으면 1v1. 공(터치 뒤 위치)에 대해 TTI_att − TTI_gk > smotherMargin이면 스마더, 아니면 세트(이등분선 위 전진 후 정지).
    /// 세트 지점 = 골 + (캐리어 − 골) 방향 × min(advance·거리, 011 깊이 상한). 정지 시각 오차 N(0, σ_t = 0.15 − 0.1n) (FR-22, RNG는 호출자).
    /// 스프레드(FR-21)는 거리 판정만 — 보정값은 011 선방 모델에 넘긴다.
    /// </summary>
    public sealed class OneOnOneModel
    {
        readonly GoalkeeperParams _p;

        public OneOnOneModel(GoalkeeperParams p) => _p = p;

        /// <param name="ballLoose">공이 캐리어 발에 붙어 있지 않다 (터치 뒤 굴러가는 중).</param>
        public OneOnOneEval Evaluate(ActionWorld w, int team, in KeeperProfile prof, Vec2 goal, float dirIn, int carrier, Vec2 ball, bool ballLoose)
        {
            var e = new OneOnOneEval { Carrier = carrier, Ball = ball };
            var gk = team * ActionWorld.PerTeam;
            if (carrier < 0 || !w.Active[gk] || !w.Active[carrier] || ActionWorld.TeamOf(carrier) == team) return e;
            var cpos = w.Pos[carrier];
            var toC = cpos - goal;
            var dist = toC.Length();
            if (!(dist <= _p.OneOnOneRange) || dist < 1e-3f) return e;
            var cDepth = (cpos.X - goal.X) * dirIn;
            var cw2 = _p.OneOnOneCoverWidth * _p.OneOnOneCoverWidth;
            for (int s = team * ActionWorld.PerTeam + 1; s < (team + 1) * ActionWorld.PerTeam; s++)
            {
                if (!w.Active[s]) continue;
                var p = w.Pos[s];
                if ((p.X - goal.X) * dirIn >= cDepth) continue; // 캐리어보다 골에서 먼 수비수는 막지 못함
                if (ShotWatcher.DistSqToSegment(p, cpos, goal) <= cw2) return e;
            }
            e.Has = true;
            e.CarrierDist = dist;
            var tAtt = ballLoose ? TimeToIntercept.Estimate(in w.Tti[carrier], ball.X, ball.Y) : 0f;
            var tGk = TimeToIntercept.EstimateNoReaction(in w.Tti[gk], ball.X, ball.Y) + prof.ReactTime;
            e.TAtt = tAtt;
            e.TGk = tGk;
            var gap = tAtt - tGk;
            e.Smother = ballLoose && tGk < float.MaxValue && gap > _p.OneOnOneSmotherMargin;
            e.PWin = tGk < float.MaxValue ? KeeperPositioner.Sigmoid(gap / _p.ChoiceContestScale) : 0f;
            e.USmother = e.PWin * _p.ChoiceContestWin - (1f - e.PWin) * _p.OneOnOneSmotherFail;
            e.USet = _p.ChoicePositionUtility + _p.OneOnOneSetBonus;
            var adv = _p.OneOnOneAdvance * dist;
            if (adv > _p.PosDepthMax) adv = _p.PosDepthMax;
            e.SetPoint = goal + toC * (adv / dist);
            var v = w.Vel[carrier].Length();
            var tts = (dist - _p.OneOnOneShootDist) / (v > _p.OneOnOneMinSpeed ? v : _p.OneOnOneMinSpeed);
            e.TimeToShot = tts > 0f ? tts : 0f;
            return e;
        }

        /// <summary>정지 시각 오차 표본 N(0, σ_t) (FR-22). σ_t ≤ 0이면 0, RNG 0회.</summary>
        public static float StopError(in KeeperProfile prof, SeedRng rng) => prof.OneOnOneSigma > 0f ? rng.Normal(0f, prof.OneOnOneSigma) : 0f;

        /// <summary>FR-21 스프레드: 슈터가 GK에서 spreadDist 안이면 r0 + Δr0, v_dive × (1 − k)로 바꾼 프로필.</summary>
        public KeeperProfile Spread(in KeeperProfile prof)
        {
            var k = prof;
            k.R0Bonus = _p.OneOnOneSpreadR0;
            k.DiveSpeed = prof.DiveSpeed * (1f - _p.OneOnOneSpreadDive);
            return k;
        }
    }
}
