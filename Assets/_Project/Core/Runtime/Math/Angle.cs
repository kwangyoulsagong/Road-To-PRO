namespace RoadToPro.Core.Math
{
    /// <summary>각도(라디안) 도구 (specs/001 FR-12).</summary>
    public static class Angle
    {
        /// <summary>(−π, π]로 정규화. −π는 π가 된다.</summary>
        public static float Normalize(float a)
        {
            if (a > -DMath.Pi && a <= DMath.Pi) return a;
            var r = a - DMath.TwoPi * DMath.Floor((a + DMath.Pi) / DMath.TwoPi);
            // |a|가 아주 크면(≈2e8 이상) 한 번의 보정으로 범위에 못 들어온다 → 반복 (결정론 리뷰 #6)
            while (r <= -DMath.Pi) r += DMath.TwoPi;
            while (r > DMath.Pi) r -= DMath.TwoPi;
            return r;
        }

        /// <summary>from → to 최단 부호 각도 (−π, π]. 정확히 반대면 +π (양의 방향).</summary>
        public static float Delta(float from, float to) => Normalize(to - from);

        /// <summary>최단 경로 각도 보간. 결과는 (−π, π].</summary>
        public static float LerpShortest(float from, float to, float t) => Normalize(from + Delta(from, to) * t);
    }
}
