using RoadToPro.Core.Math;

namespace RoadToPro.MatchEngine.Referee
{
    /// <summary>
    /// 심판 위치 목표 (specs/012 FR-80, 리서치 07 § 1.5 대각선 시스템). 주심: 목표 = lerp(공, 대각선 위 점, 0.6), 공에서 15~20 m.
    /// 대각선 = (0, 0) → (L, W) (부심 1이 낮은 터치라인·높은 x 반, 부심 2가 높은 터치라인·낮은 x 반 — 주심은 반대 대각선).
    /// 부심: x만, x = 담당 반에서 뒤에서 두 번째 수비수와 공 중 골라인에 가까운 쪽. 속도 한계 = 체력 선형. RNG 없음.
    /// </summary>
    public static class OfficialsMotion
    {
        public static float MaxSpeed(RefereeParams p, float fitness) => p.RefSpeedMin + (p.RefSpeedMax - p.RefSpeedMin) * (fitness < 0f ? 0f : (fitness > 1f ? 1f : fitness));

        public static Vec2 RefereeTarget(RefereeParams p, Vec2 ball, float L, float W)
        {
            var diag = new Vec2(L, W);
            var dl2 = diag.LengthSq();
            var t = dl2 > 1e-3f ? (ball.X * diag.X + ball.Y * diag.Y) / dl2 : 0f;
            t = t < 0f ? 0f : (t > 1f ? 1f : t);
            var onDiag = diag * t;
            var target = ball + (onDiag - ball) * p.DiagLerp;
            var v = target - ball;
            var len = v.Length();
            Vec2 dir;
            if (len < 1e-3f)
            {
                // 공이 대각선 위: 대각선 수직 방향(낮은 x·높은 y 쪽)으로 비켜선다
                var n = new Vec2(-diag.Y, diag.X);
                dir = n / DMath.Max(n.Length(), 1e-3f);
                len = 0f;
            }
            else dir = v / len;
            var d = len < p.RefDistMin ? p.RefDistMin : (len > p.RefDistMax ? p.RefDistMax : len);
            var r = ball + dir * d;
            return new Vec2(Clamp(r.X, 1f, L - 1f), Clamp(r.Y, 1f, W - 1f));
        }

        /// <summary>부심 1 (높은 x 반, 낮은 터치라인 밖): x = max(뒤에서 두 번째 수비수 x, 공 x)를 [L/2, L]로.</summary>
        public static Vec2 AssistantHigh(RefereeParams p, float secondLastX, float ballX, float L) =>
            new Vec2(Clamp(secondLastX > ballX ? secondLastX : ballX, L * 0.5f, L), -p.AssistantOffset);

        /// <summary>부심 2 (낮은 x 반, 높은 터치라인 밖): x = min(뒤에서 두 번째 수비수 x, 공 x)를 [0, L/2]로.</summary>
        public static Vec2 AssistantLow(RefereeParams p, float secondLastX, float ballX, float L, float W) =>
            new Vec2(Clamp(secondLastX < ballX ? secondLastX : ballX, 0f, L * 0.5f), W + p.AssistantOffset);

        static float Clamp(float v, float lo, float hi) => v < lo ? lo : (v > hi ? hi : v);
    }
}
