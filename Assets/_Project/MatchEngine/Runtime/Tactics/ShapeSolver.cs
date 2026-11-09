using RoadToPro.Core.Math;

namespace RoadToPro.MatchEngine.Tactics
{
    /// <summary>블록 중심·목표 크기 계산 결과. 중심은 정규화, 길이·폭은 미터.</summary>
    public struct SolvedBlock
    {
        public float Cx, Cy, L, W, LMax, WMax;

        public static SolvedBlock Lerp(in SolvedBlock a, in SolvedBlock b, float t) => new SolvedBlock
        {
            Cx = Interp.Lerp(a.Cx, b.Cx, t), Cy = Interp.Lerp(a.Cy, b.Cy, t), L = Interp.Lerp(a.L, b.L, t),
            W = Interp.Lerp(a.W, b.W, t), LMax = Interp.Lerp(a.LMax, b.LMax, t), WMax = Interp.Lerp(a.WMax, b.WMax, t),
        };
    }

    /// <summary>슬롯 목표 공식 (FR-30·31·33, 리서치 03 A3). 팀 관점 정규화 좌표. 할당 없음.</summary>
    public static class ShapeSolver
    {
        public static void Resolve(ShapeParams p, ShapeProfile profile, Vec2 ball, in TeamInstructions ins, out SolvedBlock b)
        {
            int i = (int)profile;
            bool possessing = profile <= ShapeProfile.FinalThird;
            var ballX = Interp.Clamp01(ball.X);
            var ballY = Interp.Clamp01(ball.Y);

            var cx = Interp.Clamp(Interp.Lerp(p.CxMin[i], p.CxMax[i], ballX), p.Floor[i], p.Ceil[i]) + Bias(p, profile, ins.LineHeight);
            var lateral = possessing ? p.LateralMin : Interp.Lerp(p.LateralMin, p.LateralMax, Interp.Clamp01(ins.Compactness));

            // Compactness 0.5 이하는 표 상한 그대로, 1이면 표 하한까지 조인다 (A6)
            var squeeze = ins.Compactness > 0.5f ? (ins.Compactness - 0.5f) * 2f : 0f;
            var lMax = Interp.Lerp(p.LMax[i], p.LMin[i], squeeze);
            var wMax = Interp.Lerp(p.WMax[i], p.WMin[i], squeeze);
            var width = possessing ? ins.WidthIn : ins.WidthOut;
            var wScale = Interp.Lerp(p.WidthScaleMin, p.WidthScaleMax, Interp.Clamp01(width));

            b = new SolvedBlock
            {
                Cx = cx,
                Cy = Interp.Lerp(0.5f, ballY, lateral),
                L = Interp.Clamp(p.LTarget[i], p.LMin[i], lMax),
                W = Interp.Clamp(p.WTarget[i] * wScale, p.WMin[i], wMax),
                LMax = lMax,
                WMax = wMax,
            };
        }

        static float Bias(ShapeParams p, ShapeProfile profile, float lineHeight)
        {
            if (profile <= ShapeProfile.FinalThird) return (lineHeight - 0.5f) * 2f * p.BiasMax;
            float lo, hi;
            if (profile == ShapeProfile.DefLow) { lo = 0f; hi = p.BlockLowBound; }
            else if (profile == ShapeProfile.DefMid) { lo = p.BlockLowBound; hi = p.BlockHighBound; }
            else { lo = p.BlockHighBound; hi = 1f; }
            var half = (hi - lo) * 0.5f;
            return half > 0f ? Interp.Clamp((lineHeight - (lo + hi) * 0.5f) / half, -1f, 1f) * p.BiasMax : 0f;
        }

        /// <summary>슬롯 목표 = 중심 + 앵커 스케일 + 추가 오프셋(역할·피치 컨트롤). GK(0번)는 건드리지 않는다.</summary>
        public static void ComputeTargets(ShapeParams p, in SolvedBlock b, Vec2[] anchors, Vec2[] extra, bool[] slotActive, Vec2[] targets)
        {
            var sx = b.L / p.LTemplate;
            var sy = b.W / p.WTemplate;
            for (int i = 1; i < anchors.Length; i++)
            {
                if (!slotActive[i]) continue;
                targets[i] = new Vec2(b.Cx + anchors[i].X * sx + extra[i].X, b.Cy + anchors[i].Y * sy + extra[i].Y);
            }
        }

        /// <summary>압축 제약 반복 투영 (FR-33). 길이를 넘으면 앞선을 끌어내리고, 폭을 넘으면 중심으로 줄인다. excluded(압박 중)는 건드리지 않고 재지도 않는다.</summary>
        public static void Project(ShapeParams p, in SolvedBlock b, bool[] slotActive, bool[] excluded, Vec2[] targets)
        {
            var maxLen = b.LMax / p.PitchL;
            var maxWid = b.WMax / p.PitchW;
            for (int iter = 0; iter < p.ProjIterations; iter++)
            {
                float back = float.MaxValue, front = float.MinValue, lo = float.MaxValue, hi = float.MinValue;
                for (int i = 1; i < targets.Length; i++)
                {
                    if (!slotActive[i] || excluded[i]) continue;
                    var t = targets[i];
                    if (t.X < back) back = t.X;
                    if (t.X > front) front = t.X;
                    if (t.Y < lo) lo = t.Y;
                    if (t.Y > hi) hi = t.Y;
                }
                if (front < back) return;
                var overLen = front - back > maxLen;
                var overWid = hi - lo > maxWid;
                if (!overLen && !overWid) return;
                var cap = back + maxLen;
                var mid = (lo + hi) * 0.5f;
                var scale = overWid ? maxWid / (hi - lo) : 1f;
                for (int i = 1; i < targets.Length; i++)
                {
                    if (!slotActive[i] || excluded[i]) continue;
                    var t = targets[i];
                    targets[i] = new Vec2(overLen && t.X > cap ? cap : t.X, overWid ? mid + (t.Y - mid) * scale : t.Y);
                }
            }
        }
    }
}
