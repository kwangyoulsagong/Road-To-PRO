namespace RoadToPro.MatchEngine.UtilityAI
{
    /// <summary>
    /// 결정 진단·텔레메트리 수신 (specs/010 FR-60~62·99·104). 결정 단계가 결정마다·틱마다 부른다. 구현은 읽기만 하고 시뮬 상태·RNG를 바꾸지 않는다(AC-16·19).
    /// 꺼져 있으면(null) 호출 비용·할당 0.
    /// </summary>
    public interface IDecisionDiagnostics
    {
        /// <summary>결정 1회. decider가 null이 아니면 이번 결정의 후보·점수 분해를 읽을 수 있다(오프볼).</summary>
        void OnDecision(long tick, int slot, in PlayerDecisionState state, OffBallDecider decider, DecisionWorld world);
        /// <summary>틱 끝 (결정 대상 처리 뒤).</summary>
        void OnTick(long tick, DecisionWorld world, int spaceQueries);
    }
}
