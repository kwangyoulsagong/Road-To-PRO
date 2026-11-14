using RoadToPro.Core.Hashing;
using RoadToPro.Core.Serialization;

namespace RoadToPro.MatchEngine.Actions
{
    /// <summary>접촉 종류 (specs/070 FR-13). <c>SimEventKind.Contact</c>의 A. 번호는 데이터에 들어가므로 바꾸지 않는다.</summary>
    public enum ContactKind : byte
    {
        Tackle = 0,
        TakeOn = 1,
        /// <summary>공중볼 경합 (071이 같은 형식으로 낸다).</summary>
        Aerial = 2,
        Shield = 3,
        Collision = 4,
        /// <summary>팔 영역 접촉 (FR-21 핸드볼 입력만 의미 있음).</summary>
        Arm = 5,
    }

    /// <summary>
    /// 접촉 상세 (specs/070 FR-13·21). 012(파울·징계·핸드볼)·027(부상 위험)이 읽는다. 0~1 필드는 항상 [0, 1], NaN 없음.
    /// 득점 기회 저지 스냅샷을 만들 수 없으면(피치 밖) <see cref="DogsoValid"/> = false이고 그 필드는 0.
    /// </summary>
    public struct ContactDetail
    {
        public long Tick;
        public sbyte Substep;
        public ContactKind Kind;
        /// <summary>공격자(공을 가졌거나 다투는 쪽) 슬롯·수비자 슬롯.</summary>
        public int Attacker, Defender;
        /// <summary>수비자가 공을 먼저 건드렸는지 (0~1).</summary>
        public float BallFirst;
        /// <summary>강도 (0~1, 상대 속도 × 유효 질량).</summary>
        public float Intensity;
        public float FromBehind;
        public float Lateness;
        public bool Sliding;
        /// <summary>발바닥 노출 (0~1) [대체: 슬라이딩 × (0.5 + 0.5·뒤에서)].</summary>
        public float StudsUp;
        /// <summary>접촉 높이 (m).</summary>
        public float Height;
        public float X, Y;
        /// <summary>수비 팀 자기 페널티 박스 안.</summary>
        public bool InBox;
        public bool DogsoValid;
        public float GoalDist, GoalAngle;
        public int DefendersBetween;
        /// <summary>공격자가 공을 컨트롤할 가능성 (0~1).</summary>
        public float ControlLikely;
        /// <summary>버릇 부가 효과 비트 (bit0 슬라이딩 장인 — 012가 파울 ×1.4 적용, 003 FR-21).</summary>
        public byte HabitBits;
        public bool HasHand;
        public float HandSilhouette, HandBallDist, HandReactTime;
        public bool HandSelfDeflect;

        public const byte HabitSlideMaster = 1;

        public void AddTo(ref StableHasher h)
        {
            h.Add(Tick); h.Add(Substep); h.Add((byte)Kind); h.Add(Attacker); h.Add(Defender);
            h.Add(BallFirst); h.Add(Intensity); h.Add(FromBehind); h.Add(Lateness); h.Add(Sliding); h.Add(StudsUp); h.Add(Height);
            h.Add(X); h.Add(Y); h.Add(InBox); h.Add(DogsoValid); h.Add(GoalDist); h.Add(GoalAngle); h.Add(DefendersBetween); h.Add(ControlLikely);
            h.Add(HabitBits); h.Add(HasHand); h.Add(HandSilhouette); h.Add(HandBallDist); h.Add(HandReactTime); h.Add(HandSelfDeflect);
        }

        public void Write(StateWriter w)
        {
            w.Write(Tick); w.Write((byte)Substep); w.Write((byte)Kind); w.Write(Attacker); w.Write(Defender);
            w.Write(BallFirst); w.Write(Intensity); w.Write(FromBehind); w.Write(Lateness); w.Write(Sliding); w.Write(StudsUp); w.Write(Height);
            w.Write(X); w.Write(Y); w.Write(InBox); w.Write(DogsoValid); w.Write(GoalDist); w.Write(GoalAngle); w.Write(DefendersBetween); w.Write(ControlLikely);
            w.Write(HabitBits); w.Write(HasHand); w.Write(HandSilhouette); w.Write(HandBallDist); w.Write(HandReactTime); w.Write(HandSelfDeflect);
        }

        public static ContactDetail Read(StateReader r)
        {
            var c = new ContactDetail();
            c.Tick = r.ReadLong(); c.Substep = (sbyte)r.ReadByte(); c.Kind = (ContactKind)r.ReadByte(); c.Attacker = r.ReadInt(); c.Defender = r.ReadInt();
            c.BallFirst = r.ReadFloat(); c.Intensity = r.ReadFloat(); c.FromBehind = r.ReadFloat(); c.Lateness = r.ReadFloat(); c.Sliding = r.ReadBool();
            c.StudsUp = r.ReadFloat(); c.Height = r.ReadFloat();
            c.X = r.ReadFloat(); c.Y = r.ReadFloat(); c.InBox = r.ReadBool(); c.DogsoValid = r.ReadBool(); c.GoalDist = r.ReadFloat(); c.GoalAngle = r.ReadFloat();
            c.DefendersBetween = r.ReadInt(); c.ControlLikely = r.ReadFloat();
            c.HabitBits = r.ReadByte(); c.HasHand = r.ReadBool(); c.HandSilhouette = r.ReadFloat(); c.HandBallDist = r.ReadFloat(); c.HandReactTime = r.ReadFloat();
            c.HandSelfDeflect = r.ReadBool();
            return c;
        }
    }
}
