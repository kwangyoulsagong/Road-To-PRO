using System;
using RoadToPro.Core.Math;
using RoadToPro.Core.Pitch;

namespace RoadToPro.Presentation.Highlight.Logic
{
    /// <summary>골대와 골망의 월드 치수 (골대 중심 = 골라인 X = ±L/2).</summary>
    public readonly struct GoalBox
    {
        public readonly float HalfLength, HalfMouth, Height, NetDepth, BallRadius;

        public GoalBox(PitchSpec pitch, float goalWidth, float goalHeight, float netDepth, float ballRadius)
        {
            HalfLength = pitch.LengthM * 0.5f;
            HalfMouth = goalWidth * 0.5f;
            Height = goalHeight;
            NetDepth = netDepth;
            BallRadius = ballRadius;
        }
    }

    /// <summary>
    /// 샘플 사이 보간으로 생기는 공 표시 오류를 막는다 (specs/041 FR-04). 샘플 위치(접촉 순간 포함)는 시뮬 값 그대로 둔다.
    /// </summary>
    public static class BallVisualGuard
    {
        /// <param name="value">표시하려는 위치.</param>
        /// <param name="a">앞 샘플.</param>
        /// <param name="b">뒤 샘플.</param>
        /// <param name="alpha">앞 샘플에서 뒤 샘플까지의 비율.</param>
        public static Vec3 Apply(Vec3 value, Vec3 a, Vec3 b, float alpha, in GoalBox box)
        {
            if (alpha <= 0f || alpha >= 1f) return value;
            if (CrossesGoalLineOutsideMouth(a, b, box)) return a;
            if (InNetZone(a, box) && InNetZone(b, box)) value = ClampInsideNet(value, box);

            float floor = Math.Min(box.BallRadius, Math.Min(a.Y, b.Y));
            return value.Y < floor ? new Vec3(value.X, floor, value.Z) : value;
        }

        static bool CrossesGoalLineOutsideMouth(Vec3 a, Vec3 b, in GoalBox box)
        {
            float sa = Math.Abs(a.X) - box.HalfLength, sb = Math.Abs(b.X) - box.HalfLength;
            if (sa < 0f == sb < 0f || a.X * b.X <= 0f) return false;
            float t = sa / (sa - sb);
            float z = a.Z + (b.Z - a.Z) * t, y = a.Y + (b.Y - a.Y) * t;
            return Math.Abs(z) > box.HalfMouth || y > box.Height;
        }

        static bool InNetZone(Vec3 p, in GoalBox box) =>
            Math.Abs(p.X) >= box.HalfLength && Math.Abs(p.Z) <= box.HalfMouth && p.Y <= box.Height;

        static Vec3 ClampInsideNet(Vec3 v, in GoalBox box)
        {
            float maxX = box.HalfLength + box.NetDepth - box.BallRadius;
            float maxZ = box.HalfMouth - box.BallRadius;
            float maxY = box.Height - box.BallRadius;
            float x = Math.Abs(v.X) > maxX ? Math.Sign(v.X) * maxX : v.X;
            float z = Math.Abs(v.Z) > maxZ ? Math.Sign(v.Z) * maxZ : v.Z;
            return new Vec3(x, Math.Min(v.Y, maxY), z);
        }
    }
}
