using RoadToPro.Core.Hashing;
using RoadToPro.Core.Math;
using RoadToPro.Core.Serialization;

namespace RoadToPro.MatchEngine.Goalkeeper
{
    /// <summary>다이빙 단계 (specs/011 FR-17).</summary>
    public enum DivePhase : byte
    {
        None = 0,
        /// <summary>반응 대기 (슛 뒤 t_react_eff 전). GK는 아직 005 운동학.</summary>
        Reacting = 1,
        /// <summary>몸을 날림 (외부 구동, 수평 변위 = R(τ)).</summary>
        Flying = 2,
        /// <summary>착지 뒤 누워 있다가 일어남 — 행동 불가.</summary>
        Down = 3,
    }

    /// <summary>
    /// GK 한 명의 다이빙 상태 (FR-17). 시각은 절대 서브스텝. 값 타입이라 스냅샷·해시에 그대로 들어간다.
    /// 다이빙하지 않는 선방(몸 앞 공)은 Reacting → 접촉 → None.
    /// </summary>
    public struct DiveMotion
    {
        public DivePhase Phase;
        public bool Dives;
        public long ReactSubstep;
        public long ContactSubstep;
        public long LandSubstep;
        public long RecoverSubstep;
        public Vec2 Velocity;
        public int Direction;
        public DiveHeight Height;

        public bool Busy => Phase == DivePhase.Flying || Phase == DivePhase.Down;

        /// <summary>
        /// 다이빙 수평 속도: 반응 시점 GK 위치에서 접촉 예정점까지(standOff = 몸 반폭 + r0을 뺀 거리)를 접촉 시각에 닿게, 상한 v_dive.
        /// R(τ) = r0 + v_dive·τ와 일치 — 몸 중심은 r0만큼 덜 가고 손이 나머지를 덮는다.
        /// </summary>
        public static Vec2 FlightVelocity(Vec2 from, Vec2 contact, float standOff, float diveSpeed, float timeToContact)
        {
            var d = contact - from;
            var len = d.Length();
            var need = len - standOff;
            if (need <= 0f || len < 1e-5f) return Vec2.Zero;
            var t = timeToContact > 1e-3f ? timeToContact : 1e-3f;
            var speed = need / t;
            if (speed > diveSpeed) speed = diveSpeed;
            return d * (speed / len);
        }

        public void Write(StateWriter w)
        {
            w.Write((byte)Phase); w.Write(Dives); w.Write(ReactSubstep); w.Write(ContactSubstep); w.Write(LandSubstep); w.Write(RecoverSubstep);
            w.Write(Velocity); w.Write(Direction); w.Write((byte)Height);
        }

        public static DiveMotion Read(StateReader r) => new DiveMotion
        {
            Phase = (DivePhase)r.ReadByte(), Dives = r.ReadBool(), ReactSubstep = r.ReadLong(), ContactSubstep = r.ReadLong(), LandSubstep = r.ReadLong(),
            RecoverSubstep = r.ReadLong(), Velocity = r.ReadVec2(), Direction = r.ReadInt(), Height = (DiveHeight)r.ReadByte(),
        };

        public void AddTo(ref StableHasher h)
        {
            h.Add((byte)Phase); h.Add(Dives); h.Add(ReactSubstep); h.Add(ContactSubstep); h.Add(LandSubstep); h.Add(RecoverSubstep);
            h.Add(Velocity.X); h.Add(Velocity.Y); h.Add(Direction); h.Add((byte)Height);
        }
    }
}
