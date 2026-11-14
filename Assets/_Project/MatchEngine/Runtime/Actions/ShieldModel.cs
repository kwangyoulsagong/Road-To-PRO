using RoadToPro.Core.Random;

namespace RoadToPro.MatchEngine.Actions
{
    /// <summary>
    /// 몸싸움 · 볼 지키기 (specs/070 FR-20, 005 FR-24가 넘긴 판정). 계수 [대체]:
    /// P_keep = sigmoid(k0 + k1·(0.6·근력_공격 + 0.4·밸런스_공격 − 0.6·근력_수비 − 0.4·밸런스_수비)). 판정 1회 = 균등 난수 1개.
    /// </summary>
    public static class ShieldModel
    {
        public const int RngPerShield = 1;

        public static float KeepProbability(ActionParams p, ActionWorld w, int carrier, int challenger)
        {
            var att = 0.6f * w.Strength[carrier] + 0.4f * w.Balance[carrier];
            var def = 0.6f * w.Strength[challenger] + 0.4f * w.Balance[challenger];
            return ActionMath.Prob(p.ShieldK0 + p.ShieldK1 * (att - def), p);
        }

        /// <summary>true = 공을 지킴. RNG 정확히 1.</summary>
        public static bool Resolve(float keepProb, SeedRng rng) => rng.NextFloat() < keepProb;
    }
}
