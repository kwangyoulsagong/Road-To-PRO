using RoadToPro.Core.Hashing;
using RoadToPro.Core.Serialization;

namespace RoadToPro.MatchEngine.Actions
{
    /// <summary>
    /// 공중볼 경합 참여자 1명의 동작 정보 (specs/071 FR-06, 008 FR-72 상세 기록 — 소비자 054 FR-43).
    /// 도약·정점은 절대 서브스텝, 정점 높이 = 점프 높이(m). 한 경합의 참여자는 연속 번호로 저장하고 첫 번호에 묶음 수를 둔다.
    /// </summary>
    public struct AerialDetail
    {
        public long Tick;
        public sbyte Substep;
        public int Slot;
        public byte Team;
        public long JumpSubstep, ApexSubstep, LandSubstep;
        public float ApexHeight;
        public float Reach, Timing, WinProb;
        public bool Won;
        /// <summary>이 경합 첫 기록 번호와 참여자 수.</summary>
        public int GroupFirst;
        public byte GroupCount;

        public void AddTo(ref StableHasher h)
        {
            h.Add(Tick); h.Add(Substep); h.Add(Slot); h.Add(Team);
            h.Add(JumpSubstep); h.Add(ApexSubstep); h.Add(LandSubstep); h.Add(ApexHeight);
            h.Add(Reach); h.Add(Timing); h.Add(WinProb); h.Add(Won); h.Add(GroupFirst); h.Add(GroupCount);
        }

        public void Write(StateWriter w)
        {
            w.Write(Tick); w.Write(Substep); w.Write(Slot); w.Write(Team);
            w.Write(JumpSubstep); w.Write(ApexSubstep); w.Write(LandSubstep); w.Write(ApexHeight);
            w.Write(Reach); w.Write(Timing); w.Write(WinProb); w.Write(Won); w.Write(GroupFirst); w.Write(GroupCount);
        }

        public static AerialDetail Read(StateReader r) => new AerialDetail
        {
            Tick = r.ReadLong(), Substep = r.ReadSByte(), Slot = r.ReadInt(), Team = r.ReadByte(),
            JumpSubstep = r.ReadLong(), ApexSubstep = r.ReadLong(), LandSubstep = r.ReadLong(), ApexHeight = r.ReadFloat(),
            Reach = r.ReadFloat(), Timing = r.ReadFloat(), WinProb = r.ReadFloat(), Won = r.ReadBool(), GroupFirst = r.ReadInt(), GroupCount = r.ReadByte(),
        };
    }
}
