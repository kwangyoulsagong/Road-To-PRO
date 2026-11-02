namespace RoadToPro.Core.Logging
{
    /// <summary>
    /// 주입형 로거 (specs/001 FR-70). 코어는 엔진 로그를 직접 부르지 않는다.
    /// 호출자는 <see cref="LoggerExtensions"/>의 제네릭 오버로드를 써서 비활성 레벨에서 문자열을 만들지 않는다 (FR-71).
    /// </summary>
    public interface ILogger
    {
        bool IsEnabled(LogLevel level);
        void Log(LogLevel level, string message);
    }
}
