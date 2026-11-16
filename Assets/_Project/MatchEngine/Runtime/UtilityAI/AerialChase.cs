using RoadToPro.Core.Math;
using RoadToPro.MatchEngine.Kinematics;
using PhysicsStage = RoadToPro.MatchEngine.Match.Stages.PhysicsStage;

namespace RoadToPro.MatchEngine.UtilityAI
{
    /// <summary>
    /// 뜬 공 착지점 추격 (specs/081 FR-24·25): 071이 경합을 예약한 공이 날아가는 동안, 팀마다 착지점 TTI가 가장 짧은 필드 선수 최대
    /// <c>dec.aerial.chasers</c>명이 오프볼 결정 대신 착지점으로 간다. 매 결정 틱 다시 고른다(상태 없음 — 스냅샷 불필요). RNG 없음, 할당 0.
    /// 제외: 경기장 밖 착지점(전부), 단계가 넘긴 제외 슬롯(받는 선수 008 T14a·이동 불가·조종 불가), 착지점에서 <c>dec.aerial.maxDist</c>보다 먼 선수,
    /// 자기 박스 밖 착지점의 GK(011), 수비 팀 최후방 수비수(오프사이드 라인 — 플랜 해석 1: 009에 팀 이탈 인원 상한이 없어 추격 인원 상한이 그 역할).
    /// 순서: 팀 0 → 1, 팀 안은 TTI 오름차순(동점 = 낮은 슬롯).
    /// </summary>
    public sealed class AerialChase
    {
        public const int MaxPerTeam = 2;

        readonly int[] _sel = new int[2 * MaxPerTeam];
        readonly float[] _tti = new float[2 * MaxPerTeam];
        readonly int[] _count = new int[2];

        public Vec2 Landing { get; private set; }
        public int CountOf(int team) => _count[team];
        public int At(int team, int k) => _sel[team * MaxPerTeam + k];

        public void Clear()
        {
            _count[0] = 0;
            _count[1] = 0;
        }

        public bool IsChaser(int slot)
        {
            var t = DecisionWorld.TeamOf(slot);
            for (int k = 0; k < _count[t]; k++) if (_sel[t * MaxPerTeam + k] == slot) return true;
            return false;
        }

        /// <param name="excluded">단계가 채운 제외 슬롯 (길이 N, null이면 없음).</param>
        public void Select(DecisionParams p, DecisionWorld w, Vec2 landing, int kickerTeam, bool[] excluded)
        {
            Clear();
            Landing = landing;
            if (landing.X < 0f || landing.X > w.PitchL || landing.Y < 0f || landing.Y > w.PitchW) return;
            var max = (int)p.AerialChasers;
            if (max <= 0) return;
            if (max > MaxPerTeam) max = MaxPerTeam;
            var maxDistSq = p.AerialMaxDist * p.AerialMaxDist;
            for (int t = 0; t < 2; t++)
            {
                var lastDef = t != kickerTeam ? LastDefender(w, t) : -1;
                var ownBox = InOwnBox(w, t, landing);
                var b = t * MaxPerTeam;
                for (int o = t * DecisionWorld.PerTeam; o < (t + 1) * DecisionWorld.PerTeam; o++)
                {
                    if (!w.Active[o] || (excluded != null && excluded[o]) || o == lastDef) continue;
                    if (DecisionWorld.IsKeeper(o) && !ownBox) continue;
                    if ((w.Pos[o] - landing).LengthSq() > maxDistSq) continue;
                    var x = TimeToIntercept.Estimate(in w.Tti[o], landing.X, landing.Y);
                    // 상위 max개 삽입 (엄격 < — 슬롯 오름차순 순회라 동점은 낮은 슬롯이 남는다)
                    var n = _count[t];
                    var at = n;
                    while (at > 0 && x < _tti[b + at - 1]) at--;
                    if (at >= max) continue;
                    var last = n < max ? n : max - 1;
                    for (int k = last; k > at; k--) { _sel[b + k] = _sel[b + k - 1]; _tti[b + k] = _tti[b + k - 1]; }
                    _sel[b + at] = o;
                    _tti[b + at] = x;
                    if (n < max) _count[t] = n + 1;
                }
            }
        }

        /// <summary>자기 골문에 가장 가까운 필드 선수 (오프사이드 라인을 정하는 수비수, 동점 = 낮은 슬롯).</summary>
        static int LastDefender(DecisionWorld w, int team)
        {
            var dir = w.AttackDir[team];
            var best = -1;
            var bestX = float.MaxValue;
            for (int o = team * DecisionWorld.PerTeam; o < (team + 1) * DecisionWorld.PerTeam; o++)
            {
                if (!w.Active[o] || DecisionWorld.IsKeeper(o)) continue;
                var x = w.Pos[o].X * dir;
                if (x < bestX) { bestX = x; best = o; }
            }
            return best;
        }

        static bool InOwnBox(DecisionWorld w, int team, Vec2 p)
        {
            var goal = w.OwnGoal(team);
            var depth = (p.X - goal.X) * w.AttackDir[team];
            return depth >= 0f && depth <= PhysicsStage.BoxDepth && DMath.Abs(p.Y - goal.Y) <= PhysicsStage.BoxHalfWidth;
        }
    }
}
