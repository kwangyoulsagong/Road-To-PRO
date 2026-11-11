namespace RoadToPro.MatchEngine.Goalkeeper
{
    /// <summary>
    /// GK 동작 목록 (specs/011 FR-90, 073·074 동작 포함). 043(FR-31)·054(FR-44)에 넘기는 목록이고 GK 접촉 이벤트의 A 값이다.
    /// 번호는 세이브·리플레이에 들어가므로 바꾸지 않는다. 다이빙 높이·방향은 이벤트의 방향·높이 구간 필드가 따로 담는다.
    /// </summary>
    public enum KeeperMotionKind : byte
    {
        None = 0,
        ReadyStance = 1,
        Shuffle = 2,
        Dive = 3,
        CatchGround = 4,
        CatchChest = 5,
        CatchOverhead = 6,
        ParrySide = 7,
        ParryFront = 8,
        TipOverBar = 9,
        PunchOneHand = 10,
        PunchTwoHands = 11,
        /// <summary>073: 크로스 클레임 점프.</summary>
        ClaimJump = 12,
        /// <summary>073: 1v1 덮치기.</summary>
        Smother = 13,
        /// <summary>073: 1v1 스프레드.</summary>
        Spread = 14,
        GetUp = 15,
        /// <summary>074: PK 조기 이동.</summary>
        PenaltyEarlyMove = 16,
        /// <summary>074 배급.</summary>
        DistributeRoll = 17,
        DistributeOverarm = 18,
        DistributeGoalKick = 19,
        DistributePunt = 20,
        DistributeDropKick = 21,
    }

    /// <summary>다이빙 높이 구간 (FR-17).</summary>
    public enum DiveHeight : byte
    {
        Low = 0,
        Mid = 1,
        High = 2,
    }
}
