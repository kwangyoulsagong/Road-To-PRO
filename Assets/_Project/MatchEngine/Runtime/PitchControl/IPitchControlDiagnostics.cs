namespace RoadToPro.MatchEngine.PitchControl
{
    /// <summary>
    /// 007 FR-27 진단 싱크 (040 '피치 컨트롤 · xT' 레이어 등). null이면 호출 0. 구현은 결과·RNG를 바꾸지 않는다(시뮬 상태를 읽거나 쓰지 않는다).
    /// </summary>
    public interface IPitchControlDiagnostics
    {
        /// <summary>패스 후보 궤적 재평가: 로지스틱 xPass(수신 지점 컨트롤 포함) → 궤적 질의 4종 → 판단 P (item = 072 OnBallItem 값).</summary>
        void OnPassRefined(long tick, int passer, int receiver, byte item, float logisticXPass, in TrajectoryControl trajectory, float judged);
    }
}
