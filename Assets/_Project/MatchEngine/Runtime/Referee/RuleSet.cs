using System;

namespace RoadToPro.MatchEngine.Referee
{
    /// <summary>시즌 규칙 연도 (specs/012 FR-25·63, 플랜 P3). 값은 세이브·설정에 들어가므로 번호를 바꾸지 않는다.</summary>
    public enum RuleSeason : byte
    {
        Season2025_26 = 0,
        Season2026_27 = 1,
    }

    /// <summary>
    /// 시즌 규칙 세트 (specs/012 FR-25·63·64, 플랜 P3). 기본 = 2026/27 (2025/26 GK 8초 + 어드밴티지 후 골 DOGSO 경고 없음).
    /// 경기 설정 입력 — 경기 중 바뀌지 않는다.
    /// </summary>
    public sealed class RuleSet
    {
        public RuleSeason Season { get; }
        /// <summary>FR-63: GK 손 보유 초과 → 상대 코너킥 (2025/26부터). 꺼져 있으면 이전 규칙(간접 FK)으로 판정.</summary>
        public bool KeeperHoldCorner { get; }
        /// <summary>FR-25: 어드밴티지 후 골이면 DOGSO 경고 없음 (2026/27).</summary>
        public bool NoDogsoCautionAfterAdvantageGoal { get; }
        /// <summary>FR-64: GK 백패스 규칙 (플랜 P12 채택).</summary>
        public bool BackPassRule { get; }

        RuleSet(RuleSeason season, bool keeperCorner, bool noDogsoCaution, bool backPass)
        {
            Season = season;
            KeeperHoldCorner = keeperCorner;
            NoDogsoCautionAfterAdvantageGoal = noDogsoCaution;
            BackPassRule = backPass;
        }

        public static readonly RuleSet Season2025_26 = new RuleSet(RuleSeason.Season2025_26, true, false, true);
        public static readonly RuleSet Season2026_27 = new RuleSet(RuleSeason.Season2026_27, true, true, true);

        /// <summary>플랜 P3 기본 규칙 세트.</summary>
        public static RuleSet Default => Season2026_27;

        public static RuleSet Of(RuleSeason season) => season switch
        {
            RuleSeason.Season2025_26 => Season2025_26,
            RuleSeason.Season2026_27 => Season2026_27,
            _ => throw new ArgumentOutOfRangeException(nameof(season)),
        };
    }
}
