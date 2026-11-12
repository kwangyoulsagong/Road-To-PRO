using RoadToPro.MatchEngine.Match.Events;

namespace RoadToPro.MatchEngine.Actions
{
    /// <summary>
    /// 액션 평가 진단 싱크 (specs/008 FR-63, 040 FR-30 레이어 '액션 평가'). null이면 호출 0 — 시뮬 결과·RNG 소비를 바꾸지 않는다.
    /// 070·071 평가값도 같은 싱크로 낸다. 구현은 콜백 안에서 ActionSystem(평가·압박 등)을 부르지 않는다 — 틱 월드 갱신 시점이 바뀌어 결과가 달라질 수 있다.
    /// </summary>
    public interface IActionDiagnostics
    {
        /// <summary>패스 후보 하나 (속도 후보 중 최고). score = xPass × ΔxT 등 호출자 점수.</summary>
        void OnPassCandidate(long tick, int passer, int receiver, in PassOption option, float deltaXt, float pressure, float score);
        /// <summary>선택된 패스.</summary>
        void OnPassChosen(long tick, int passer, int receiver, in PassOption option);
        /// <summary>접촉 실행 요약 (킥·수신·인터셉트). value = 종류별 값(xPass·이격 등).</summary>
        void OnContact(long tick, int substep, int slot, SimEventKind kind, float value);
    }
}
