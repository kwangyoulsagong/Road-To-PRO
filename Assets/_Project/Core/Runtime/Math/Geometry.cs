namespace RoadToPro.Core.Math
{
    /// <summary>충돌·접촉 판정 (specs/001 FR-13).</summary>
    public static class Geometry
    {
        /// <summary>
        /// 선분 a→b(한 서브스텝 동안의 공 이동)가 원(중심 c, 반지름 r)에 처음 닿는 비율 t∈[0,1].
        /// 시작점이 이미 원 안(경계 포함)이면 t=0. 접선으로 스치면 접점의 t. 닿지 않으면 false.
        /// </summary>
        public static bool SweptSegmentCircle(Vec2 a, Vec2 b, Vec2 c, float r, out float t)
        {
            var f = a - c;
            var c0 = f.LengthSq() - r * r;
            if (c0 <= 0f)
            {
                t = 0f;
                return true;
            }

            var d = b - a;
            var qa = d.LengthSq();
            if (qa == 0f)
            {
                t = 0f;
                return false;
            }

            var qb = 2f * Vec2.Dot(f, d);
            if (qb >= 0f)
            {
                // 원에서 멀어지거나 평행: 시작점이 밖이므로 닿지 않는다
                t = 0f;
                return false;
            }

            var disc = qb * qb - 4f * qa * c0;
            if (disc < 0f)
            {
                t = 0f;
                return false;
            }

            var t1 = (-qb - DMath.Sqrt(disc)) / (2f * qa);
            if (t1 >= 0f && t1 <= 1f)
            {
                t = t1;
                return true;
            }

            t = 0f;
            return false;
        }
    }
}
