namespace RoadToPro.MatchEngine.UtilityAI
{
    /// <summary>
    /// 평가 노이즈·압박·집중 저하·친숙도 (specs/010 FR-21·23·26·86). 순수 함수 — RNG는 호출자가 선수 결정 스트림에서 정해진 순서로 뽑는다(FR-25).
    /// σ 식·h0·리더십 반경·친숙도 가산은 스펙 [미정] 자리의 [대체] 값(decision.json).
    /// </summary>
    public static class EvalNoise
    {
        static float Clamp01(float v) => v < 0f ? 0f : (v > 1f ? 1f : v);

        /// <summary>기본 σ = base + slope·(1 − (s_Dec + s_Vis)/2) (FR-21 [대체]).</summary>
        public static float Sigma(DecisionParams p, float sDecisions, float sVision) =>
            p.NoiseBase + p.NoiseSlope * (1f - (Clamp01(sDecisions) + Clamp01(sVision)) * 0.5f);

        /// <summary>압박 하 σ × (1 + P·(1 − s_Composure)) (FR-21, 리서치 03 § B).</summary>
        public static float UnderPressure(float sigma, float pressure, float sComposure) =>
            sigma * (1f + Clamp01(pressure) * (1f - Clamp01(sComposure)));

        /// <summary>압박도 P = 1 − (상대 최근접 거리 / 반경), 0~1 (FR-21 [대체]).</summary>
        public static float Pressure(DecisionParams p, float nearestOpponentDist) =>
            p.PressureRadius > 0f ? Clamp01(1f - nearestOpponentDist / p.PressureRadius) : 0f;

        /// <summary>집중 저하 hazard h = h0·(1 − s_Concentration)·(1 + C)·(1 − 리더 완화) (FR-26·86).</summary>
        public static float LapseHazard(DecisionParams p, float sConcentration, float chronicC, float leaderRelief) =>
            p.LapseH0 * (1f - Clamp01(sConcentration)) * (1f + Clamp01(chronicC)) * (1f - Clamp01(leaderRelief));

        /// <summary>반경 안 동료 중 최고 리더십 정규화값 → 완화 비율 (FR-86).</summary>
        public static float LeaderRelief(DecisionParams p, float bestLeadershipN) => p.LeaderRelief * Clamp01(bestLeadershipN);

        /// <summary>친숙도 낮으면 온도 가산 (FR-23).</summary>
        public static float FamiliarityTau(DecisionParams p, float familiarityN) => p.FamiliarityTauAdd * (1f - Clamp01(familiarityN));
    }
}
