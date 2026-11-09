namespace RoadToPro.MatchEngine.Tactics
{
    /// <summary>팀 형태 지표 (FR-95). 길이·폭·높이는 미터, 면적은 m².</summary>
    public struct TeamShapeMetrics
    {
        public float Length, Width, Area, LineHeightM, LineGapM;
        public int PressingPlayers;
    }

    /// <summary>결정 틱 지표 수신 (015·017 집계용). null이면 계산·호출 0.</summary>
    public interface ITeamShapeTelemetry
    {
        void OnTeamShape(int team, long tick, TeamPhase phase, in TeamShapeMetrics metrics);
    }

    /// <summary>디버그 진단 (FR-96, 040 레이어). 읽기만 한다. null이면 호출·할당 0.</summary>
    public interface IShapeDiagnostics
    {
        void OnTeam(int team, long tick, TeamShapeEngine engine);
    }
}
