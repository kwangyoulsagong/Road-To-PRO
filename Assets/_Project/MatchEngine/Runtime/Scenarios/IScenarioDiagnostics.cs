namespace RoadToPro.MatchEngine.Scenarios
{
    /// <summary>실행 중 시나리오 진단 (FR-52, 040 FR-30). 읽기 전용이고 null이면 호출 0 — 시뮬 결과와 RNG 소비를 바꾸지 않는다.</summary>
    public interface IScenarioDiagnostics
    {
        /// <summary>실행 중인 시나리오를 틱마다 알린다. slots·steps는 재사용 배열이라 호출 안에서만 유효하다.</summary>
        void OnRunning(int team, long tick, string id, bool forced, int roleCount, int[] slots, int[] steps);
        void OnBranch(int team, long tick, string id, int role, int step, StepOutcome outcome);
        void OnEnded(int team, long tick, string id, bool forced, ScenarioEndReason reason);
    }

    /// <summary>런타임 구간 시간 측정 훅 (AC-16). null이면 호출 0. 구현은 Verification(시계 API는 코어 밖).</summary>
    public interface IScenarioTimer
    {
        void Begin();
        void End();
    }
}
