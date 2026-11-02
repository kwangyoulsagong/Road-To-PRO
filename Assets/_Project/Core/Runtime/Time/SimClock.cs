namespace RoadToPro.Core.Time
{
    /// <summary>
    /// 시뮬 시계 상수 (specs/001 FR-40, 아키텍처 § 3, research 01 A-8). 시간의 기준은 정수 결정 틱이다.
    /// 실수 시간을 누적하지 않는다 — 초가 필요하면 틱에서 계산한다.
    /// </summary>
    public static class SimClock
    {
        public const int DecisionHz = 10;
        public const int SubstepsPerDecision = 10;
        public const int PhysicsHz = DecisionHz * SubstepsPerDecision;
        public const float DecisionDt = 0.1f;
        public const float PhysicsDt = 0.01f;

        public const long TicksPerSecond = DecisionHz;
        public const long TicksPerMinute = 60 * TicksPerSecond;

        /// <summary>체크포인트 간격: 경기 시각 10초 (research 07 §2.3).</summary>
        public const long CheckpointIntervalTicks = 10 * TicksPerSecond;

        /// <summary>최장 경기 [제안 승인] 150분.</summary>
        public const long MaxMatchTicks = 150 * TicksPerMinute;

        /// <summary>결정 틱 → 경과 초 (틱에서 직접 계산).</summary>
        public static float SecondsAt(long decisionTick) => decisionTick / (float)TicksPerSecond;

        /// <summary>전체 물리 서브스텝 번호.</summary>
        public static long PhysicsStepIndex(long decisionTick, int substep) => decisionTick * SubstepsPerDecision + substep;
    }
}
