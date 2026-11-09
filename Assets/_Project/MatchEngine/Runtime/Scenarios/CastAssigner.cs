using RoadToPro.Core.Math;
using RoadToPro.MatchEngine.Match;

namespace RoadToPro.MatchEngine.Scenarios
{
    /// <summary>슬롯의 배역 상태.</summary>
    public enum CastBusy : byte
    {
        Free = 0,
        /// <summary>자기 팀 시나리오의 배역.</summary>
        OwnScenario = 1,
        /// <summary>상대 팀 시나리오가 준 배역. 자기 팀 시나리오가 뺏을 수 있다 (FR-32).</summary>
        OpponentRole = 2,
    }

    /// <summary>배역 배정 입력. 위치는 공과 같은 임의 좌표계면 되지만 측면 판정은 팀 관점 y를 쓴다.</summary>
    public sealed class CastFrame
    {
        public readonly bool[] Active = new bool[Squad.Slots];
        public readonly Vec2[] Position = new Vec2[Squad.Slots];
        public readonly PositionMask[] Role = new PositionMask[Squad.Slots];
        public readonly CastBusy[] Busy = new CastBusy[Squad.Slots];
        /// <summary>팀별 이미 배역을 가진 인원과 상한 (FR-34).</summary>
        public readonly int[] CastCount = new int[2], CastCap = new int[2];
        public Vec2 Ball;
        public int Owner = -1, Taker = -1;
    }

    /// <summary>배역 배정 (FR-30~34): 조건에 맞는 선수를 공에서 가까운 순으로. 동점은 슬롯 번호.</summary>
    public sealed class CastAssigner
    {
        readonly bool[] _used = new bool[Squad.Slots];

        /// <summary>slots에 배역별 슬롯(없으면 −1)을 쓴다. 필수 배역을 못 채우면 false (부분 실행 금지, FR-31).</summary>
        public bool Assign(ScenarioDefinition def, int team, CastFrame f, int[] slots)
        {
            System.Array.Clear(_used, 0, _used.Length);
            int oppTeam = 1 - team;
            int ownCount = 0, oppCount = 0;
            for (int r = 0; r < def.Cast.Length; r++)
            {
                var role = def.Cast[r];
                var slot = PickSlot(role, role.Opponent ? oppTeam : team, f, oppCount);
                slots[r] = slot;
                if (slot < 0)
                {
                    if (role.Required) return false;
                    continue;
                }
                _used[slot] = true;
                if (role.Opponent) oppCount++; else ownCount++;
            }
            return true;
        }

        int PickSlot(ScenarioRole role, int team, CastFrame f, int oppAssigned)
        {
            if (role.IsCarrier) return Eligible(role, f.Owner, team, f, oppAssigned) ? f.Owner : -1;
            if (role.IsTaker) return Eligible(role, f.Taker, team, f, oppAssigned) ? f.Taker : -1;
            int best = -1;
            float bestD = float.MaxValue;
            for (int s = team * Squad.SlotsPerTeam; s < (team + 1) * Squad.SlotsPerTeam; s++)
            {
                if (!Eligible(role, s, team, f, oppAssigned)) continue;
                var d = Vec2.DistanceSq(f.Position[s], f.Ball);
                if (d < bestD) { bestD = d; best = s; }
            }
            return best;
        }

        bool Eligible(ScenarioRole role, int s, int team, CastFrame f, int oppAssigned)
        {
            if (s < 0 || Squad.TeamOfSlot(s) != team || !f.Active[s] || _used[s]) return false;
            if ((role.Positions & f.Role[s]) == 0) return false;
            if (role.Opponent)
            {
                if (f.Busy[s] != CastBusy.Free) return false;
                if (f.CastCount[team] + oppAssigned >= f.CastCap[team]) return false;
            }
            else if (f.Busy[s] == CastBusy.OwnScenario) return false;

            var y = f.Position[s].Y;
            switch (role.Side)
            {
                case RoleSideFilter.Left: return y < 0.5f;
                case RoleSideFilter.Right: return y >= 0.5f;
                case RoleSideFilter.BallSide: return (y < 0.5f) == (f.Ball.Y < 0.5f);
                case RoleSideFilter.FarSide: return (y < 0.5f) != (f.Ball.Y < 0.5f);
                default: return true;
            }
        }
    }
}
