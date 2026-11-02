namespace RoadToPro.Core.Math
{
    /// <summary>보간·클램프 (specs/001 FR-12).</summary>
    public static class Interp
    {
        public static float Lerp(float a, float b, float t) => a + (b - a) * t;

        /// <summary>a == b면 0.</summary>
        public static float InverseLerp(float a, float b, float v) => a == b ? 0f : (v - a) / (b - a);

        public static float Clamp(float v, float min, float max) => v < min ? min : (v > max ? max : v);
        public static int Clamp(int v, int min, int max) => v < min ? min : (v > max ? max : v);
        public static float Clamp01(float v) => Clamp(v, 0f, 1f);

        public static float Smoothstep(float edge0, float edge1, float x)
        {
            var t = Clamp01(InverseLerp(edge0, edge1, x));
            return t * t * (3f - 2f * t);
        }

        public static float Sigmoid(float x) => 1f / (1f + DMath.Exp(-x));
    }
}
