using System;

namespace RoadToPro.MatchEngine.Match
{
    /// <summary>
    /// 대회 규칙 (specs/006 FR-10·12). 값은 리그·대회 규칙 데이터(020 FR-15)에서 받는다.
    /// <see cref="Default"/>는 데이터가 없을 때의 기본값 (2026-11-03 승인): 5명 / 경기 중 3창(하프타임 제외), 연장 시 +1명·+1창, 뇌진탕 1회 별도.
    /// </summary>
    public sealed class CompetitionRules
    {
        public bool ExtraTime { get; set; }
        public bool Shootout { get; set; }
        public int MaxSubstitutions { get; set; } = 5;
        public int MaxWindows { get; set; } = 3;
        public int BenchSize { get; set; } = 12;
        public int ExtraTimeSubstitutions { get; set; } = 1;
        public int ExtraTimeWindows { get; set; } = 1;
        public int ConcussionSubstitutions { get; set; } = 1;
        /// <summary>승부차기 기본 키커 수 (FR-15).</summary>
        public int ShootoutRounds { get; set; } = 5;

        public static CompetitionRules Default() => new CompetitionRules();

        internal object MemberwiseCopy() => MemberwiseClone();

        public static CompetitionRules Cup() => new CompetitionRules { ExtraTime = true, Shootout = true };

        public void Validate()
        {
            if (MaxSubstitutions < 0 || MaxWindows < 0 || BenchSize < 0 || ExtraTimeSubstitutions < 0 || ExtraTimeWindows < 0 || ConcussionSubstitutions < 0)
                throw new MatchSetupException("대회 규칙 값은 0 이상");
            if (ShootoutRounds < 1) throw new MatchSetupException("승부차기 라운드는 1 이상");
        }
    }

    /// <summary>경기 설정 오류 (FR-10 입력 거부: 선발 11명 아님, 중복 선수 등).</summary>
    public sealed class MatchSetupException : Exception
    {
        public MatchSetupException(string message) : base(message) { }
    }
}
