namespace RoadToPro.MatchEngine.Ball
{
    /// <summary>
    /// 공 상태 (specs/004 FR-10, 아키텍처 5). <see cref="Bounce"/>는 순간 상태라 <see cref="BallState"/>에 저장되지 않고
    /// 전이 이벤트(Air → Bounce → Air/Rolling)로만 나타난다 (FR-12).
    /// </summary>
    public enum BallPhase : byte
    {
        Controlled = 0,
        Air = 1,
        Bounce = 2,
        Rolling = 3,
        Rest = 4,
    }

    /// <summary>전이 사유 (FR-11).</summary>
    public enum BallTransitionReason : byte
    {
        None = 0,
        LoftedKick = 1,
        GroundKick = 2,
        GroundContact = 3,
        ReboundHigh = 4,
        ReboundLow = 5,
        SpeedBelowRest = 6,
        TakenControl = 7,
        OwnerChanged = 8,
    }

    /// <summary>허용된 전이 그래프 (아키텍처 5). 테스트·리뷰가 같은 정의를 본다.</summary>
    public static class BallTransitions
    {
        public static bool IsAllowed(BallPhase from, BallPhase to)
        {
            switch (from)
            {
                case BallPhase.Controlled: return to == BallPhase.Air || to == BallPhase.Rolling;
                case BallPhase.Air: return to == BallPhase.Bounce || to == BallPhase.Controlled;
                case BallPhase.Bounce: return to == BallPhase.Air || to == BallPhase.Rolling;
                case BallPhase.Rolling: return to == BallPhase.Rest || to == BallPhase.Controlled;
                case BallPhase.Rest: return to == BallPhase.Controlled;
                default: return false;
            }
        }
    }
}
