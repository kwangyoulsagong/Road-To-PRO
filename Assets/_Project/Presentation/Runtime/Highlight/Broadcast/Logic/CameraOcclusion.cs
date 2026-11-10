using System;
using RoadToPro.Core.Math;

namespace RoadToPro.Presentation.Highlight.Broadcast.Logic
{
    /// <summary>카메라 → 사건 지점 시야 검사 (specs/042 FR-24). 물리 레이캐스트 없이 기하만 쓴다.</summary>
    public static class CameraOcclusion
    {
        /// <summary>from → to 선분이 구조물에 막히면 true. to를 품은 상자(그물 안의 공)는 막힘으로 치지 않는다.</summary>
        public static bool IsBlocked(CameraRigData rig, Vec3 from, Vec3 to)
        {
            for (int i = 0; i < rig.Posts.Count; i++)
                if (HitsPost(from, to, rig.Posts[i])) return true;
            for (int i = 0; i < rig.GoalBlockers.Count; i++)
                if (!rig.GoalBlockers[i].Contains(to) && HitsBox(from, to, rig.GoalBlockers[i])) return true;
            for (int i = 0; i < rig.NoGo.Count; i++)
                if (!rig.NoGo[i].Contains(to) && HitsBox(from, to, rig.NoGo[i])) return true;
            return false;
        }

        public static bool InsideNoGo(CameraRigData rig, Vec3 p)
        {
            for (int i = 0; i < rig.NoGo.Count; i++)
                if (rig.NoGo[i].Contains(p)) return true;
            return false;
        }

        /// <summary>선분 대 상자 슬랩 교차.</summary>
        public static bool HitsBox(Vec3 a, Vec3 b, in CameraBox box)
        {
            float t0 = 0f, t1 = 1f;
            return Slab(a.X, b.X - a.X, box.Min.X, box.Max.X, ref t0, ref t1)
                && Slab(a.Y, b.Y - a.Y, box.Min.Y, box.Max.Y, ref t0, ref t1)
                && Slab(a.Z, b.Z - a.Z, box.Min.Z, box.Max.Z, ref t0, ref t1);
        }

        static bool Slab(float origin, float dir, float min, float max, ref float t0, ref float t1)
        {
            if (MathF.Abs(dir) < 1e-7f) return origin >= min && origin <= max;
            float a = (min - origin) / dir, b = (max - origin) / dir;
            if (a > b) { float s = a; a = b; b = s; }
            if (a > t0) t0 = a;
            if (b < t1) t1 = b;
            return t0 <= t1;
        }

        /// <summary>선분과 수직 원기둥. XZ 평면 최근접 지점의 높이가 기둥 안이면 막힘.</summary>
        public static bool HitsPost(Vec3 a, Vec3 b, in CameraPost post)
        {
            float dx = b.X - a.X, dz = b.Z - a.Z;
            float len = dx * dx + dz * dz;
            float t = len < 1e-8f ? 0f : ((post.X - a.X) * dx + (post.Z - a.Z) * dz) / len;
            t = CameraMath.Clamp(t, 0f, 1f);
            float cx = a.X + dx * t - post.X, cz = a.Z + dz * t - post.Z;
            if (cx * cx + cz * cz > post.Radius * post.Radius) return false;
            float y = a.Y + (b.Y - a.Y) * t;
            return y >= 0f && y <= post.Height;
        }
    }
}
