using RoadToPro.Core.Hashing;
using RoadToPro.Core.Serialization;
using RoadToPro.MatchEngine.Ball;

namespace RoadToPro.MatchEngine.Match.Events
{
    /// <summary>
    /// 시뮬 이벤트 (specs/006 FR-30·82). 고정 크기 값 타입 — 로그는 미리 잡은 배열이라 틱 경로 할당이 없다.
    /// 정렬 키 (FR-32): (<see cref="Tick"/>, <see cref="Substep"/>, <see cref="Stage"/>, <see cref="Order"/>).
    /// Substep: −1 = 틱 시작(물리 전 단계), 0~9 = 물리 서브스텝, 10 = 틱 끝(심판 뒤 진행 판정).
    /// </summary>
    public struct SimEvent
    {
        public const sbyte TickStart = -1;
        public const sbyte TickEnd = 10;
        /// <summary>진행 판정(MatchFlow)의 가상 단계 번호 (단계 9 발행보다 뒤).</summary>
        public const byte FlowStage = 10;

        public long Seq;
        public long Tick;
        /// <summary>표기용 경기 시각 (001 FR-41): 기간(0 전반 … 3 연장 후반)과 기간 안 틱. <see cref="Time"/>.</summary>
        public byte Period;
        public int Clock;
        public sbyte Substep;
        public byte Stage;
        public ushort Order;
        public SimEventKind Kind;
        /// <summary>0 홈, 1 원정, −1 없음.</summary>
        public sbyte Team;
        public int PlayerId;
        public int OtherId;
        /// <summary>정규화 x·y (0~1, 기준 좌표 — 001 FR-20), 높이 z (m).</summary>
        public float X, Y, Z;
        public BallPhase BallPhase;
        public long RelatedSeq;
        public int ScenarioId;
        public BodyPart Part;
        /// <summary>종류별 데이터 (소유 스펙이 의미를 정함).</summary>
        public int A, B;
        public float F0, F1;

        public static SimEvent Make(SimEventKind kind, int team = -1, int player = -1, int other = -1)
        {
            return new SimEvent
            {
                Kind = kind,
                Team = (sbyte)team,
                PlayerId = player,
                OtherId = other,
                RelatedSeq = -1,
                ScenarioId = -1,
                Substep = TickStart,
            };
        }

        public Core.Time.MatchTime Time => new Core.Time.MatchTime((Core.Time.MatchPeriod)Period, Clock);

        /// <summary>정렬 비교 (Seq 제외).</summary>
        public static int CompareKey(in SimEvent a, in SimEvent b)
        {
            if (a.Tick != b.Tick) return a.Tick < b.Tick ? -1 : 1;
            if (a.Substep != b.Substep) return a.Substep < b.Substep ? -1 : 1;
            if (a.Stage != b.Stage) return a.Stage < b.Stage ? -1 : 1;
            if (a.Order != b.Order) return a.Order < b.Order ? -1 : 1;
            return 0;
        }

        public void AddTo(ref StableHasher h)
        {
            h.Add(Seq); h.Add(Tick); h.Add(Period); h.Add(Clock); h.Add((byte)Substep); h.Add(Stage); h.Add((uint)Order);
            h.Add((uint)Kind); h.Add((byte)Team); h.Add(PlayerId); h.Add(OtherId);
            h.Add(X); h.Add(Y); h.Add(Z); h.Add((byte)BallPhase); h.Add(RelatedSeq); h.Add(ScenarioId);
            h.Add((byte)Part); h.Add(A); h.Add(B); h.Add(F0); h.Add(F1);
        }

        public void Write(StateWriter w)
        {
            w.Write(Seq); w.Write(Tick); w.Write(Period); w.Write(Clock); w.Write(Substep); w.Write(Stage); w.Write(Order);
            w.Write((ushort)Kind); w.Write(Team); w.Write(PlayerId); w.Write(OtherId);
            w.Write(X); w.Write(Y); w.Write(Z); w.Write((byte)BallPhase); w.Write(RelatedSeq); w.Write(ScenarioId);
            w.Write((byte)Part); w.Write(A); w.Write(B); w.Write(F0); w.Write(F1);
        }

        public static SimEvent Read(StateReader r) => new SimEvent
        {
            Seq = r.ReadLong(), Tick = r.ReadLong(), Period = r.ReadByte(), Clock = r.ReadInt(), Substep = r.ReadSByte(), Stage = r.ReadByte(), Order = r.ReadUShort(),
            Kind = (SimEventKind)r.ReadUShort(), Team = r.ReadSByte(), PlayerId = r.ReadInt(), OtherId = r.ReadInt(),
            X = r.ReadFloat(), Y = r.ReadFloat(), Z = r.ReadFloat(), BallPhase = (BallPhase)r.ReadByte(), RelatedSeq = r.ReadLong(),
            ScenarioId = r.ReadInt(), Part = (BodyPart)r.ReadByte(), A = r.ReadInt(), B = r.ReadInt(), F0 = r.ReadFloat(), F1 = r.ReadFloat(),
        };

        public override string ToString() => $"#{Seq} t{Tick}.{Substep} s{Stage} {Kind} team {Team} p{PlayerId}/{OtherId} A{A} B{B}";
    }
}
