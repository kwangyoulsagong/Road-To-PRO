using RoadToPro.Core.Hashing;
using RoadToPro.Core.Math;
using RoadToPro.Core.Serialization;

namespace RoadToPro.MatchEngine.UtilityAI
{
    /// <summary>이동 강도 (FR-70). 값 순서 = 단계 순서.</summary>
    public enum Intensity : byte { Walk = 0, Jog = 1, Run = 2, Sprint = 3 }

    /// <summary>결정 출처 (FR-99). 0~3은 010 오프볼, 4~ 는 다른 경로(대체 온볼·GK·데드볼)가 정한 결정.</summary>
    public enum DecisionSource : byte
    {
        General = 0,
        IntentMatch = 1,
        IntentIgnored = 2,
        Opportunistic = 3,
        OnBall = 4,
        Keeper = 5,
        DeadBall = 6,
        KeyMoment = 7,
    }

    /// <summary>
    /// 선수 1명 결정 상태 (specs/010 FR-42~46·72·93·26). 직렬화·해시 대상. 값 타입, 할당 없음.
    /// </summary>
    public struct PlayerDecisionState
    {
        public bool HasAction;
        public Consideration Kind;
        public Vec2 Point;
        public int Target;
        public long StartTick;
        public Intensity Intensity;
        /// <summary>마지막으로 준수 굴림을 한 의도 단계 키 (없으면 −1).</summary>
        public int IntentKey;
        public bool Complied;
        public DecisionSource Source;
        /// <summary>집중 저하 종료 틱 (이 틱 전까지 저하 상태).</summary>
        public long LapseUntil;
        public long LastDecisionTick;
        /// <summary>정지 구간 안에서 서 있는 중 (FR-43a). 서 있는 지점 = <see cref="Point"/>.</summary>
        public bool Settled;

        public static PlayerDecisionState Initial => new PlayerDecisionState { Target = -1, IntentKey = -1, Intensity = Intensity.Jog, LapseUntil = long.MinValue / 2, LastDecisionTick = long.MinValue / 2 };

        public bool InLapse(long tick) => tick < LapseUntil;

        public void Write(StateWriter w)
        {
            w.Write(HasAction); w.Write((byte)Kind); w.Write(Point); w.Write(Target); w.Write(StartTick); w.Write((byte)Intensity);
            w.Write(IntentKey); w.Write(Complied); w.Write((byte)Source); w.Write(LapseUntil); w.Write(LastDecisionTick); w.Write(Settled);
        }

        public static PlayerDecisionState Read(StateReader r) => new PlayerDecisionState
        {
            HasAction = r.ReadBool(), Kind = (Consideration)r.ReadByte(), Point = r.ReadVec2(), Target = r.ReadInt(), StartTick = r.ReadLong(),
            Intensity = (Intensity)r.ReadByte(), IntentKey = r.ReadInt(), Complied = r.ReadBool(), Source = (DecisionSource)r.ReadByte(),
            LapseUntil = r.ReadLong(), LastDecisionTick = r.ReadLong(), Settled = r.ReadBool(),
        };

        public void AddTo(ref StableHasher h)
        {
            h.Add(HasAction); h.Add((byte)Kind); h.Add(Point.X); h.Add(Point.Y); h.Add(Target); h.Add(StartTick); h.Add((byte)Intensity);
            h.Add(IntentKey); h.Add(Complied); h.Add((byte)Source); h.Add(LapseUntil); h.Add(LastDecisionTick); h.Add(Settled);
        }
    }
}
