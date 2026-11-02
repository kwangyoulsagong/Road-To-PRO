using System;
using System.Diagnostics;
using RoadToPro.Core.Logging;

namespace RoadToPro.Core.Math
{
    /// <summary>NaN·무한대 처리 방식 (specs/001 FR-15).</summary>
    public enum GuardMode
    {
        /// <summary>디버그: 즉시 실패.</summary>
        Strict,
        /// <summary>릴리스: 안전값으로 바꾸고 경고 1건.</summary>
        Sanitize,
    }

    /// <summary>수치 오류 (NaN·무한대·정의역 밖).</summary>
    public sealed class NumericException : Exception
    {
        public NumericException(string message) : base(message) { }
    }

    /// <summary>
    /// 실수 검사. 시뮬 상태를 확정하는 지점(물리 적분 끝 등)에서 <see cref="Check(float,string,GuardMode,ILogger,float)"/>를 부른다.
    /// <see cref="DefaultMode"/>는 컴파일 상수라 전역 상태가 아니다.
    /// </summary>
    public static class FloatGuard
    {
#if DEBUG
        public const GuardMode DefaultMode = GuardMode.Strict;
#else
        public const GuardMode DefaultMode = GuardMode.Sanitize;
#endif

        public static bool IsFinite(float v) => !float.IsNaN(v) && !float.IsInfinity(v);

        public static float Check(float v, string what, GuardMode mode, ILogger log, float safeValue = 0f)
        {
            if (IsFinite(v)) return v;
            if (mode == GuardMode.Strict)
                throw new NumericException(FormattableString.Invariant($"{what}: 유한하지 않은 값 {v}"));
            if (log != null && log.IsEnabled(LogLevel.Warning))
                log.Log(LogLevel.Warning, FormattableString.Invariant($"[FloatGuard] {what}: 유한하지 않은 값 {v} → {safeValue}"));
            return safeValue;
        }

        /// <summary>두 축 중 하나라도 문제면 경고 1건으로 처리.</summary>
        public static Vec2 Check(Vec2 v, string what, GuardMode mode, ILogger log)
        {
            if (IsFinite(v.X) && IsFinite(v.Y)) return v;
            if (mode == GuardMode.Strict)
                throw new NumericException(FormattableString.Invariant($"{what}: 유한하지 않은 벡터 ({v.X}, {v.Y})"));
            if (log != null && log.IsEnabled(LogLevel.Warning))
                log.Log(LogLevel.Warning, FormattableString.Invariant($"[FloatGuard] {what}: 유한하지 않은 벡터 ({v.X}, {v.Y}) → 해당 축 0"));
            return new Vec2(IsFinite(v.X) ? v.X : 0f, IsFinite(v.Y) ? v.Y : 0f);
        }

        public static Vec3 Check(Vec3 v, string what, GuardMode mode, ILogger log)
        {
            if (IsFinite(v.X) && IsFinite(v.Y) && IsFinite(v.Z)) return v;
            if (mode == GuardMode.Strict)
                throw new NumericException(FormattableString.Invariant($"{what}: 유한하지 않은 벡터 ({v.X}, {v.Y}, {v.Z})"));
            if (log != null && log.IsEnabled(LogLevel.Warning))
                log.Log(LogLevel.Warning, FormattableString.Invariant($"[FloatGuard] {what}: 유한하지 않은 벡터 ({v.X}, {v.Y}, {v.Z}) → 해당 축 0"));
            return new Vec3(IsFinite(v.X) ? v.X : 0f, IsFinite(v.Y) ? v.Y : 0f, IsFinite(v.Z) ? v.Z : 0f);
        }

        /// <summary>디버그 빌드에서만: 정의역 위반이면 실패.</summary>
        [Conditional("DEBUG")]
        public static void AssertDomain(bool ok, string fn, float x)
        {
            if (!ok) throw new NumericException(FormattableString.Invariant($"{fn}: 정의역 밖 입력 {x}"));
        }

        /// <summary>디버그 빌드에서만: 결과가 유한하지 않으면 실패.</summary>
        [Conditional("DEBUG")]
        public static void AssertFinite(float v, string fn)
        {
            if (!IsFinite(v)) throw new NumericException(FormattableString.Invariant($"{fn}: 유한하지 않은 결과 {v}"));
        }
    }
}
