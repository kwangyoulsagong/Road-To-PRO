using RoadToPro.Core.Math;
using RoadToPro.MatchEngine.Kinematics;

namespace RoadToPro.MatchEngine.UtilityAI
{
    /// <summary>
    /// 007 전 대체 공간 평가 (specs/010 플랜). 피치 컨트롤 = 로지스틱((상대 최단 도달 시간 − 내 도달 시간) / 척도), 도달 시간은 005 TTI.
    /// 그래서 TopSpeed·Acceleration이 높으면 같은 지점의 컨트롤이 높다(AC-26 경로). xT 대체 = 상대 골까지 거리 기반.
    /// 007이 구현되면 007 질의로 교체하고 AC-37로 재확인한다.
    /// </summary>
    public sealed class StandInSpace : ISpaceEvaluator
    {
        readonly DecisionParams _p;
        int _queries;

        public StandInSpace(DecisionParams p) => _p = p;

        public int QueryCount => _queries;
        public void ResetQueries() => _queries = 0;

        public float Control(DecisionWorld w, int s, Vec2 p)
        {
            _queries++;
            var mine = TimeToIntercept.Estimate(in w.Tti[s], p.X, p.Y);
            var opp = 1 - DecisionWorld.TeamOf(s);
            float best = float.MaxValue;
            for (int o = opp * DecisionWorld.PerTeam; o < (opp + 1) * DecisionWorld.PerTeam; o++)
            {
                if (!w.Active[o]) continue;
                var t = TimeToIntercept.Estimate(in w.Tti[o], p.X, p.Y);
                if (t < best) best = t;
            }
            if (best == float.MaxValue) return 1f;
            var x = (best - mine) / DMath.Max(_p.SpaceScale, 1e-3f);
            if (x > 20f) return 1f;
            if (x < -20f) return 0f;
            return 1f / (1f + DMath.Exp(-x));
        }

        public float Threat(DecisionWorld w, int team, Vec2 p)
        {
            var d = (w.OppGoal(team) - p).Length();
            var t = 1f - d / _p.ThreatDist;
            return t < 0f ? 0f : t * t;
        }

        public float Openness(DecisionWorld w, int s, Vec2 p, float delay)
        {
            var opp = 1 - DecisionWorld.TeamOf(s);
            float best = float.MaxValue;
            for (int o = opp * DecisionWorld.PerTeam; o < (opp + 1) * DecisionWorld.PerTeam; o++)
            {
                if (!w.Active[o]) continue;
                var d = (w.Perceived(o, delay) - p).LengthSq();
                if (d < best) best = d;
            }
            if (best == float.MaxValue) return 1f;
            var v = DMath.Sqrt(best) / _p.OpenDist;
            return v > 1f ? 1f : v;
        }
    }
}
