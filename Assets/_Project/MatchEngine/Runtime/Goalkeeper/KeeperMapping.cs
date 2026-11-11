using RoadToPro.Core.Math;

namespace RoadToPro.MatchEngine.Goalkeeper
{
    /// <summary>
    /// GK 능력치 → 모델 파라미터 (specs/011 FR-70, 리서치 04 § A7). 011이 소유하고 073·074가 같은 값을 쓴다. 순수 함수, 할당 없음.
    /// n은 002 FR-14 정규화(내부/100).
    /// </summary>
    public static class KeeperMapping
    {
        /// <summary>Reflexes → t_react = 0.32 − 0.14·n s.</summary>
        public static float ReactTime(GoalkeeperParams p, float n) => p.MapReactBase - p.MapReactSlope * n;
        /// <summary>Diving → v_dive = 3.0 + 1.6·n m/s.</summary>
        public static float DiveSpeed(GoalkeeperParams p, float n) => p.MapDiveBase + p.MapDiveSlope * n;
        /// <summary>r_max = 기준 × 키/기준 키 + 0.3·n(Diving) m (FR-03).</summary>
        public static float MaxReach(GoalkeeperParams p, float heightM, float divingN) => p.ReachRmaxBase * (heightM / p.ReachRefHeight) + p.MapRmaxSlope * divingN;
        /// <summary>손 도달 높이(서서 팔 뻗음) = 기준 × 키/기준 키 + Jumping 보정.</summary>
        public static float StandReachZ(GoalkeeperParams p, float heightM, float jumpingN) => p.ReachStandZ * (heightM / p.ReachRefHeight) + p.MapJumpSlope * jumpingN;
        /// <summary>Handling → a = 2 + 4n (잡기 기울기).</summary>
        public static float HandlingA(GoalkeeperParams p, float n) => p.MapHandlingBase + p.MapHandlingSlope * n;
        /// <summary>Handling → spill 거리 평균 = 6 − 3n m (FR-12, 방향 2026-11-11 사용자 결정).</summary>
        public static float SpillMean(GoalkeeperParams p, float n) => p.MapSpillBase - p.MapSpillSlope * n;
        /// <summary>GKPositioning → 위치 오차 σ = 0.6 − 0.5n m (FR-06), 하한 0.</summary>
        public static float PositionSigma(GoalkeeperParams p, float n) => DMath.Max(0f, p.MapPosNoiseBase - p.MapPosNoiseSlope * n);
        /// <summary>GKPositioning → 위험도 후보 수: 기준 × (0.5 + 0.5n)을 3의 배수(깊이 줄 × 좌우 3)로 반올림, 최소 3 (FR-06 "후보 수 증가").</summary>
        public static int CandidateCount(GoalkeeperParams p, float n)
        {
            var rows = (int)DMath.Floor(p.PosCandidates * (0.5f + 0.5f * n) / 3f + 0.5f);
            return rows < 1 ? 3 : rows * 3;
        }
        /// <summary>OneOnOne → σ_t = 0.15 − 0.1n s (073).</summary>
        public static float OneOnOneSigma(GoalkeeperParams p, float n) => p.MapOneOnOneBase - p.MapOneOnOneSlope * n;
        /// <summary>Jumping → 점프 도달 +0.3·n m (073).</summary>
        public static float JumpBonus(GoalkeeperParams p, float n) => p.MapJumpSlope * n;
        /// <summary>CommandArea → 결단 지연 0.2·(1 − n) s (073).</summary>
        public static float CommandDelay(GoalkeeperParams p, float n) => p.MapCommandSlope * (1f - n);
        /// <summary>Sweeping → 마진 m = 0.4 − 0.5n s (073, 음수면 공격적).</summary>
        public static float SweepMargin(GoalkeeperParams p, float n) => p.MapSweepingBase - p.MapSweepingSlope * n;
        /// <summary>Distribution → σ_angle = 6° − 4.5°·n (074).</summary>
        public static float DistributionSigmaDeg(GoalkeeperParams p, float n) => p.MapDistributionBase - p.MapDistributionSlope * n;
        /// <summary>010 FR-20 공통 온도 τ = τ_max − (τ_max − τ_min)·n (Decisions).</summary>
        public static float Temperature(GoalkeeperParams p, float n) => p.ChoiceTauMax - (p.ChoiceTauMax - p.ChoiceTauMin) * n;
    }

    /// <summary>경기 중 GK 한 명의 매핑 결과 (슬롯에 선수가 들어올 때 한 번 계산). 값 타입.</summary>
    public struct KeeperProfile
    {
        public float ReactTime, DiveSpeed, MaxReach, StandReachZ, HandlingA, SpillMean, PositionSigma, Temperature;
        public float HandlingN, PositioningN, DecisionsN;
        public int Candidates;

        public static KeeperProfile From(GoalkeeperParams p, in KeeperAttributes a)
        {
            var reflexes = KeeperAttributes.N(a.Reflexes);
            var diving = KeeperAttributes.N(a.Diving);
            var handling = KeeperAttributes.N(a.Handling);
            var positioning = KeeperAttributes.N(a.GKPositioning);
            var decisions = KeeperAttributes.N(a.Decisions);
            return new KeeperProfile
            {
                ReactTime = KeeperMapping.ReactTime(p, reflexes),
                DiveSpeed = KeeperMapping.DiveSpeed(p, diving),
                MaxReach = KeeperMapping.MaxReach(p, a.HeightM, diving),
                StandReachZ = KeeperMapping.StandReachZ(p, a.HeightM, KeeperAttributes.N(a.Jumping)),
                HandlingA = KeeperMapping.HandlingA(p, handling),
                SpillMean = KeeperMapping.SpillMean(p, handling),
                PositionSigma = KeeperMapping.PositionSigma(p, positioning),
                Temperature = KeeperMapping.Temperature(p, decisions),
                HandlingN = handling,
                PositioningN = positioning,
                DecisionsN = decisions,
                Candidates = KeeperMapping.CandidateCount(p, positioning),
            };
        }
    }
}
