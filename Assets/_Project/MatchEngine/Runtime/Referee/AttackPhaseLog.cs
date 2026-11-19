using RoadToPro.Core.Hashing;
using RoadToPro.Core.Math;
using RoadToPro.Core.Serialization;

namespace RoadToPro.MatchEngine.Referee
{
    /// <summary>VAR 대상 구간 사건 종류 (specs/012 FR-71, 플랜 AttackPhaseLog).</summary>
    public enum PhaseIncidentKind : byte
    {
        None = 0,
        /// <summary>놓친 오프사이드 (부심 지각 ≤ 0, 실제 d &gt; 0).</summary>
        MissedOffside = 1,
        /// <summary>휘슬 안 분 진짜 파울 (공격 팀이 범함 — 골 취소 대상).</summary>
        MissedAttackFoul = 2,
        /// <summary>공격 팀 핸드볼 (득점 직전 우연 포함).</summary>
        AttackHandball = 3,
        /// <summary>휘슬 안 분 수비 팀 박스 안 반칙 (VAR PK 선언 대상).</summary>
        MissedPenaltyFoul = 4,
        /// <summary>휘슬 안 분 진짜 퇴장급 반칙 (소급 퇴장 대상).</summary>
        MissedRed = 5,
    }

    /// <summary>공격 단계 사건 한 건.</summary>
    public struct PhaseIncident
    {
        public PhaseIncidentKind Kind;
        public long Tick;
        /// <summary>이득을 본 팀(골·PK 판단 기준 = 공격 팀).</summary>
        public int AttackTeam;
        public int Offender, OffenderId, Victim, VictimId;
        public Vec2 Spot;
        /// <summary>진짜 값(파울 점수 또는 오프사이드 d)·인지 값·진짜 판정 단계.</summary>
        public float TrueValue, Perceived;
        public byte TrueDecision;
        public bool Tight;
    }

    /// <summary>
    /// 공격 단계(마지막 점유 변경 이후) 사건 기록 — VAR이 득점·PK·퇴장 직후 되짚는 범위 (specs/012 FR-71, 플랜 AttackPhaseLog).
    /// 고정 링 버퍼 16칸. 점유 변경·재시작 때 <see cref="Clear"/>. RNG 없음.
    /// </summary>
    public sealed class AttackPhaseLog
    {
        public const int Capacity = 16;
        readonly PhaseIncident[] _items = new PhaseIncident[Capacity];
        int _start, _count;

        public int Count => _count;
        public ref readonly PhaseIncident this[int i] => ref _items[(_start + i) % Capacity];

        public void Add(in PhaseIncident x)
        {
            if (_count == Capacity) { _start = (_start + 1) % Capacity; _count--; }
            _items[(_start + _count) % Capacity] = x;
            _count++;
        }

        public void Clear() { _start = 0; _count = 0; }

        /// <summary>attackTeam 이득 쪽 첫 사건 (종류 일치, 오래된 순). 없으면 −1.</summary>
        public int Find(PhaseIncidentKind kind, int attackTeam)
        {
            for (int i = 0; i < _count; i++)
            {
                ref readonly var x = ref this[i];
                if (x.Kind == kind && x.AttackTeam == attackTeam) return i;
            }
            return -1;
        }

        public void Write(StateWriter w)
        {
            w.Write(_count);
            for (int i = 0; i < _count; i++)
            {
                ref readonly var x = ref this[i];
                w.Write((byte)x.Kind); w.Write(x.Tick); w.Write(x.AttackTeam); w.Write(x.Offender); w.Write(x.OffenderId); w.Write(x.Victim); w.Write(x.VictimId);
                w.Write(x.Spot); w.Write(x.TrueValue); w.Write(x.Perceived); w.Write(x.TrueDecision); w.Write(x.Tight);
            }
        }

        public void Read(StateReader r)
        {
            _start = 0;
            _count = r.ReadInt();
            for (int i = 0; i < _count; i++)
                _items[i] = new PhaseIncident
                {
                    Kind = (PhaseIncidentKind)r.ReadByte(), Tick = r.ReadLong(), AttackTeam = r.ReadInt(), Offender = r.ReadInt(), OffenderId = r.ReadInt(), Victim = r.ReadInt(), VictimId = r.ReadInt(),
                    Spot = r.ReadVec2(), TrueValue = r.ReadFloat(), Perceived = r.ReadFloat(), TrueDecision = r.ReadByte(), Tight = r.ReadBool(),
                };
        }

        public void AddTo(ref StableHasher h)
        {
            h.Add(_count);
            for (int i = 0; i < _count; i++)
            {
                ref readonly var x = ref this[i];
                h.Add((byte)x.Kind); h.Add(x.Tick); h.Add(x.AttackTeam); h.Add(x.Offender); h.Add(x.OffenderId); h.Add(x.Victim); h.Add(x.VictimId);
                h.Add(x.Spot.X); h.Add(x.Spot.Y); h.Add(x.TrueValue); h.Add(x.Perceived); h.Add(x.TrueDecision); h.Add(x.Tight);
            }
        }
    }
}
