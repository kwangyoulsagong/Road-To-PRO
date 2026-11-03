using RoadToPro.Core.Math;

namespace RoadToPro.MatchEngine.Kinematics
{
    /// <summary>경기 중 피로 (specs/005 FR-30~36). 경기 중 피로 곡선은 여기서만 정의한다.</summary>
    public static class Fatigue
    {
        /// <summary>Vmax_eff = Vmax·(1 − 0.06·(1−W) − 0.05·C) (FR-32).</summary>
        public static float EffectiveMaxSpeed(in PlayerMotionProfile prof, float w, float c, KinematicsParams p) =>
            prof.MaxSpeed * (1f - p.VmaxAcute * (1f - w) - p.VmaxChronic * c);

        /// <summary>a0_eff = a0·(1 − 0.04·(1−W) − 0.02·C) (FR-32).</summary>
        public static float EffectiveMaxAccel(in PlayerMotionProfile prof, float w, float c, KinematicsParams p) =>
            prof.MaxAccel * (1f - p.AccAcute * (1f - w) - p.AccChronic * c);

        /// <summary>반응 지연 = 인지 + 운동 개시 + 피로 가산 (FR-06·34, 가산 기본 0).</summary>
        public static float ReactionDelay(in PlayerMotionProfile prof, float w, KinematicsParams p) =>
            prof.ReactionDelay + p.ReactionAdd * (1f - w);

        /// <summary>능력치 피로 곡선 ∈ [f_min, 1] (FR-36, 002 FR-62가 기술·정신 유효 능력치에 곱한다).</summary>
        public static float Curve(float w, float c, KinematicsParams p) =>
            DMath.Max(p.CurveMin, 1f - p.CurveAcute * (1f - w) - p.CurveChronic * c);

        /// <summary>스프린트 비용 가중치 (1 + 2C) (FR-33 → 010).</summary>
        public static float SprintCostWeight(float c) => 1f + 2f * c;

        /// <summary>
        /// 서브스텝 1회 W·C 갱신 (FR-30·31). v = 서브스텝 끝 속력.
        /// dW/dt = −k_a·k_f·max(0, v − v_crit)/Vmax + r·rec·(1 − W)·[v &lt; v_crit],
        /// dC/dt = k_f·(c_d·v + c_h·max(0, v − v_hi)) (plan [제안] ④·FR-31 모델). 매 스텝 [0,1] 클램프.
        /// </summary>
        public static void Step(ref float w, ref float c, float v, in PlayerMotionProfile prof, KinematicsParams p)
        {
            var vCrit = p.CritRatio * prof.MaxSpeed;
            float dw;
            if (v > vCrit) dw = -p.AcuteDrain * prof.FatigueRateScale * (v - vCrit) / prof.MaxSpeed;
            else dw = p.RecoverRate * prof.RecoveryScale * (1f - w);
            w = Clamp01(w + dw * p.Dt);

            var hir = v > p.HirSpeed ? v - p.HirSpeed : 0f;
            c = Clamp01(c + prof.FatigueRateScale * (p.ChronicDistRate * v + p.ChronicHirRate * hir) * p.Dt);
        }

        static float Clamp01(float x) => x < 0f ? 0f : (x > 1f ? 1f : (x >= 0f ? x : 0f));
    }
}
