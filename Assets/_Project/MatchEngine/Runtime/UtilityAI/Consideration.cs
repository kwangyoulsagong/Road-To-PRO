namespace RoadToPro.MatchEngine.UtilityAI
{
    /// <summary>
    /// 행동 고려 항목 (specs/010 FR-03, 리서치 03 § A8). 0~31 = 오프볼 후보(010이 만든다), 32~ = 온볼 항목(072가 후보를 만들고 010 공통 틀로 점수를 낸다).
    /// 번호는 텔레메트리·데이터에 들어가므로 바꾸지 않는다.
    /// </summary>
    public enum Consideration : byte
    {
        HoldShape = 0,
        Support = 1,
        RunInBehind = 2,
        DropToReceive = 3,
        Overlap = 4,
        Underlap = 5,
        StayWide = 6,
        Invert = 7,
        BoxCrash = 8,
        LooseBall = 9,
        Press = 10,
        CoverShadow = 11,
        Mark = 12,
        Cover = 13,
        LineHold = 14,
        PassLaneBlock = 15,
        RecoveryRun = 16,

        // 온볼 (072) — 결합 배수 질의용
        Dribble = 32,
        Pass = 33,
        Cross = 34,
        CutIn = 35,
        LongShot = 36,
        HoldBall = 37,
        OneTouchPass = 38,
        ThroughBall = 39,
        SlideTackle = 40,
        Shot = 41,
        AssistPass = 42,
        Trick = 43,
        DiveForFoul = 44,
        Tackle = 45,
    }

    public static class Considerations
    {
        /// <summary>배열 크기 (최대 id + 1).</summary>
        public const int Count = 46;
        /// <summary>오프볼 후보 종류 수 (0 ~ RecoveryRun).</summary>
        public const int OffBallCount = 17;

        public static bool IsOffBall(Consideration k) => (int)k < OffBallCount;

        /// <summary>질주가 기본인 급한 행동 (FR-71 질주 비용이 이득에 붙는다).</summary>
        public static bool IsUrgent(Consideration k) =>
            k == Consideration.RunInBehind || k == Consideration.RecoveryRun || k == Consideration.LooseBall || k == Consideration.Press
            || k == Consideration.Overlap || k == Consideration.Underlap || k == Consideration.BoxCrash;

        /// <summary>공 쪽으로 가는 행동 (FR-15 몰림 억제 대상).</summary>
        public static bool IsBallward(Consideration k) =>
            k == Consideration.LooseBall || k == Consideration.Press || k == Consideration.Support;

        /// <summary>형태를 잠시 떠나는 침투류 — 이탈 상한(FR-85)으로 자르지 않는다.</summary>
        public static bool IsRun(Consideration k) =>
            k == Consideration.RunInBehind || k == Consideration.BoxCrash || k == Consideration.Overlap || k == Consideration.Underlap;

        /// <summary>수비 임무 — 이탈 상한(FR-85)을 받지 않는다.</summary>
        public static bool IsDefensiveDuty(Consideration k) =>
            k == Consideration.Press || k == Consideration.Mark || k == Consideration.LooseBall || k == Consideration.RecoveryRun || k == Consideration.CoverShadow;
    }
}
