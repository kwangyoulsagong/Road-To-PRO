namespace RoadToPro.Core.Logging
{
    /// <summary>아무것도 기록하지 않는 로거. 모든 레벨 비활성 → 호출 비용 0.</summary>
    public sealed class NullLogger : ILogger
    {
        public static readonly NullLogger Instance = new NullLogger();

        NullLogger() { }

        public bool IsEnabled(LogLevel level) => false;
        public void Log(LogLevel level, string message) { }
    }
}
