namespace RoadToPro.MatchEngine.Match.Pipeline
{
    /// <summary>틱 파이프라인 단계 (specs/006 FR-01, 아키텍처 3). 번호가 실행 순서다.</summary>
    public enum StageId : byte
    {
        TeamTactics = 0,
        Phase = 1,
        Shape = 2,
        RoleAdjust = 3,
        Tendency = 4,
        ScenarioOrUtility = 5,
        SteeringTargets = 6,
        Physics = 7,
        Referee = 8,
        Publish = 9,
    }

    public static class Stages
    {
        public const int Count = 10;
    }
}
