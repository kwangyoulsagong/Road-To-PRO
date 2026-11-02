using RoadToPro.Core.Math;

namespace RoadToPro.MatchEngine.Ball
{
    /// <summary>한 서브스텝 동안의 공 이동 선분 (FR-22). 008·011이 선분-원 접촉 판정에 쓴다.</summary>
    public readonly struct BallSegment
    {
        public readonly Vec3 Start;
        public readonly Vec3 End;

        public BallSegment(Vec3 start, Vec3 end)
        {
            Start = start;
            End = end;
        }
    }
}
