using System.Globalization;

namespace RoadToPro.Core.Logging
{
    /// <summary>
    /// 레벨별 로그 호출 (specs/001 FR-71). 제네릭 인자라 비활성 레벨에서는 박싱·문자열 생성이 없다.
    /// 포맷은 InvariantCulture.
    /// </summary>
    public static class LoggerExtensions
    {
        public static void Write(this ILogger log, LogLevel level, string message)
        {
            if (log.IsEnabled(level)) log.Log(level, message);
        }

        public static void Write<T1>(this ILogger log, LogLevel level, string format, T1 a)
        {
            if (log.IsEnabled(level)) log.Log(level, string.Format(CultureInfo.InvariantCulture, format, a));
        }

        public static void Write<T1, T2>(this ILogger log, LogLevel level, string format, T1 a, T2 b)
        {
            if (log.IsEnabled(level)) log.Log(level, string.Format(CultureInfo.InvariantCulture, format, a, b));
        }

        public static void Write<T1, T2, T3>(this ILogger log, LogLevel level, string format, T1 a, T2 b, T3 c)
        {
            if (log.IsEnabled(level)) log.Log(level, string.Format(CultureInfo.InvariantCulture, format, a, b, c));
        }

        public static void Trace(this ILogger log, string m) => log.Write(LogLevel.Trace, m);
        public static void Trace<T1>(this ILogger log, string f, T1 a) => log.Write(LogLevel.Trace, f, a);
        public static void Trace<T1, T2>(this ILogger log, string f, T1 a, T2 b) => log.Write(LogLevel.Trace, f, a, b);
        public static void Trace<T1, T2, T3>(this ILogger log, string f, T1 a, T2 b, T3 c) => log.Write(LogLevel.Trace, f, a, b, c);

        public static void Debug(this ILogger log, string m) => log.Write(LogLevel.Debug, m);
        public static void Debug<T1>(this ILogger log, string f, T1 a) => log.Write(LogLevel.Debug, f, a);
        public static void Debug<T1, T2>(this ILogger log, string f, T1 a, T2 b) => log.Write(LogLevel.Debug, f, a, b);
        public static void Debug<T1, T2, T3>(this ILogger log, string f, T1 a, T2 b, T3 c) => log.Write(LogLevel.Debug, f, a, b, c);

        public static void Info(this ILogger log, string m) => log.Write(LogLevel.Info, m);
        public static void Info<T1>(this ILogger log, string f, T1 a) => log.Write(LogLevel.Info, f, a);
        public static void Info<T1, T2>(this ILogger log, string f, T1 a, T2 b) => log.Write(LogLevel.Info, f, a, b);
        public static void Info<T1, T2, T3>(this ILogger log, string f, T1 a, T2 b, T3 c) => log.Write(LogLevel.Info, f, a, b, c);

        public static void Warning(this ILogger log, string m) => log.Write(LogLevel.Warning, m);
        public static void Warning<T1>(this ILogger log, string f, T1 a) => log.Write(LogLevel.Warning, f, a);
        public static void Warning<T1, T2>(this ILogger log, string f, T1 a, T2 b) => log.Write(LogLevel.Warning, f, a, b);
        public static void Warning<T1, T2, T3>(this ILogger log, string f, T1 a, T2 b, T3 c) => log.Write(LogLevel.Warning, f, a, b, c);

        public static void Error(this ILogger log, string m) => log.Write(LogLevel.Error, m);
        public static void Error<T1>(this ILogger log, string f, T1 a) => log.Write(LogLevel.Error, f, a);
        public static void Error<T1, T2>(this ILogger log, string f, T1 a, T2 b) => log.Write(LogLevel.Error, f, a, b);
        public static void Error<T1, T2, T3>(this ILogger log, string f, T1 a, T2 b, T3 c) => log.Write(LogLevel.Error, f, a, b, c);
    }
}
