using System.Runtime.CompilerServices;
using RoadToPro.Core.Math;

namespace RoadToPro.MatchEngine.Ball
{
    /// <summary>공기역학 계수 (specs/004 FR-02·03).</summary>
    public static class BallAero
    {
        /// <summary>이 속도 미만이면 항력·마그누스 항을 0으로 (경계 상황: 0으로 나누기 방지).</summary>
        public const float MinSpeed = 1e-4f;

        /// <summary>Cd(v) = CdHigh + (CdLow − CdHigh) / (1 + exp((v − vCrit)/w)) (FR-02).</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float Cd(float speed, BallParams p)
        {
            var x = (speed - p.CdCritSpeed) / p.CdTransitionW;
            // exp 오버플로 방지: 매우 큰 x면 분모가 무한대 → 항은 0
            if (x > 60f) return p.CdHigh;
            return p.CdHigh + (p.CdLow - p.CdHigh) / (1f + DMath.Exp(x));
        }

        /// <summary>C_L(Sp) = clMax·(1 − exp(−Sp/spScale)) (FR-03).</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float ClOf(float spinParameter, BallParams p) =>
            p.ClMax * (1f - DMath.Exp(-spinParameter / p.ClSpScale));

        /// <summary>저속 페이드: v &lt; minSpeed → 0, minSpeed..minSpeed+fadeWidth 선형, 이후 1 (FR-03, plan [제안] 승인).</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float MagnusFade(float speed, BallParams p)
        {
            if (speed < p.MagnusMinSpeed) return 0f;
            if (p.MagnusFadeWidth <= 0f) return 1f;
            var f = (speed - p.MagnusMinSpeed) / p.MagnusFadeWidth;
            return f >= 1f ? 1f : f;
        }

        /// <summary>
        /// 비행 중 가속도: 중력 + 항력 + 마그누스 (FR-01~03).
        /// a = −g·ẑ − β·Cd(v)·|v|·v + β·C_L·v²·(ω̂×v̂).
        /// </summary>
        public static Vec3 AirAcceleration(Vec3 velocity, Vec3 spin, BallParams p)
        {
            var ax = 0f;
            var ay = 0f;
            var az = -p.Gravity;
            var s2 = velocity.LengthSq();
            if (s2 >= MinSpeed * MinSpeed)
            {
                var s = DMath.Sqrt(s2);
                var k = p.Beta * Cd(s, p) * s;
                ax -= k * velocity.X;
                ay -= k * velocity.Y;
                az -= k * velocity.Z;

                var fade = MagnusFade(s, p);
                if (fade > 0f)
                {
                    var w2 = spin.LengthSq();
                    if (w2 > 0f)
                    {
                        var w = DMath.Sqrt(w2);
                        var cl = ClOf(p.Radius * w / s, p) * fade;
                        // β·C_L·v²·(ω̂×v̂) = β·C_L·(v/w)·(ω×v)
                        var m = p.Beta * cl * s / w;
                        ax += m * (spin.Y * velocity.Z - spin.Z * velocity.Y);
                        ay += m * (spin.Z * velocity.X - spin.X * velocity.Z);
                        az += m * (spin.X * velocity.Y - spin.Y * velocity.X);
                    }
                }
            }
            return new Vec3(ax, ay, az);
        }

        /// <summary>구름 감속 크기 a + b·v + β·Cd(v)·v² (FR-06). 땅볼 역산도 같은 함수를 쓴다.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float RollDeceleration(float speed, BallParams p) =>
            p.RollA + p.RollB * speed + p.Beta * Cd(speed, p) * speed * speed;
    }
}
