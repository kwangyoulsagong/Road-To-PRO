namespace RoadToPro.MatchEngine.UtilityAI
{
    /// <summary>
    /// 오프볼 점수 (specs/010 FR-11·15·71·83·84·100): U = (이득 × 결합 배수 × 전술·성격·피로 보정) − 손실.
    /// 결합 배수는 이득 항에만 곱한다(FR-11). 결합 배수 0인 후보는 제외(U = 0, Mult = 0). 결합 배수는 003 소유라 여기서 다시 정의하지 않는다.
    /// 전술·성격·피로 보정은 <see cref="OffBallCandidate.Tactic"/>에 분해해 남긴다(FR-60).
    /// </summary>
    public sealed class OffBallScorer
    {
        readonly DecisionParams _p;
        readonly IStyleMultiplierSource _style;

        public OffBallScorer(DecisionParams p, IStyleMultiplierSource style)
        {
            _p = p;
            _style = style;
        }

        static float Centered(float internalValue) => DecisionAttributes.N(internalValue) - 0.5f;

        /// <summary>다혈질 → 태클 시도 효용 배수 (FR-83). 070 전에는 대체 태클 시도 확률에 곱한다.</summary>
        public static float TackleAttemptScale(DecisionParams p, float temperament) => 1f + p.PersonaTemperament * Centered(temperament);

        public void Score(DecisionWorld w, int s, ref OffBallCandidate c)
        {
            c.Mult = _style.Multiplier(s, c.Kind);
            c.MultFull = _style.MultiplierFullCompliance(s, c.Kind);
            if (c.Mult <= 0f) { c.Tactic = 0f; c.Utility = 0f; return; }
            var t = DecisionWorld.TeamOf(s);
            var m = w.Mind[s];
            float f = 1f;

            // 공 몰림 억제 (FR-15): 자기 빼고 공 근처 동료가 이미 n명이면 공 쪽 후보를 깎는다
            if (Considerations.IsBallward(c.Kind))
            {
                var near = w.NearBall[t];
                if ((w.Pos[s] - w.Ball).LengthSq() <= _p.CrowdRadius * _p.CrowdRadius) near--;
                if (near >= (int)_p.CrowdCount) f *= _p.CrowdPenalty;
            }

            // 성격 (FR-83·84)
            if (c.Kind == Consideration.Press || c.Kind == Consideration.CoverShadow)
                f *= 1f + _p.PersonaTemperament * Centered(m.Temperament);
            if (c.Kind == Consideration.RecoveryRun || Considerations.IsUrgent(c.Kind))
                f *= 1f + _p.PersonaWorkRate * Centered(m.WorkRate) + _p.PersonaProfessionalism * Centered(m.Professionalism) + _p.PersonaAmbition * Centered(m.Ambition);

            // 질주 비용 (FR-71·102): 만성 피로 C가 쌓이면 급한 행동의 이득이 줄어든다
            if (Considerations.IsUrgent(c.Kind))
                f /= 1f + _p.SprintCost * 2f * w.C[s];

            if (f < 0f) f = 0f;
            c.Tactic = f;
            c.Utility = c.Gain * c.Mult * f - c.Loss;
        }

        /// <summary>준수율 1로 다시 낸 점수 (072 FR-32). 결합 배수 0으로 빠졌던 후보는 전술 보정 없이(1) 근사.</summary>
        public static float FullComplianceUtility(in OffBallCandidate c) => c.Gain * c.MultFull * (c.Mult > 0f ? c.Tactic : 1f) - c.Loss;
    }
}
