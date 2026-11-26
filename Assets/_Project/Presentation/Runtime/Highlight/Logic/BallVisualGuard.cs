using System;
using RoadToPro.Core.Math;
using RoadToPro.Core.Pitch;

namespace RoadToPro.Presentation.Highlight.Logic
{
    /// <summary>골대와 골망의 월드 치수 (골대 중심 = 골라인 X = ±L/2).</summary>
    public readonly struct GoalBox
    {
        public readonly float HalfLength, HalfMouth, Height, NetDepth, BallRadius;
        /// <summary>골 안 공이 그물을 밀고 들어가는 최대 깊이 (specs/059 FR-41 ⑥, `ball.net.pocket`). 0이면 정지 그물 면에서 막힘.</summary>
        public readonly float Pocket;

        public GoalBox(PitchSpec pitch, float goalWidth, float goalHeight, float netDepth, float ballRadius, float pocket = 0f)
        {
            Pocket = pocket;
            HalfLength = pitch.LengthM * 0.5f;
            HalfMouth = goalWidth * 0.5f;
            Height = goalHeight;
            NetDepth = netDepth;
            BallRadius = ballRadius;
        }

        /// <summary>
        /// 튜닝을 읽지 않는 곳의 기본 그물 상자: 골문 7.32 × 2.44 m(규격), 그물 깊이 `ball.net.depth` 2.0 m·공 반지름 `ball.radius` 0.109 m·
        /// 그물 주머니 `ball.net.pocket` 1.0 m 기본값.
        /// 튜닝을 읽는 쪽(검증 화면 등)은 BallParams로 만든 상자를 넘긴다.
        /// </summary>
        public static GoalBox Default(PitchSpec pitch) =>
            new GoalBox(pitch, RoadToPro.MatchEngine.Ball.BallParams.GoalWidth, RoadToPro.MatchEngine.Ball.BallParams.GoalHeight, 2f, 0.109f, 1.0f);

        /// <summary>치수를 바로 받는다 (카메라 배치 데이터 등 피치 규격이 없는 곳).</summary>
        public GoalBox(float halfLength, float halfMouth, float height, float netDepth, float ballRadius, float pocket = 0f)
        {
            Pocket = pocket;
            HalfLength = halfLength;
            HalfMouth = halfMouth;
            Height = height;
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
            // 059 FR-41 ⑥: 골 안 공은 그물을 주머니 깊이까지 밀고 들어갈 수 있다
            float maxX = box.HalfLength + box.NetDepth - box.BallRadius + box.Pocket;
            float maxZ = box.HalfMouth - box.BallRadius + box.Pocket;
            float maxY = box.Height - box.BallRadius + box.Pocket;
            float x = Math.Abs(v.X) > maxX ? Math.Sign(v.X) * maxX : v.X;
            float z = Math.Abs(v.Z) > maxZ ? Math.Sign(v.Z) * maxZ : v.Z;
            return new Vec3(x, Math.Min(v.Y, maxY), z);
        }
    }
}
