using RoadToPro.Core.Math;
using RoadToPro.Core.Random;

namespace RoadToPro.MatchEngine.Referee
{
    /// <summary>
    /// 심판 인지 (specs/012 FR-07·27·31·32, 플랜 P2·P8). 진짜 판정 + 노이즈 + 홈 편향 = 심판이 본 판정.
    /// 시야 품질 = 거리 항 × 가림 항 × 각도 항, 가까운 부심이 보완. RNG는 인자로만 받는다 (결정론, 호출마다 소비 개수 고정).
    /// </summary>
    public static class Perception
    {
        static float C01(float v) => v > 0f ? (v < 1f ? v : 1f) : 0f;

        /// <summary>FR-32 거리 항: 0 m nearMin → bestMin 1 → bestMax 1 → far distMin (선형).</summary>
        public static float DistanceTerm(RefereeParams p, float d)
        {
            if (d < p.SightBestMin) return p.SightNearMin + (1f - p.SightNearMin) * (p.SightBestMin > 1e-3f ? d / p.SightBestMin : 1f);
            if (d <= p.SightBestMax) return 1f;
            var span = p.SightFar - p.SightBestMax;
            var t = span > 1e-3f ? C01((d - p.SightBestMax) / span) : 1f;
            return 1f + (p.SightDistMin - 1f) * t;
        }

        /// <summary>
        /// FR-32 시야 품질 (0.05~1): 주심 위치 → 사건 지점. a·b(사건 당사자)는 가림에서 뺀다. attackerVel은 각도 항(시선과 진행 방향이 직각일수록 잘 보임).
        /// </summary>
        public static float SightQuality(RefereeParams p, RefereeWorld w, Vec2 at, int a, int b, Vec2 attackerVel)
        {
            var refPos = w.Officials[0];
            var v = at - refPos;
            var len = v.Length();
            var q = DistanceTerm(p, len);
            if (len > 1e-3f)
            {
                var dir = v / len;
                var occ = 0;
                for (int s = 0; s < RefereeWorld.N && occ < p.OcclusionMaxInt; s++)
                {
                    if (!w.Active[s] || s == a || s == b) continue;
                    var r = w.Pos[s] - refPos;
                    var along = r.X * dir.X + r.Y * dir.Y;
                    if (along <= 1f || along >= len - 1f) continue;
                    var perp = DMath.Abs(r.X * dir.Y - r.Y * dir.X);
                    if (perp < p.OcclusionWidth) occ++;
                }
                q *= C01(1f - p.OcclusionPenalty * occ);
                var vl = attackerVel.Length();
                if (vl > 0.5f)
                {
                    var sin = DMath.Abs(dir.X * attackerVel.Y - dir.Y * attackerVel.X) / vl;
                    q *= p.SightAngleBase + (1f - p.SightAngleBase) * sin;
                }
            }
            // 가까운 부심 보완 (부심 1·2 중 가까운 쪽)
            var ar = DMath.Min((w.Officials[1] - at).Length(), (w.Officials[2] - at).Length());
            if (ar < p.AssistantRange) q += p.AssistantAssist * (1f - q);
            return q < p.SightFloor ? p.SightFloor : (q > 1f ? 1f : q);
        }

        /// <summary>P2 노이즈 σ = σ0·(1.5 − 일관성)·(1.5 − 시야)·(1 + k·(1 − 압박 저항)·압박).</summary>
        public static float Sigma(RefereeParams p, RefereeProfile r, float sight, float pressure) =>
            p.Sigma0 * (1.5f - r.Consistency) * (1.5f - C01(sight)) * (1f + p.PressureK * (1f - r.PressureResistance) * C01(pressure));

        /// <summary>FR-31: 인지 점수 = 진짜 + N(0, σ) + 홈 편향(홈 팀이 피해일 때). RNG 1회.</summary>
        public static float Perceive(RefereeParams p, RefereeProfile r, float trueScore, float sight, bool homeVictim, float pressure, SeedRng rng)
        {
            var n = rng.Normal(0f, Sigma(p, r, sight, pressure));
            return trueScore + n + (homeVictim ? r.HomeBias * p.HomeBiasMax : 0f);
        }

        /// <summary>P8 다이빙 속임 확률.</summary>
        public static float DiveFoolProb(RefereeParams p, RefereeProfile r, float sight, bool weakContact) =>
            C01(p.DiveFoolBase * (1f - C01(sight)) * (1f - 0.5f * r.Consistency) + (weakContact ? p.DiveFoolWeak : 0f));

        /// <summary>FR-07 부심 σ_AR = σ0 + k·|부심 x − 기준선 x|.</summary>
        public static float AssistantSigma(RefereeParams p, float alignDiff) => p.OffsideSigma0 + p.OffsideSigmaK * DMath.Abs(alignDiff);

        /// <summary>
        /// FR-07 부심 지각 d: d + N(0, σ_AR) + flash-lag(공격수 쪽, U(min, max) × clamp(공격수 속도 / 기준)). RNG 2회(정규·균등) 고정.
        /// </summary>
        public static float AssistantPerceive(RefereeParams p, float d, float alignDiff, float attackerSpeed, SeedRng rng)
        {
            var n = rng.Normal(0f, AssistantSigma(p, alignDiff));
            var u = rng.Range(p.FlashLagMin, p.FlashLagMax);
            var k = p.FlashLagSpeed > 1e-3f ? C01(attackerSpeed / p.FlashLagSpeed) : 0f;
            return d + n + u * k;
        }

        /// <summary>판정 기준선 쪽 부심 (1 = x ≥ 하프, 2 = x &lt; 하프 — 대각선 시스템) 의 정렬 차 |부심 x − 기준선 x|.</summary>
        public static float AssistantAlign(RefereeWorld w, float lineX)
        {
            var ar = lineX >= w.PitchL * 0.5f ? w.Officials[1] : w.Officials[2];
            return DMath.Abs(ar.X - lineX);
        }
    }
}
