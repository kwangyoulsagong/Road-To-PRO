namespace RoadToPro.MatchEngine.PitchControl
{
    /// <summary>지점 질의 결과 (FR-08): 공격 + 수비 + 미통제 = 1. Leader = 컨트롤이 가장 큰 선수(동률은 낮은 슬롯, 없으면 −1).</summary>
    public struct PointControl
    {
        public float Att, Def, None;
        public int Leader;
        /// <summary>적분이 끝난 시각 (공 도착 이후 경과, s).</summary>
        public float Elapsed;
    }

    /// <summary>궤적 질의 결과 (FR-12): 의도한 수신자 + 다른 동료 + 상대 + 아웃(아무도 못 잡음) = 1.</summary>
    public struct TrajectoryControl
    {
        public float Receiver, Teammate, Opponent, Out;
        /// <summary>궤적 끝: 0 = 상한 시간, 1 = 라인 밖, 2 = 멈춤 (<see cref="TrajectorySamples"/>), 3 = 수렴 (<see cref="PitchControlQuery.EndConverged"/>).</summary>
        public byte End;
    }
}
