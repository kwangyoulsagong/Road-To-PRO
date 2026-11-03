namespace RoadToPro.MatchEngine.Match.Events
{
    /// <summary>
    /// SimEvent 종류 (specs/006 FR-31). 번호 범위가 소유 스펙이다 — 006은 공통 스키마·순서·버전만 소유하고,
    /// 다른 스펙 종류는 대체 단계가 쓰는 것만 정의한다(나머지 번호는 예약). 번호는 세이브에 들어가므로 바꾸지 않는다 (FR-33).
    /// </summary>
    public enum SimEventKind : ushort
    {
        None = 0,

        // 006 (1~99): 경기 진행 · 교체 · 입력
        Kickoff = 1,
        PeriodStart = 2,
        PeriodEnd = 3,
        AddedTimeAnnounced = 4,
        MatchEnd = 5,
        MatchAbandoned = 6,
        MatchFailed = 7,
        Substitution = 8,
        SubstitutionRejected = 9,
        ShootoutKick = 10,
        ShootoutEnd = 11,
        InputApplied = 12,

        // 009 (100~): 점유 · 국면
        PossessionChange = 100,
        PhaseChange = 101,

        // 008 (200~): 액션 · 골
        Pass = 200,
        PassReceived = 201,
        Cross = 202,
        Dribble = 203,
        Carry = 204,
        Shot = 205,
        Block = 206,
        Tackle = 207,
        Interception = 208,
        Clearance = 209,
        Duel = 210,
        Goal = 211,
        WoodworkHit = 212,
        BallRecovered = 213,

        // 011 (300~): 골키퍼
        Save = 300,
        Catch = 301,
        Punch = 302,
        KeeperRush = 303,

        // 012 (400~): 심판 · 재시작
        Foul = 400,
        Card = 401,
        Offside = 402,
        ThrowIn = 403,
        GoalKick = 404,
        Corner = 405,
        FreeKick = 406,
        Penalty = 407,
        VarCheck = 408,
        VarOverturn = 409,
        PlayersBelowMinimum = 410,
        Stoppage = 411,

        // 027 (500~): 부상
        Injury = 500,

        // 013 (600~), 003 (700~), 019 (800~)
        ScenarioStart = 600,
        InstructionBreach = 700,
        KeyMoment = 800,
    }

    /// <summary>FR-82 공 접촉 사용 부위.</summary>
    public enum BodyPart : byte
    {
        None = 0,
        LeftFoot = 1,
        RightFoot = 2,
        Head = 3,
        Chest = 4,
        Thigh = 5,
        Hand = 6,
    }

    /// <summary>교체·종료 사유 등 이벤트 데이터 코드 (SimEvent.A).</summary>
    public static class EventCodes
    {
        // 교체 거부 사유
        public const int RejectLimit = 1;
        public const int RejectWindows = 2;
        public const int RejectReentry = 3;
        public const int RejectNotOnPitch = 4;
        public const int RejectNotOnBench = 5;
        public const int RejectConcussionUsed = 6;
        public const int RejectMatchOver = 7;

        // 교체 사유 (SimEvent.B)
        public const int SubNormal = 0;
        public const int SubConcussion = 1;

        // 카드 (SimEvent.A)
        public const int CardYellow = 1;
        public const int CardRed = 2;

        // 정지 요인 (SimEvent.A on Stoppage, StoppageClock)
        public const int StopGoal = 0;
        public const int StopSubstitution = 1;
        public const int StopInjury = 2;
        public const int StopVar = 3;
        public const int StopCard = 4;
        public const int StopDissent = 5;
        public const int StopKinds = 6;
    }
}
