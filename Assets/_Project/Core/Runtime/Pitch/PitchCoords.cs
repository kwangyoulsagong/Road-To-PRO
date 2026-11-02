using RoadToPro.Core.Math;

namespace RoadToPro.Core.Pitch
{
    public enum TeamSide
    {
        Home = 0,
        Away = 1,
    }

    /// <summary>
    /// 피치 좌표계 (specs/001 FR-20~22). 기준 좌표는 정규화 x∈[0,1](홈이 전반에 공격하는 방향), y∈[0,1](폭), 높이 z(미터).
    /// 팀 관점 좌표는 "자기 골라인 0 → 상대 골라인 1". 관점 변환은 <see cref="ToPerspective"/> 한 함수로만 한다.
    /// </summary>
    public static class PitchCoords
    {
        public static Vec2 ToMeters(Vec2 normalized, PitchSpec spec) =>
            new Vec2(normalized.X * spec.LengthM, normalized.Y * spec.WidthM);

        public static Vec2 ToNormalized(Vec2 meters, PitchSpec spec) =>
            new Vec2(meters.X / spec.LengthM, meters.Y / spec.WidthM);

        public static Vec3 ToMeters(Vec3 normalizedXyMetersZ, PitchSpec spec) =>
            new Vec3(ToMeters(normalizedXyMetersZ.XY, spec), normalizedXyMetersZ.Z);

        /// <summary>
        /// 기준 좌표 → 팀 관점 좌표. 기간(0 전반, 1 후반, 2 연장 전반, 3 연장 후반)마다 진영이 바뀐다.
        /// 반전은 (1−x, 1−y). [0,1] float 반전은 왕복 시 최대 1 ulp(1.0 기준) 오차가 있다 (AC-05, 사용자 승인).
        /// </summary>
        public static Vec2 ToPerspective(Vec2 world, TeamSide side, int periodIndex)
        {
            var flip = (side == TeamSide.Away) ^ ((periodIndex & 1) == 1);
            return flip ? new Vec2(1f - world.X, 1f - world.Y) : world;
        }

        /// <summary>팀 관점 → 기준 좌표. 반전은 자기 역함수라 같은 변환이다.</summary>
        public static Vec2 FromPerspective(Vec2 perspective, TeamSide side, int periodIndex) =>
            ToPerspective(perspective, side, periodIndex);
    }
}
