using RoadToPro.Core.Math;
using RoadToPro.Core.Pitch;
using UnityEngine;

namespace RoadToPro.Presentation.Stadium
{
    /// <summary>
    /// 시뮬 좌표 ↔ Unity 월드 (specs/044 FR-01, 2026-11-03 확정). 시뮬 미터 (x 길이 0~L, y 폭 0~W, z 높이)를
    /// Unity (X = x − L/2, Y = z, Z = y − W/2)로 — 센터마크가 원점. 041·043도 이 변환을 쓴다.
    /// </summary>
    public static class PitchCoordinates
    {
        public static Vector3 ToWorld(Vec3 simMeters, PitchSpec spec) =>
            new Vector3(simMeters.X - spec.LengthM * 0.5f, simMeters.Z, simMeters.Y - spec.WidthM * 0.5f);

        public static Vec3 ToSim(Vector3 world, PitchSpec spec) =>
            new Vec3(world.x + spec.LengthM * 0.5f, world.z + spec.WidthM * 0.5f, world.y);

        public static Vector3 NormalizedToWorld(Vec2 normalized, float heightM, PitchSpec spec) =>
            ToWorld(new Vec3(PitchCoords.ToMeters(normalized, spec), heightM), spec);
    }
}
