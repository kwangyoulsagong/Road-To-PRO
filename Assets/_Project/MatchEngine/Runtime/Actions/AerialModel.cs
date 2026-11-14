using RoadToPro.Core.Math;

namespace RoadToPro.MatchEngine.Actions
{
    /// <summary>
    /// 공중볼 경합 모델 (specs/071 FR-03·04). reach = 키(cm)×1.33/100 + jumpH (+ GK 0.6 m), jumpH = lerp(0.35, 0.70, a_Jumping) × runUpBonus × staminaFactor,
    /// timing = clamp01(1 − |t_arrive − t_land| / 0.5), S = 4.0·reach + 1.5·timing + 0.8·a_Strength + 0.3·a_Heading·0.3 + 0.6·a_Positioning,
    /// P(win) = softmax(S) (최댓값 빼기). 키 단독이 아니다(FR-04). 평가 함수는 RNG 없음, 승자 표본은 균등 1개(호출자).
    /// </summary>
    public static class AerialModel
    {
        public static float RunUpBonus(ActionParams p, float approachSpeed)
        {
            var t = p.AerialRunUpSpeed > 1e-3f ? approachSpeed / p.AerialRunUpSpeed : 0f;
            if (!(t > 0f)) t = 0f;
            if (t > 1f) t = 1f;
            return 1f + (p.AerialRunUpMax - 1f) * t;
        }

        public static float StaminaFactor(ActionParams p, float aStamina) => ActionMath.Lerp(p.AerialStaminaLo, 1f, aStamina);

        public static float JumpHeight(ActionParams p, float aJumping, float approachSpeed, float aStamina) =>
            ActionMath.Lerp(p.AerialJumpLo, p.AerialJumpHi, aJumping) * RunUpBonus(p, approachSpeed) * StaminaFactor(p, aStamina);

        public static float Reach(ActionParams p, float heightCm, float jumpH, bool keeperHands) =>
            heightCm * p.AerialHeightK / 100f + jumpH + (keeperHands ? p.AerialGkBonus : 0f);

        public static float Timing(ActionParams p, float tArrive, float tLand)
        {
            var w = p.AerialTimingWin > 1e-4f ? p.AerialTimingWin : 1e-4f;
            var v = 1f - DMath.Abs(tArrive - tLand) / w;
            if (!(v > 0f)) return 0f;
            return v > 1f ? 1f : v;
        }

        public static float Score(ActionParams p, float reach, float timing, float aStrength, float aHeading, float aPositioning) =>
            p.AerialWReach * reach + p.AerialWTiming * timing + p.AerialWStr * aStrength + p.AerialWHead * aHeading * p.AerialHeadScale + p.AerialWPos * aPositioning;

        /// <summary>softmax 확률을 prob[0..n)에 채운다 (최댓값 빼기 — 큰 점수에서도 오버플로 없음). n = 0이면 아무것도 안 함.</summary>
        public static void Softmax(float[] score, int n, float[] prob)
        {
            if (n <= 0) return;
            var max = score[0];
            for (int i = 1; i < n; i++) if (score[i] > max) max = score[i];
            var sum = 0f;
            for (int i = 0; i < n; i++) { prob[i] = DMath.Exp(score[i] - max); sum += prob[i]; }
            if (!(sum > 0f)) { for (int i = 0; i < n; i++) prob[i] = 1f / n; return; }
            for (int i = 0; i < n; i++) prob[i] /= sum;
        }

        /// <summary>확률 prob[0..n)에서 균등 u로 하나 고른다. n = 0이면 −1.</summary>
        public static int Pick(float[] prob, int n, float u)
        {
            if (n <= 0) return -1;
            var acc = 0f;
            for (int i = 0; i < n; i++)
            {
                acc += prob[i];
                if (u < acc) return i;
            }
            return n - 1;
        }
    }
}
