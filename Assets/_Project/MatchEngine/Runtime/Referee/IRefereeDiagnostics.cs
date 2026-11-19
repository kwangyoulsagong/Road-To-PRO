using RoadToPro.Core.Math;

namespace RoadToPro.MatchEngine.Referee
{
    /// <summary>
    /// 심판 진단 출력 (specs/012 FR-97 → 040 FR-30 레이어 '심판 · 오프사이드 · 어드밴티지'). 널이면 호출·할당 0.
    /// 구현은 시뮬 상태·RNG를 바꾸지 않는다 (AC-24).
    /// </summary>
    public interface IRefereeDiagnostics
    {
        /// <summary>오프사이드 스냅샷: 공격 팀, 기준선 x, 판정 신체 부위(몸 앞쪽 끝) x 22개(후보 아니면 NaN).</summary>
        void OnOffsideSnapshot(long tick, int attackTeam, float lineX, System.ReadOnlySpan<float> frontX);
        /// <summary>어드밴티지 보류 상태 (매 틱, 보류가 있을 때만): 반칙당한 팀, 남은 시간(s).</summary>
        void OnAdvantage(long tick, int victimTeam, float remainingSeconds);
        /// <summary>심판 3명 목표·위치와 마지막 판정 시야 품질.</summary>
        void OnOfficials(long tick, Vec2 referee, Vec2 assistantHigh, Vec2 assistantLow, float lastSight);
    }
}
