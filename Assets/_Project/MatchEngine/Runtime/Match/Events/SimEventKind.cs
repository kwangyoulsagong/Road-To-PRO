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
        /// <summary>
        /// specs/071 FR-05 공중볼 경합 승자의 헤더: A = 결과(0 헤딩슛 · 1 헤딩 패스 · 2 클리어), PlayerId = 승자, OtherId = 경합 최근접 상대(없으면 −1),
        /// B = 공중볼 상세 번호 + 1 (<c>ActionDetailLog</c>, 0 = 없음), F0 = 승리 확률, F1 = 헤딩슛 xG(슛이 아니면 0).
        /// </summary>
        Header = 215,

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
        /// <summary>
        /// GK 배급 (074 FR-10~14). 배급 킥(Pass 이벤트) 바로 뒤에 낸다. A = 방식(<c>Goalkeeper.DistributionKind</c>), OtherId = 받는 선수(없으면 −1),
        /// B = 손 보유 서브스텝 수(100 Hz, 손에 든 공이 아니면 −1), F0 = 보유 초(B/100, 아니면 0), F1 = 선택 후보의 성공 확률(008 xPass 또는 007 낙하점 컨트롤).
        /// </summary>
        Distribution = 308,
        /// <summary>다이빙 뒤 일어나기 끝 (011 FR-17·92).</summary>
        KeeperRecovered = 309,
        /// <summary>
        /// PK GK 전략 (074 FR-01·03). Substep = 다이빙 시작(조기 움직임) 서브스텝, A = 방향(−1·0·+1, 골문 y 증가 쪽 +1, 0 = 서서 반응),
        /// B = 킥 순간 골라인 이탈(0/1, 012 재차기 입력), F0 = 조기 움직임 시각 t_e(s, 킥 기준 실제값), F1 = 기울기 l.
        /// </summary>
        PenaltyKeeper = 310,

        // 012 (400~): 심판 · 재시작 — 데이터 규약은 specs/012 plan "SimEvent 데이터 규약" (FR-95)
        /// <summary>
        /// 휘슬 분 반칙 (012 FR-21·31). PlayerId = 반칙한 선수, OtherId = 당한 선수, A = 내린 판정 × 10 + 진짜 판정(<see cref="EventCodes"/>.Decision*),
        /// B = 판정 플래그(<see cref="EventCodes"/>.Ref*), F0 = 진짜 파울 점수, F1 = 시야 품질. 위치 = 반칙 지점.
        /// </summary>
        Foul = 400,
        /// <summary>카드. A = 색(<see cref="EventCodes.CardYellow"/>·<see cref="EventCodes.CardRed"/>), B = 사유(<see cref="EventCodes"/>.Reason*), PlayerId = 받은 선수(감독 퇴장이면 −1, Team만).</summary>
        Card = 401,
        /// <summary>오프사이드 (012 FR-01~10). PlayerId = 관여 선수, A = 관여 유형(1 플레이 · 2 상대 · 3 이득), B = 플래그(<see cref="EventCodes"/>.Off*), F0 = 실제 d, F1 = 지각 d. 위치 = 관여 지점.</summary>
        Offside = 402,
        ThrowIn = 403,
        GoalKick = 404,
        Corner = 405,
        /// <summary>프리킥 재시작. A = 1 직접 · 2 간접.</summary>
        FreeKick = 406,
        Penalty = 407,
        /// <summary>VAR 체크 (012 FR-71·73). A = 사안(<see cref="EventCodes"/>.Var*Incident), B = 체크 정지 틱, F0 = 오류 크기.</summary>
        VarCheck = 408,
        /// <summary>VAR 번복 (012 FR-74). A = 결과 유형(<see cref="EventCodes"/>.VarResult*, 0 = 골 취소 — 대체 스크립트 호환).</summary>
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
        /// <summary>012 FR-40: 어드밴티지 선언 (보류 반칙). PlayerId = 반칙한 선수, OtherId = 당한 선수, F0 = 이득 점수. 위치 = 반칙 지점.</summary>
        AdvantagePlayed = 417,
        /// <summary>012 FR-41: 보류 끝. A = 1 이득 유지(해제) · 2 이득 소멸(원래 지점 FK) · 3 골.</summary>
        AdvantageEnd = 418,
        /// <summary>012 FR-50·51: 핸드볼 반칙. PlayerId = 반칙한 선수, A = 내린 판정 × 10 + 진짜 판정, B = 판정 플래그, F0 = 핸드볼 점수, F1 = 시야 품질.</summary>
        HandballOffence = 419,
        /// <summary>012 FR-66: PK 재차기. A = 사유(1 GK 조기 이탈 · 2 침범).</summary>
        PenaltyRetake = 420,
        /// <summary>012 FR-63: GK 손 보유 초과. PlayerId = GK, F0 = 보유 시간(s).</summary>
        KeeperHoldViolation = 421,
        /// <summary>012: 이미 낸 재시작 취소 (VAR 번복 등). A = 취소한 재시작 이벤트 종류.</summary>
        RestartCancelled = 422,
        /// <summary>012 경계 조건: 접촉 입력 값이 범위 밖이라 잘랐다. A = 필드 번호.</summary>
        ContactInputClamped = 423,

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

        // 판정 단계 (012, Foul·HandballOffence의 A = 내린 × 10 + 진짜)
        public const int DecisionNone = 0;
        public const int DecisionFoul = 1;
        public const int DecisionYellow = 2;
        public const int DecisionRed = 3;

        // 카드 사유 (012 FR-22·26, Card의 B)
        public const int ReasonReckless = 1;
        public const int ReasonSeriousFoulPlay = 2;
        public const int ReasonSecondYellow = 3;
        public const int ReasonDogso = 4;
        public const int ReasonSpa = 5;
        public const int ReasonRepeatedFouls = 6;
        public const int ReasonSimulation = 7;
        public const int ReasonDissent = 8;
        public const int ReasonDelay = 9;
        public const int ReasonShirtOff = 10;
        public const int ReasonViolentConduct = 11;
        public const int ReasonHandball = 12;
        public const int ReasonKeeperEncroach = 13;
        public const int ReasonBench = 14;
        public const int ReasonEnterWithoutPermission = 15;

        // 판정 플래그 (012, Foul·HandballOffence의 B)
        public const int RefError = 1;
        public const int RefAdvantage = 2;
        public const int RefInBox = 4;
        public const int RefDogso = 8;
        public const int RefSpa = 16;
        public const int RefSliding = 32;
        public const int RefVarReviewable = 64;
        public const int RefDive = 128;
        public const int RefHomeVictim = 256;

        // 오프사이드 플래그 (012, Offside의 B)
        public const int OffTight = 1;
        public const int OffLateFlag = 2;
        public const int OffAssistantError = 4;
        public const int OffVarCorrected = 8;

        // VAR 사안 (012 FR-71, VarCheck의 A)
        public const int VarGoalIncident = 1;
        public const int VarPenaltyIncident = 2;
        public const int VarRedIncident = 3;
        public const int VarIdentityIncident = 4;

        // VAR 결과 (012 FR-74, VarOverturn의 A; VarCheck 결과 유지는 VarOverturn 없음)
        public const int VarResultGoalDisallowed = 0;
        public const int VarResultPenaltyOverturned = 1;
        public const int VarResultRedUpgrade = 2;
        public const int VarResultRetroRed = 3;
        public const int VarResultSimulationCaution = 4;
        public const int VarResultPenaltyAwarded = 5;
        public const int VarResultRedDowngrade = 6;

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
