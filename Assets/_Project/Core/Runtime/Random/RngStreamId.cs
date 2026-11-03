namespace RoadToPro.Core.Random
{
    /// <summary>
    /// 서브시스템별 RNG 스트림 id (specs/001 FR-30·35). **번호는 세이브·리플레이 재현에 들어가므로 절대 바꾸지 않는다.**
    /// 새 스트림은 빈 번호를 새로 쓴다. 1~99 경기, 100~ 커리어·월드.
    /// </summary>
    public enum RngStreamId : ulong
    {
        PhysicsNoise = 1,
        PlayerDecision = 2,
        Referee = 3,
        Injury = 4,
        ScenarioSelect = 5,
        Highlight = 6,
        MatchForm = 7,
        /// <summary>경기 진행: 동전 던지기, 대체 단계 무작위 (specs/006).</summary>
        MatchFlow = 8,

        CareerWeek = 101,
        CareerEvents = 102,
        WorldGen = 103,
    }
}
