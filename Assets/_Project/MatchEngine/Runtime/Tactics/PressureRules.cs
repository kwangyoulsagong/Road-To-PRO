using RoadToPro.Core.Math;

namespace RoadToPro.MatchEngine.Tactics
{
    /// <summary>"압박 중" 통계 판정 (FR-62, FIFA 정의 차용). 미터·m/s.</summary>
    public static class PressureRules
    {
        public static bool IsPressing(ShapeParams p, Vec2 position, Vec2 velocity, Vec2 ball, bool fasterThanTeammates)
        {
            var toBall = ball - position;
            var d = toBall.Length();
            if (d <= p.PressureNear) return true;
            var speed = velocity.Length();
            if (speed > p.PressureFarSpeed && d <= p.PressureFar) return true;
            return fasterThanTeammates && speed > p.PressureMidSpeed && d <= p.PressureMid && Vec2.Dot(toBall, velocity) > 0f;
        }
    }
}
