using System.Runtime.CompilerServices;
namespace RoadToPro.Core.Math
{
    /// <summary>
    /// 결정론 수학 래퍼 (specs/001 FR-11·14). 코어의 초월함수는 반드시 여기를 거친다.
    /// 크로스플랫폼 결정론(고정소수점 여부)이 정해지면 이 파일만 바꾼다. 모든 입출력은 float.
    /// 디버그 빌드에서는 정의역 밖 입력(음수 Sqrt·Log 등)이 NumericException으로 즉시 실패한다 (FR-15).
    /// </summary>
    public static class DMath
    {
        public const float Pi = 3.14159265358979f;
        public const float TwoPi = 6.28318530717959f;
        public const float HalfPi = 1.57079632679490f;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float Sqrt(float x)
        {
            FloatGuard.AssertDomain(x >= 0f, "Sqrt", x);
            return System.MathF.Sqrt(x);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float Sin(float x) => System.MathF.Sin(x);
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float Cos(float x) => System.MathF.Cos(x);

        /// <summary>atan2(0,0) = 0.</summary>
        public static float Atan2(float y, float x)
        {
            if (y == 0f && x == 0f) return 0f;
            return System.MathF.Atan2(y, x);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float Exp(float x) => System.MathF.Exp(x);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float Log(float x)
        {
            FloatGuard.AssertDomain(x > 0f, "Log", x);
            return System.MathF.Log(x);
        }

        public static float Pow(float x, float y)
        {
            var r = System.MathF.Pow(x, y);
            FloatGuard.AssertFinite(r, "Pow");
            return r;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float Abs(float x) => x < 0f ? -x : (x == 0f ? 0f : x);
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float Min(float a, float b) => a < b ? a : b;
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float Max(float a, float b) => a > b ? a : b;
        public static int Min(int a, int b) => a < b ? a : b;
        public static int Max(int a, int b) => a > b ? a : b;
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float Floor(float x) => System.MathF.Floor(x);
        public static float Sign(float x) => x > 0f ? 1f : (x < 0f ? -1f : 0f);
    }
}
