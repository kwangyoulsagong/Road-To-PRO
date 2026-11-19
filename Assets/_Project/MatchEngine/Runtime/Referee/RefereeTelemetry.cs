namespace RoadToPro.MatchEngine.Referee
{
    /// <summary>
    /// 경기 판정 누계 (specs/012 FR-95·96, 측정용 — 해시·체크포인트 밖). 015·017·측정 리포트가 읽는다.
    /// "주요 판정" = 휘슬 분 반칙 + 놓친 진짜 반칙 + 오프사이드 관여 + 다이빙 + 핸드볼 (VAR 전 기준).
    /// </summary>
    public sealed class RefereeTelemetry
    {
        public int Contacts, TrueFouls, Whistles, Advantages, AdvantageReturned;
        public int Yellows, Reds, SecondYellows, PenaltiesAwarded, Offsides, OffsideInvolvements, AssistantErrors;
        public int MajorDecisions, MajorCorrect;
        public int VarChecks, VarOverturns, ErrorsAfterVar;
        public int Dives, DivesFooled, DiveCards, Handballs, KeeperHolds;
        /// <summary>엄격·관대 비교용: 스탠딩·슬라이딩 태클 접촉 수와 휘슬 수 (AC-23 실경기 측정).</summary>
        public int StandingTackles, StandingTackleFouls, SlidingTackles, SlidingTackleFouls;

        public void Reset()
        {
            Contacts = TrueFouls = Whistles = Advantages = AdvantageReturned = 0;
            Yellows = Reds = SecondYellows = PenaltiesAwarded = Offsides = OffsideInvolvements = AssistantErrors = 0;
            MajorDecisions = MajorCorrect = 0;
            VarChecks = VarOverturns = ErrorsAfterVar = 0;
            Dives = DivesFooled = DiveCards = Handballs = KeeperHolds = 0;
            StandingTackles = StandingTackleFouls = SlidingTackles = SlidingTackleFouls = 0;
        }

        public void Major(bool correct)
        {
            MajorDecisions++;
            if (correct) MajorCorrect++;
        }
    }
}
