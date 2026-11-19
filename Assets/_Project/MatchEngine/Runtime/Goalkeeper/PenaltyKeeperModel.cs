using RoadToPro.Core.Math;
using RoadToPro.Core.Random;
using RoadToPro.MatchEngine.Actions;

namespace RoadToPro.MatchEngine.Goalkeeper
{
    /// <summary>PK GK 전략 표본 (specs/074 FR-01·03).</summary>
    public struct PenaltyStrategy
    {
        /// <summary>−1·0·+1 (골문 y 증가 쪽 +1, 0 = 서서 반응).</summary>
        public int Dir;
        /// <summary>기울기 l = μ + N(0, σ_dir·압박 배수).</summary>
        public float Lean;
        /// <summary>조기 움직임 시각 t_e (s, 킥 기준).</summary>
        public float Lead;
        /// <summary>킥 순간 골라인 이탈 (012 재차기 입력).</summary>
        public bool OffLine;
    }

    /// <summary>승부차기 한 번 안의 GK 쪽 누적 (FR-04): 상대 키커가 찬 방향 합(+1 = 골문 y 증가 쪽)과 다이빙 수. 값 타입.</summary>
    public struct ShootoutKeeperMemory
    {
        public int Balance;
        public int Dives;

        public void Record(int shotSide, int keeperDir)
        {
            Balance += shotSide;
            if (keeperDir != 0) Dives++;
        }
    }

    /// <summary>
    /// PK GK 쪽 혼합전략 (specs/074 FR-01·03·04, 리서치 04 § A1, 리서치 03 § A9). 키커 쪽(071 FR-17)과 독립으로 기울기 l을 한 번 뽑는다:
    /// μ = 키커 자연 측 부호 × naturalBias + memoryWeight × 승부차기 기억, l = μ + N(0, σ_dir × 압박 배수). |l| &lt; 중앙 폭이면 서서 반응(011 FR-10),
    /// 아니면 l 부호 쪽으로 킥 t_e(균등 [leadMin, leadMax]) 전에 다이빙을 시작한다. 판정은 011 <see cref="SaveModel"/>의 조기 출발 입력.
    /// RNG 순서 고정: 기울기 정규 1 → t_e 1 → 라인 이탈 1. 할당 없음.
    /// </summary>
    public sealed class PenaltyKeeperModel
    {
        readonly GoalkeeperParams _p;

        public PenaltyKeeperModel(GoalkeeperParams p) => _p = p;

        /// <summary>FR-04 압박 배수: 승부차기면 1 + (shootoutMul − 1)·(1 − BigMatch_n)·(1 − Composure_n), 아니면 1. 키커 σ와 GK σ_dir 공용.</summary>
        public static float PressureMul(GoalkeeperParams p, bool shootout, float bigMatchN, float composureN)
        {
            if (!shootout) return 1f;
            var b = bigMatchN < 0f ? 0f : (bigMatchN > 1f ? 1f : bigMatchN);
            var c = composureN < 0f ? 0f : (composureN > 1f ? 1f : composureN);
            return 1f + (p.PkShootoutMul - 1f) * (1f - b) * (1f - c);
        }

        /// <summary>BigMatch 버릇 레벨(0~2) → 0~1 (003 능력치 정의 전 대체, 플랜 위험).</summary>
        public static float BigMatchN(int habitLevel) => habitLevel <= 0 ? 0f : (habitLevel >= 2 ? 1f : 0.5f);

        /// <summary>키커 자연 측 (골문 y 부호): 071 <see cref="ShotSystem.PenaltyCell"/>과 같은 규칙 — 주발 반대쪽, 키커가 골문을 볼 때 왼쪽 = 공격 방향 부호.</summary>
        public static int NaturalSign(Foot kickerFoot, int kickerAttackDir)
        {
            var leftSign = kickerAttackDir > 0 ? 1 : -1;
            return kickerFoot == Foot.Left ? -leftSign : leftSign;
        }

        /// <summary>기울기 평균 μ.</summary>
        public float Mean(int naturalSign, int memoryBalance) => naturalSign * _p.PkNaturalBias + _p.PkMemoryWeight * memoryBalance;

        public PenaltyStrategy Draw(int naturalSign, int memoryBalance, float pressureMul, SeedRng rng)
        {
            var sigma = _p.PkSigmaDir * (pressureMul > 0f ? pressureMul : 1f);
            var lean = Mean(naturalSign, memoryBalance) + rng.Normal(0f, sigma);
            var lead = rng.Range(_p.PkLeadMin, _p.PkLeadMax);
            var off = rng.NextFloat() < _p.PkOffLineP;
            return new PenaltyStrategy { Dir = DMath.Abs(lean) < _p.PkCenterBand ? 0 : (lean > 0f ? 1 : -1), Lean = lean, Lead = lead, OffLine = off };
        }

        /// <summary>FR-04 승부차기 피로: 다이빙 수만큼 v_dive × (1 − fatigueDive·dives), 하한 0.5배.</summary>
        public KeeperProfile Fatigued(in KeeperProfile k, int dives)
        {
            var f = 1f - _p.PkFatigueDive * (dives > 0 ? dives : 0);
            if (f < 0.5f) f = 0.5f;
            var r = k;
            r.DiveSpeed = k.DiveSpeed * f;
            return r;
        }
    }
}
