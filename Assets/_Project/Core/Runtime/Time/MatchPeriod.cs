namespace RoadToPro.Core.Time
{
    /// <summary>경기 기간. 값은 진영 교체 판정(<c>PitchCoords.ToPerspective</c>의 periodIndex)과 같다.</summary>
    public enum MatchPeriod
    {
        FirstHalf = 0,
        SecondHalf = 1,
        ExtraFirst = 2,
        ExtraSecond = 3,
    }
}
