using System;
using RoadToPro.Core.Random;
using RoadToPro.MatchEngine.Tactics;

namespace RoadToPro.MatchEngine.Scenarios
{
    /// <summary>후보 점수와 추첨 (FR-20~24). 상태 없는 계산이라 고정 상황에서 바로 시험한다.</summary>
    public static class ScenarioSelector
    {
        /// <summary>`기대 가치 × 성공 확률 × 성향 × 전술 × 수식자 × 감쇠 × 분류 사전확률 × 게이트 λ` (FR-20·24).</summary>
        public static float Score(ScenarioDefinition d, ScenarioParams p, float trait, float tactic, float modifier, float decay, float valueGain)
        {
            var xg = d.HasXg ? d.Xg : p.DefaultXg;
            var value = xg > valueGain ? xg : valueGain;
            var success = d.HasSuccessRate ? d.SuccessRate : p.DefaultSuccessRate;
            var gate = d.HasGate && d.Lambda < 1f ? d.Lambda : 1f;
            return value * success * trait * tactic * modifier * decay * Prior(d, p) * gate;
        }

        public static float Prior(ScenarioDefinition d, ScenarioParams p)
        {
            if (d.Category == ScenarioCategory.SP) return d.Group == "PK" ? p.PriorPenalty : p.PriorSetPiece;
            if (d.Category == ScenarioCategory.CT || d.Category == ScenarioCategory.TRN) return p.PriorCounter;
            return p.PriorOpenPlay;
        }

        /// <summary>최근 사용 감쇠 (FR-22). lastTick &lt; 0이면 사용 기록 없음.</summary>
        public static float Decay(long lastTick, long tick, ScenarioParams p)
        {
            if (lastTick < 0 || p.DecayRecoverTicks <= 0) return 1f;
            var t = (float)(tick - lastTick) / p.DecayRecoverTicks;
            if (t >= 1f) return 1f;
            if (t < 0f) t = 0f;
            return p.DecayFactor + (1f - p.DecayFactor) * t;
        }

        /// <summary>전술 지시 배수 (0.5~1.5 범위). 분류에 맞는 지시가 높을수록 커진다.</summary>
        public static float Tactic(ScenarioDefinition d, in TeamInstructions ins, ScenarioParams p)
        {
            float v;
            switch (d.Category)
            {
                case ScenarioCategory.BU: v = 1f - ins.Directness; break;
                case ScenarioCategory.PR: v = ins.Tempo; break;
                case ScenarioCategory.FT: v = ins.Risk; break;
                case ScenarioCategory.CT: case ScenarioCategory.TRN: v = ins.Directness; break;
                case ScenarioCategory.DEF: v = ins.PressIntensity; break;
                default: return 1f;
            }
            return 1f + p.TacticGain * (v - 0.5f);
        }

        public static float Modifier(ScenarioDefinition d, ushort phaseBit, ScenarioLibrary lib, ScenarioParams p)
        {
            var m = 1f;
            var mods = lib.Modifiers;
            for (int i = 0; i < mods.Count; i++)
            {
                var x = mods[i];
                if ((x.CategoryMask & (1u << (int)d.Category)) != 0 && (x.PhaseMask & phaseBit) != 0) m *= x.Weight;
            }
            if (m < p.ModifierMin) return p.ModifierMin;
            if (m > p.ModifierMax) return p.ModifierMax;
            return m;
        }

        /// <summary>판단력 → softmax 온도 (010 FR-20).</summary>
        public static float Temperature(float decisions, ScenarioParams p) => p.TauMax - (p.TauMax - p.TauMin) * decisions;

        /// <summary>
        /// 점수 비례 softmax 추첨 (FR-23). 점수는 최댓값으로 나눠 온도 척도를 맞춘다 (scores를 덮어쓴다).
        /// 유효 후보만 넘긴다(점수 &gt; 0). 합이 0이면 −1 (경계: 폴백).
        /// </summary>
        public static int Pick(float[] scores, int n, float temperature, SeedRng rng)
        {
            var max = 0f;
            for (int i = 0; i < n; i++)
                if (scores[i] > max) max = scores[i];
            if (!(max > 0f)) return -1;
            for (int i = 0; i < n; i++) scores[i] /= max;
            return rng.PickSoftmax(new ReadOnlySpan<float>(scores, 0, n), temperature);
        }
    }
}
