namespace RoadToPro.Core.Logging
{
    /// <summary>로그 레벨 (specs/001 FR-70). 숫자가 클수록 심각.</summary>
    public enum LogLevel
    {
        Trace = 0,
        Debug = 1,
        Info = 2,
        Warning = 3,
        Error = 4,
        None = 5,
    }
}
