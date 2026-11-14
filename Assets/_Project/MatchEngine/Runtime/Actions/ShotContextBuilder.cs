using RoadToPro.Core.Math;
using RoadToPro.MatchEngine.Ball;

namespace RoadToPro.MatchEngine.Actions
{
    /// <summary>
    /// <see cref="ShotContext"/> 채우기 (specs/071 FR-10 입력, 플랜 [대체]): 슬롯 위치 배열(액션 월드·결정 월드 공용)에서
    /// 콘(슈터–두 골포스트 삼각형) 안 상대 필드 선수 수, GK 이탈 = 슈터–골문 중앙 선분에서 GK 거리 / gkOutDist(0~1, GK 없음 1).
    /// 어시스트·바운드·헤더·압박은 호출자가 넘긴다. RNG 없음, 할당 없음.
    /// </summary>
    public static class ShotContextBuilder
    {
        public const int PerTeam = ActionWorld.PerTeam;

        public static ShotContext Build(ActionParams p, Vec2[] pos, bool[] active, Vec2 goal, int dir, int oppTeam, Vec2 from,
            float pressure, float header = 0f, float assist = 0f, float bouncing = 0f, bool penalty = false)
        {
            var c = new ShotContext { Header = header, Pressure = pressure, Assist = assist, Bouncing = bouncing, Penalty = penalty };
            ShotModel.Geometry(from, goal, dir, ref c);
            if (c.Behind || penalty) return c;
            var half = BallParams.GoalWidth * 0.5f;
            var a = new Vec2(goal.X, goal.Y - half);
            var b = new Vec2(goal.X, goal.Y + half);
            var gk = oppTeam * PerTeam;
            var n = 0;
            for (int s = gk + 1; s < gk + PerTeam; s++)
                if (active[s] && InTriangle(pos[s], from, a, b)) n++;
            c.Blockers = n;
            if (!active[gk]) c.GkOut = 1f;
            else
            {
                var d = SegmentDistance(pos[gk], from, goal);
                var v = p.XgGkOutDist > 1e-3f ? d / p.XgGkOutDist : 1f;
                c.GkOut = v > 1f ? 1f : v;
            }
            return c;
        }

        public static bool InTriangle(Vec2 q, Vec2 a, Vec2 b, Vec2 c)
        {
            var d1 = Cross(q - a, b - a);
            var d2 = Cross(q - b, c - b);
            var d3 = Cross(q - c, a - c);
            var neg = d1 < 0f || d2 < 0f || d3 < 0f;
            var pos = d1 > 0f || d2 > 0f || d3 > 0f;
            return !(neg && pos);
        }

        public static float SegmentDistance(Vec2 q, Vec2 a, Vec2 b)
        {
            var ab = b - a;
            var l2 = ab.LengthSq();
            var t = l2 > 1e-8f ? Vec2.Dot(q - a, ab) / l2 : 0f;
            if (t < 0f) t = 0f; else if (t > 1f) t = 1f;
            return (a + ab * t - q).Length();
        }

        static float Cross(Vec2 u, Vec2 v) => u.X * v.Y - u.Y * v.X;
    }
}
