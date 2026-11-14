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
        /// <summary>전술 변경 적용 (FR-71). A = 지시 번호, 포메이션 변경은 −1. F0 = 새 값.</summary>
        TacticsChanged = 102,
        /// <summary>지시 값 보정 경고 (FR-70, AC-14). A = 지시 번호.</summary>
        TacticsInputClamped = 103,
        /// <summary>시나리오 배역 과다 경고 (형태 인원 6명 미만).</summary>
        ShapeCastOverflow = 104,

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
        /// <summary>
        /// specs/070 FR-13 접촉 (파울 여부와 무관): A = <c>Actions.ContactKind</c>, B = 접촉 상세 번호 + 1 (<c>ActionDetailLog</c>, 0 = 없음),
        /// PlayerId = 공격자(공을 가진/다투는 쪽), OtherId = 수비자, F0 = 강도(0~1), F1 = 늦은 정도(0~1). 소비자: 012(파울·징계), 027(부상 위험).
        /// </summary>
        Contact = 214,

        // 011 (300~): 골키퍼
        Save = 300,
        Catch = 301,
        Punch = 302,
        KeeperRush = 303,
        /// <summary>다이빙 시작 (011 FR-91·92). Substep = 반응 시작, X/Y/Z = 예정 접촉 지점, A = 방향(−1·0·+1)×10 + 높이 구간(0~2), F0 = 예정 접촉까지 시간(s), F1 = t_react_eff.</summary>
        DiveStart = 304,
        /// <summary>쳐내기 (011 FR-92). B 비트 <see cref="EventCodes.ParryDanger"/> = 위험한 쳐내기(리바운드).</summary>
        Parry = 305,
        /// <summary>073 예약: 1v1 덮치기.</summary>
        Smother = 306,
        /// <summary>073 예약: 크로스 클레임 실패.</summary>
        ClaimFail = 307,
        /// <summary>074 예약: GK 배급.</summary>
        Distribution = 308,
        /// <summary>다이빙 뒤 일어나기 끝 (011 FR-17·92).</summary>
        KeeperRecovered = 309,

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
        /// <summary>specs/058 FR-04·08: 데드볼 공 재배치 (골당·재시작당 최대 1회). A = 사유(<see cref="EventCodes"/>.Relocate*). 위치 = 새 지점.</summary>
        BallRelocated = 412,
        /// <summary>specs/058 FR-02·07: 데드볼 단계 전환. A = <c>DeadBallPhase</c>.</summary>
        DeadBallPhase = 413,
        /// <summary>specs/058 FR-03 진단: 상한에 도달했지만 킥오프 규칙 위반 선수가 남아 킥오프를 미룬다. PlayerId = 위반 선수 (첫 1건).</summary>
        RestartHeld = 414,
        /// <summary>specs/058 FR-61·65: 걸어 나간 선수가 피치를 벗어났다. A = 사유(<see cref="EventCodes"/>.Leave*).</summary>
        PlayerLeftPitch = 415,
        /// <summary>specs/058 FR-61: 교체 투입 선수가 터치라인에서 들어오기 시작했다. OtherId = 나간 선수.</summary>
        PlayerEntered = 416,

        // 027 (500~): 부상
        Injury = 500,

        // 013 (600~), 003 (700~), 019 (800~)
        /// <summary>A = 체이닝 깊이, B = 플래그(1 강제, 2 반전). ScenarioId = <c>MatchResult.ScenarioIds</c> 번호 (FR-50).</summary>
        ScenarioStart = 600,
        /// <summary>배역 ↔ 선수. A = 배역 번호, B = 슬롯.</summary>
        ScenarioCast = 601,
        /// <summary>A = 배역 × 100 + 단계, B = StepOutcome.</summary>
        ScenarioStepEnd = 602,
        /// <summary>A = 배역 × 100 + 단계 (상황 분기는 −1), B = BranchTargetKind.</summary>
        ScenarioBranch = 603,
        /// <summary>A = 다음 시나리오 번호, B = 체이닝 깊이.</summary>
        ScenarioChain = 604,
        /// <summary>A = 체이닝 깊이, B = ScenarioEndReason.</summary>
        ScenarioEnd = 605,
        /// <summary>강제 선택 거부 (FR-53). B = 사유: 1 출시 설정, 2 없는 id, 3 배역 미충족.</summary>
        ScenarioForceRejected = 606,
        /// <summary>세트피스 재개 (FR-46).</summary>
        ScenarioResume = 607,
        /// <summary>실행 중 배역 해제 (이탈·회수, FR-50). A = 배역 번호, B = 슬롯.</summary>
        ScenarioCastReleased = 608,
        InstructionBreach = 700,
        KeyMoment = 800,

        // 010 (900~): 판단 AI
        /// <summary>기회주의 이탈로 배역 해제 (010 FR-94). A = 배역 번호, B = 슬롯. 같은 틱에 <see cref="ScenarioCastReleased"/>도 나간다.</summary>
        IntentOpportunistic = 900,

        // 072 (901~): 온볼 판단
        /// <summary>지시 무시 (072 FR-31·32, 003 FR-42 기준). A = 억제된 고려 항목(<c>Consideration</c>), B = 슬롯, F0 = 실효 지시 가중치. 결과(성공·실패)는 같은 선수의 다음 공 사건으로 025가 묶는다.</summary>
        InstructionIgnored = 901,
        /// <summary>넘어지기 선택 (072 FR-20 → 012 FR-27 입력). A = 접촉 강도 구간(0 = 접촉 없음, 1 = 약한 접촉), B = 슬롯, X/Y = 위치, F0 = 파울 유도 기대 가치.</summary>
        DiveAttempt = 902,
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
        /// <summary>돌파 이벤트(<see cref="SimEventKind.Dribble"/>) B 비트: 성공 (072 대체 1:1 판정, 070 전).</summary>
        public const int DribbleSuccess = 1;
        /// <summary>태클 이벤트(<see cref="SimEventKind.Tackle"/>) A 비트 (specs/070): 슬라이딩.</summary>
        public const int TackleSliding = 1;
        /// <summary>태클 이벤트 A 비트 (specs/070): 공을 따냄(잡기·찔러내기). 없으면 빗나감 — 058 동작은 실패 태클로 읽을 수 있다.</summary>
        public const int TackleWon = 2;
        /// <summary>태클 이벤트 A 비트 (specs/070): 공이 떠난 뒤 들어간 늦은 태클 (공 획득 없음).</summary>
        public const int TackleLate = 4;

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

        // 공 재배치 사유 (SimEvent.A on BallRelocated, specs/058)
        public const int RelocateGoal = 1;
        public const int RelocateRestart = 2;

        // 피치 이탈 사유 (SimEvent.A on PlayerLeftPitch, specs/058)
        public const int LeaveSubstituted = 1;
        public const int LeaveSentOff = 2;
        public const int LeaveInjured = 3;

        // 골키퍼 (specs/011): Parry·Catch·Punch의 B 비트, Goal의 B 비트
        /// <summary>B 하위 2비트 = 높이 구간(0 낮음 · 1 중간 · 2 높음).</summary>
        public const int HeightBandMask = 3;
        /// <summary>Parry B: 위험한 쳐내기(골문 앞 리바운드, FR-12).</summary>
        public const int ParryDanger = 4;
        /// <summary>Goal B: GK 손끝이 닿고 들어감 (FR-92, 054 FR-44).</summary>
        public const int Fingertip = 8;
    }
}
