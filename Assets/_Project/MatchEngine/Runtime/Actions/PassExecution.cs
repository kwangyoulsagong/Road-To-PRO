using RoadToPro.Core.Math;
using RoadToPro.Core.Random;

namespace RoadToPro.MatchEngine.Actions
{
    /// <summary>
    /// 패스 실행 오차 (specs/008 FR-13·66·71). σ 계산은 순수 함수, 샘플링은 주입된 시드 RNG로 정확히 균등 난수 4개(방향 정규 2 + 속도 정규 2) — 분기 무관 (FR-61).
    /// σ_angle = lerp(7°, 1.5°, a_Passing) × (1 + 0.8·pressure) × (1 + 0.5·(1 − a_Composure)) × 킥 배수 × 버릇 배수,
    /// σ_speed = lerp(15%, 4%, a_Passing) × 킥 배수 × 버릇 배수. 버릇 "정확도 +x%" → σ × (1 − x) (FR-71 ②, 레벨 2는 x × 1.5 [대체]).
    /// </summary>
    public static class PassExecution
    {
        public const int RngPerPass = 4;
        public const float HabitLevel2Scale = 1.5f;

        public static float HabitMul(ActionParams p, bool oneTouch, byte oneTouchLevel)
        {
            if (!oneTouch || oneTouchLevel == 0) return 1f;
            var x = p.HabitOneTouch * (oneTouchLevel >= 2 ? HabitLevel2Scale : 1f);
            return 1f - x;
        }

        /// <summary>방향 σ (deg).</summary>
        public static float SigmaAngleDeg(ActionParams p, float aPassing, float pressure, float aComposure, float kickMul, float habitMul)
            => ActionMath.Lerp(p.ErrAngleMax, p.ErrAngleMin, aPassing) * (1f + p.ErrPressK * pressure) * (1f + p.ErrComposureK * (1f - aComposure)) * kickMul * habitMul;

        /// <summary>속도 σ (비율).</summary>
        public static float SigmaSpeed(ActionParams p, float aPassing, float kickMul, float habitMul)
            => ActionMath.Lerp(p.ErrSpeedMax, p.ErrSpeedMin, aPassing) * kickMul * habitMul;

        /// <summary>
        /// 의도 초속 벡터에 오차를 넣는다: 수평 방향을 N(0, σ_angle)만큼 돌리고 크기를 (1 + N(0, σ_speed))배 (0.2배 미만은 0.2배로).
        /// 수직 성분은 같은 크기 배수만. 정확히 <see cref="RngPerPass"/>개 소비.
        /// </summary>
        public static Vec3 Apply(Vec3 intended, float sigmaAngleDeg, float sigmaSpeed, SeedRng rng, out float angleErrDeg, out float speedErr)
        {
            angleErrDeg = ActionMath.StdNormal(rng) * sigmaAngleDeg;
            speedErr = ActionMath.StdNormal(rng) * sigmaSpeed;
            var mul = 1f + speedErr;
            if (mul < 0.2f) mul = 0.2f;
            var h = ActionMath.Rotate(intended.XY, angleErrDeg * ActionMath.DegToRad) * mul;
            return new Vec3(h.X, h.Y, intended.Z * mul);
        }
    }
}
