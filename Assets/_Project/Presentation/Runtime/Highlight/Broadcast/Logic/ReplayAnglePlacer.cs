using System;
using RoadToPro.Core.Math;

namespace RoadToPro.Presentation.Highlight.Broadcast.Logic
{
    /// <summary>배치 결과.</summary>
    public readonly struct AnglePlacement
    {
        public readonly Vec3 Position;
        public readonly int Candidate;
        public readonly bool Fallback;

        public AnglePlacement(Vec3 position, int candidate, bool fallback)
        {
            Position = position;
            Candidate = candidate;
            Fallback = fallback;
        }
    }

    /// <summary>
    /// 리플레이 앵글 자동 배치 (specs/042 FR-21~24, 54). 후보는 사건 쪽 골대 부호로 계산해 좌우 대칭이다.
    /// 후보가 금지 영역 안이거나 시야가 막히면 다음 후보로, 전부 안 되면 피치 위 하이앵글로 대체한다.
    /// </summary>
    public static class ReplayAnglePlacer
    {
        const int Candidates = 3;
        /// <summary>메인 스탠드 쪽 러닝 트랙 안쪽 한계. 벤치·스탠드 앞이다.</summary>
        const float NearSideLimit = -36.5f, FarSideZ = 36.5f;

        public static AnglePlacement Place(ShotKind kind, Vec3 ev, CameraRigData rig)
        {
            float s = ev.X >= 0f ? 1f : -1f;
            for (int i = 0; i < Candidates; i++)
            {
                var p = Candidate(kind, ev, s, i, rig);
                if (CameraOcclusion.InsideNoGo(rig, p)) continue;
                if (CameraOcclusion.IsBlocked(rig, p, ev)) continue;
                return new AnglePlacement(p, i, false);
            }
            return new AnglePlacement(Fallback(ev, s), -1, true);
        }

        public static bool IsReverse(ShotKind kind) => kind == ShotKind.ReplayReverse;

        /// <summary>FR-25·54 대체 하이앵글: 피치 위 공중이라 구조물과 겹치지 않는다.</summary>
        public static Vec3 Fallback(Vec3 ev, float s) =>
            new Vec3(ev.X - s * 14f, 18f, CameraMath.Clamp(ev.Z - 22f, -30f, 10f));

        static Vec3 Candidate(ShotKind kind, Vec3 ev, float s, int i, CameraRigData rig)
        {
            switch (kind)
            {
                case ShotKind.ReplayHigh:
                {
                    float z = ev.Z * 0.4f + (i == 0 ? 0f : i == 1 ? 6f : -6f);
                    return new Vec3(s * (rig.HalfLength + 3f), i == 0 ? 11f : 13f, z);
                }
                case ShotKind.ReplayShooter:
                {
                    float x = ev.X - s * (i == 0 ? 9f : i == 1 ? 13f : 5f);
                    float z = MathF.Max(ev.Z - (i == 2 ? 16f : 13f), NearSideLimit);
                    return new Vec3(x, i == 0 ? 2.2f : 3.2f, z);
                }
                case ShotKind.ReplayReverse:
                {
                    float x = ev.X * 0.8f - s * (i == 0 ? 0f : i == 1 ? 8f : 16f);
                    return new Vec3(x, 6f, FarSideZ);
                }
                default:
                    return Fallback(ev, s);
            }
        }
    }
}
