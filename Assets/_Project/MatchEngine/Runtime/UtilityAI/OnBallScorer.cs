using RoadToPro.Core.Math;

namespace RoadToPro.MatchEngine.UtilityAI
{
    /// <summary>
    /// 온볼 기본 점수 (specs/072 FR-10~13): 이득 = P^α · sign(V)·|V|^β (α·β = Directness 보간, FR-11), 손실 = (1 − P) · 상실 지점 상대 xT · Risk 가중.
    /// 이득에만 결합 배수(003 대체, 항목별 곱)·케미(패스만, 033 배수 1 + k·chem)·원터치 템포 보너스를 곱한다 — 손실 항에는 곱하지 않는다(010 FR-11, AC-02).
    /// 버릇 정확도 보정(003 FR-21)은 점수에 곱하지 않는다(FR-13). 순수 함수 묶음, RNG·할당 없음.
    /// </summary>
    public sealed class OnBallScorer
    {
        readonly DecisionParams _p;
        readonly IStyleMultiplierSource _style;

        public OnBallScorer(DecisionParams p, IStyleMultiplierSource style)
        {
            _p = p;
            _style = style;
        }

        static float Clamp01(float v) => v < 0f ? 0f : (v > 1f ? 1f : v);

        /// <summary>Directness 0~1 → β (높을수록 기대 가치 크기를 키운다, [대체]).</summary>
        public float Beta(float directness) => _p.OnBallBetaMax + (_p.OnBallBetaMin - _p.OnBallBetaMax) * Clamp01(directness);

        /// <summary>Risk 0~1 → 손실 가중 ([대체]).</summary>
        public float RiskWeight(float risk) => _p.OnBallRiskLossMax + (_p.OnBallRiskLossMin - _p.OnBallRiskLossMax) * Clamp01(risk);

        /// <summary>이득 원형 P^α · sign(V)·|V|^β.</summary>
        public static float GainOf(float p, float v, float alpha, float beta)
        {
            var pa = p <= 0f ? 0f : (alpha == 1f ? p : DMath.Pow(p, alpha));
            var av = DMath.Abs(v);
            var vb = beta == 1f ? av : (av <= 0f ? 0f : DMath.Pow(av, beta));
            return pa * (v < 0f ? -vb : vb);
        }

        /// <summary>케미 배수 (033 제안 식, FR-12).</summary>
        public float ChemMultiplier(float chem) => 1f + _p.OnBallChemK * Clamp01(chem);

        public void Score(DecisionWorld w, int s, bool oneTouch, ref OnBallCandidate c)
        {
            var t = DecisionWorld.TeamOf(s);
            var gain = GainOf(c.P, c.V, _p.OnBallAlpha, Beta(w.Directness[t]));
            if (OnBallItems.IsPass(c.Item))
            {
                gain *= ChemMultiplier(c.Chem);
                if (oneTouch) gain *= 1f + _p.OnBallOneTouchTempo * (2f * Clamp01(w.Tempo[t]) - 1f); // FR-11 Tempo 원터치 보너스
            }
            c.GainBase = gain;

            float m = 1f, mf = 1f;
            c.Suppressed = false;
            for (int i = 0; i < c.Cons.Count; i++)
            {
                var k = c.Cons[i];
                m *= _style.Multiplier(s, k);
                mf *= _style.MultiplierFullCompliance(s, k);
                if (!c.Suppressed && _style.InstructionWeight(s, k) < 1f) { c.Suppressed = true; c.SuppressedKind = k; }
            }
            c.Mult = m;
            c.MultFull = mf;
            c.Gain = gain * m;
            c.Loss = (1f - Clamp01(c.P)) * c.LossXt * RiskWeight(w.Risk[t]);
            c.Utility = c.Gain - c.Loss;
        }

        /// <summary>준수율 1로 다시 낸 점수 (FR-32 판정용).</summary>
        public static float FullComplianceUtility(in OnBallCandidate c) => c.GainBase * c.MultFull - c.Loss;
    }
}
