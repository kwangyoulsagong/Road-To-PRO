namespace RoadToPro.MatchEngine.Tactics
{
    /// <summary>팀 국면 (specs/009 FR-20, 리서치 03 A2). DefHigh~DefLow는 정렬된 수비 블록이다.</summary>
    public enum TeamPhase : byte
    {
        BuildUp = 0,
        Progression = 1,
        FinalThird = 2,
        AttTransition = 3,
        DefTransition = 4,
        DefHigh = 5,
        DefMid = 6,
        DefLow = 7,
        SetPiece = 8,
    }

    /// <summary>포메이션 앵커 프로필 (FR-10). 전환·세트피스 국면은 이 여섯 중 하나를 쓴다.</summary>
    public enum ShapeProfile : byte
    {
        BuildUp = 0,
        Progression = 1,
        FinalThird = 2,
        DefHigh = 3,
        DefMid = 4,
        DefLow = 5,
    }

    public static class ShapeProfiles
    {
        public const int Count = 6;
    }
}
