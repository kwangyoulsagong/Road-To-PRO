using RoadToPro.Core.Hashing;
using RoadToPro.Core.Math;
using RoadToPro.Core.Serialization;
using RoadToPro.MatchEngine.Match.Flow;

namespace RoadToPro.MatchEngine.Referee
{
    /// <summary>늦은 깃발 보류 중인 오프사이드 (specs/012 FR-08, P4): 부심은 오프사이드로 봤지만 근소라 공격 단계가 끝날 때까지 깃발을 미룬다.</summary>
    public struct HeldFlag
    {
        public bool Active;
        public int Team, Slot, PlayerId;
        public byte Type;
        public float D, Perceived;
        public Vec2 Spot;
        public long Tick;

        public void AddTo(ref StableHasher h)
        {
            h.Add(Active); h.Add(Team); h.Add(Slot); h.Add(PlayerId); h.Add(Type); h.Add(D); h.Add(Perceived); h.Add(Spot.X); h.Add(Spot.Y); h.Add(Tick);
        }

        public void Write(StateWriter w)
        {
            w.Write(Active); w.Write(Team); w.Write(Slot); w.Write(PlayerId); w.Write(Type); w.Write(D); w.Write(Perceived); w.Write(Spot); w.Write(Tick);
        }

        public static HeldFlag Read(StateReader r) => new HeldFlag
        {
            Active = r.ReadBool(), Team = r.ReadInt(), Slot = r.ReadInt(), PlayerId = r.ReadInt(), Type = r.ReadByte(), D = r.ReadFloat(), Perceived = r.ReadFloat(),
            Spot = r.ReadVec2(), Tick = r.ReadLong(),
        };
    }

    /// <summary>
    /// 심판 단계 상태 모음 (specs/012 T21): 오프사이드 추적·보류 깃발·어드밴티지 큐·징계·공격 단계 기록·GK 보유·직전 재시작 종류·7명 미만 보고.
    /// 체크포인트·해시 대상 (006 FR-60). 텔레메트리는 측정용이라 밖.
    /// </summary>
    public sealed class RefereeState
    {
        public readonly OffsideTracker Offside = new OffsideTracker();
        public HeldFlag Held;
        public readonly AdvantageQueue Advantage = new AdvantageQueue();
        public readonly Discipline Discipline = new Discipline();
        public readonly AttackPhaseLog Phase = new AttackPhaseLog();
        /// <summary>GK 손 보유 시작 틱 (팀별, −1 = 없음)과 그 GK 슬롯.</summary>
        public readonly long[] HoldStart = { -1, -1 };
        public readonly int[] HoldSlot = { -1, -1 };
        /// <summary>직전 틱이 끝날 때 데드볼 재시작 종류 — 이번 틱 첫 패스가 그 재시작 킥이다 (FR-03 면제).</summary>
        public RestartKind PrevRestart;
        /// <summary>점유 팀 (공격 단계 구분, −1 = 모름).</summary>
        public int PhaseTeam = -1;
        public bool BelowReported;
        /// <summary>FR-64 백패스 입력: 마지막 패스 팀·패서·헤딩 여부 (다른 터치가 끼면 팀 = −1).</summary>
        public int LastPassTeam = -1, LastPasser = -1;
        public bool LastPassHeaded;
        /// <summary>마지막 판정 시야 품질 (진단).</summary>
        public float LastSight = 1f;

        public void Write(StateWriter w)
        {
            Offside.Write(w);
            Held.Write(w);
            Advantage.Write(w);
            Discipline.Write(w);
            Phase.Write(w);
            w.Write(HoldStart[0]); w.Write(HoldStart[1]); w.Write(HoldSlot[0]); w.Write(HoldSlot[1]);
            w.Write((byte)PrevRestart);
            w.Write(PhaseTeam);
            w.Write(BelowReported);
            w.Write(LastPassTeam); w.Write(LastPasser); w.Write(LastPassHeaded);
            w.Write(LastSight);
        }

        public void Read(StateReader r)
        {
            Offside.Read(r);
            Held = HeldFlag.Read(r);
            Advantage.Read(r);
            Discipline.Read(r);
            Phase.Read(r);
            HoldStart[0] = r.ReadLong(); HoldStart[1] = r.ReadLong(); HoldSlot[0] = r.ReadInt(); HoldSlot[1] = r.ReadInt();
            PrevRestart = (RestartKind)r.ReadByte();
            PhaseTeam = r.ReadInt();
            BelowReported = r.ReadBool();
            LastPassTeam = r.ReadInt(); LastPasser = r.ReadInt(); LastPassHeaded = r.ReadBool();
            LastSight = r.ReadFloat();
        }

        public void AddTo(ref StableHasher h)
        {
            Offside.AddTo(ref h);
            Held.AddTo(ref h);
            Advantage.AddTo(ref h);
            Discipline.AddTo(ref h);
            Phase.AddTo(ref h);
            h.Add(HoldStart[0]); h.Add(HoldStart[1]); h.Add(HoldSlot[0]); h.Add(HoldSlot[1]);
            h.Add((byte)PrevRestart);
            h.Add(PhaseTeam);
            h.Add(BelowReported);
            h.Add(LastPassTeam); h.Add(LastPasser); h.Add(LastPassHeaded);
            h.Add(LastSight);
        }
    }
}
