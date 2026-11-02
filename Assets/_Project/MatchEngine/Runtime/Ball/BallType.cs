namespace RoadToPro.MatchEngine.Ball
{
    /// <summary>공 종류 (FR-02). 값은 튜닝 <c>ball.defaultType</c>과 같은 번호.</summary>
    public enum BallType : byte
    {
        Panel32 = 0,
        Modern = 1,
    }

    /// <summary>
    /// 피치 표면 조건 (FR-05·06). 반발 e와 구름 보정의 연결은 specs/004 plan "표면 조건 → 파라미터 매핑".
    /// Wet = 젖은 짧은 잔디.
    /// </summary>
    public enum SurfaceCondition : byte
    {
        Dry = 0,
        Wet = 1,
        Artificial = 2,
        Flooded = 3,
        LongGrass = 4,
    }
}
