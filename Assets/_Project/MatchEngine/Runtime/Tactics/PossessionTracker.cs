using RoadToPro.Core.Hashing;
using RoadToPro.Core.Serialization;

namespace RoadToPro.MatchEngine.Tactics
{
    /// <summary>
    /// 점유 팀·확정 여부·변경 시각 (FR-02·22). 공 소유자가 있으면 확정, 같은 팀이 찬 공이 날아가는 동안은 점유 유지,
    /// 그 외(공 소유자 없음)는 루즈볼이다.
    /// </summary>
    public sealed class PossessionTracker
    {
        public int Team { get; private set; } = -1;
        public int PreviousTeam { get; private set; } = -1;
        public bool Confirmed { get; private set; }
        public long ChangeTick { get; private set; } = -1;
        public long LooseSinceTick { get; private set; } = -1;

        public void Update(long tick, int ownerTeam, int flightTeam)
        {
            if (ownerTeam >= 0)
            {
                Confirmed = true;
                LooseSinceTick = -1;
                if (Team != ownerTeam)
                {
                    PreviousTeam = Team;
                    Team = ownerTeam;
                    ChangeTick = tick;
                }
                return;
            }
            if (flightTeam >= 0 && flightTeam == Team)
            {
                Confirmed = true;
                LooseSinceTick = -1;
                return;
            }
            Confirmed = false;
            if (LooseSinceTick < 0) LooseSinceTick = tick;
        }

        public long LooseTicks(long tick) => LooseSinceTick < 0 ? 0 : tick - LooseSinceTick;

        public void Write(StateWriter w) { w.Write(Team); w.Write(PreviousTeam); w.Write(Confirmed); w.Write(ChangeTick); w.Write(LooseSinceTick); }
        public void Read(StateReader r) { Team = r.ReadInt(); PreviousTeam = r.ReadInt(); Confirmed = r.ReadBool(); ChangeTick = r.ReadLong(); LooseSinceTick = r.ReadLong(); }
        public void AddTo(ref StableHasher h) { h.Add(Team); h.Add(PreviousTeam); h.Add(Confirmed); h.Add(ChangeTick); h.Add(LooseSinceTick); }
    }
}
