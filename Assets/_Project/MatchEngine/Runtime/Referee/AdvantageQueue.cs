using RoadToPro.Core.Hashing;
using RoadToPro.Core.Math;
using RoadToPro.Core.Serialization;

namespace RoadToPro.MatchEngine.Referee
{
    /// <summary>보류 반칙 한 건 (specs/012 FR-40~43). 값 타입, 고정 배열에 저장.</summary>
    public struct PendingFoul
    {
        public long Tick;
        public int Offender, Victim;
        public int OffenderId, VictimId;
        public int VictimTeam;
        public Vec2 Spot;
        /// <summary>내린 판정 단계(DOGSO 반영 전) · DOGSO 반영 뒤 단계 · 카드 사유.</summary>
        public byte Decision, FinalDecision, Reason;
        public bool InBox, Dogso;
        public float Score;

        public void AddTo(ref StableHasher h)
        {
            h.Add(Tick); h.Add(Offender); h.Add(Victim); h.Add(OffenderId); h.Add(VictimId); h.Add(VictimTeam); h.Add(Spot.X); h.Add(Spot.Y);
            h.Add(Decision); h.Add(FinalDecision); h.Add(Reason); h.Add(InBox); h.Add(Dogso); h.Add(Score);
        }

        public void Write(StateWriter w)
        {
            w.Write(Tick); w.Write(Offender); w.Write(Victim); w.Write(OffenderId); w.Write(VictimId); w.Write(VictimTeam); w.Write(Spot);
            w.Write(Decision); w.Write(FinalDecision); w.Write(Reason); w.Write(InBox); w.Write(Dogso); w.Write(Score);
        }

        public static PendingFoul Read(StateReader r) => new PendingFoul
        {
            Tick = r.ReadLong(), Offender = r.ReadInt(), Victim = r.ReadInt(), OffenderId = r.ReadInt(), VictimId = r.ReadInt(), VictimTeam = r.ReadInt(), Spot = r.ReadVec2(),
            Decision = r.ReadByte(), FinalDecision = r.ReadByte(), Reason = r.ReadByte(), InBox = r.ReadBool(), Dogso = r.ReadBool(), Score = r.ReadFloat(),
        };
    }

    /// <summary>보류 창 판정 결과.</summary>
    public enum AdvantageResult : byte
    {
        None = 0,
        /// <summary>창 안 이득 유지 → 보류 해제 (징계는 다음 정지 때).</summary>
        Cleared = 1,
        /// <summary>창 안 이득 소멸 → 원래 지점 반칙 재시작.</summary>
        Lost = 2,
        /// <summary>창 안 반칙당한 팀 골 → 골 인정 + 징계.</summary>
        Goal = 3,
    }

    /// <summary>
    /// 어드밴티지 보류 큐 (specs/012 FR-40~43, 플랜 P5). 고정 4칸, 먼저 들어온 순. RNG 없음.
    /// 이득 점수 = 점유 유지 × clamp(base + 위협도), 기준 = 1 − 심판 어드밴티지 선호.
    /// </summary>
    public sealed class AdvantageQueue
    {
        public const int Capacity = 4;
        readonly PendingFoul[] _items = new PendingFoul[Capacity];
        int _count;

        public int Count => _count;
        public ref readonly PendingFoul this[int i] => ref _items[i];

        /// <summary>P5 이득 점수. kept = 반칙당한 팀이 공을 갖고 있다. goalDist = 공 → 상대 골 거리.</summary>
        public static float Score(RefereeParams p, bool kept, float goalDist)
        {
            if (!kept) return 0f;
            var threat = p.AdvantageThreatRange > 1e-3f ? 1f - goalDist / p.AdvantageThreatRange : 0f;
            var v = p.AdvantageBase + (threat > 0f ? threat : 0f);
            return v > 1f ? 1f : (v < 0f ? 0f : v);
        }

        /// <summary>FR-40: 어드밴티지를 줄지 (이득 점수 &gt; 1 − 선호).</summary>
        public static bool ShouldPlay(RefereeProfile r, float score) => score > 1f - r.Advantage;

        public bool Add(in PendingFoul f)
        {
            if (_count == Capacity) return false;
            _items[_count++] = f;
            return true;
        }

        /// <summary>같은 반칙한 선수의 보류 반칙이 이미 있는지 (FR-42 두 번째 반칙 → 둘 다 처리).</summary>
        public int IndexOfOffender(int offenderId)
        {
            for (int i = 0; i < _count; i++) if (_items[i].OffenderId == offenderId) return i;
            return -1;
        }

        public void RemoveAt(int i)
        {
            for (int k = i; k < _count - 1; k++) _items[k] = _items[k + 1];
            _count--;
        }

        public void Clear() => _count = 0;

        /// <summary>
        /// 가장 오래된 보류 반칙의 이번 틱 상태 (FR-41). ticksPerSecond = 10. 창 경계: 경과 ≥ 창 → 유지면 해제(정확히 3.0 s = 해제).
        /// lost = 반칙당한 팀이 공을 잃었다(상대 터치·상대 소유), goal = 반칙당한 팀 골, deadBall = 그 밖의 정지(아웃 등 — 이득 안 남 → 원래 반칙).
        /// </summary>
        public static AdvantageResult Evaluate(RefereeParams p, in PendingFoul f, long tick, int ticksPerSecond, bool lost, bool goal, bool deadBall)
        {
            if (goal) return AdvantageResult.Goal;
            var windowTicks = (long)(p.AdvantageWindow * ticksPerSecond + 0.5f);
            if (lost || deadBall) return tick - f.Tick < windowTicks || deadBall ? AdvantageResult.Lost : AdvantageResult.Cleared;
            return tick - f.Tick >= windowTicks ? AdvantageResult.Cleared : AdvantageResult.None;
        }

        public void Write(StateWriter w)
        {
            w.Write(_count);
            for (int i = 0; i < _count; i++) _items[i].Write(w);
        }

        public void Read(StateReader r)
        {
            _count = r.ReadInt();
            for (int i = 0; i < _count; i++) _items[i] = PendingFoul.Read(r);
        }

        public void AddTo(ref StableHasher h)
        {
            h.Add(_count);
            for (int i = 0; i < _count; i++) _items[i].AddTo(ref h);
        }
    }
}
