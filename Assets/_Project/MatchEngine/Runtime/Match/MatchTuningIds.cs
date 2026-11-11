namespace RoadToPro.MatchEngine.Match
{
    /// <summary>Data/tuning/match.json 튜닝 id (값은 데이터에만).</summary>
    public static class MatchTuningIds
    {
        public const string CoefGoal = "match.stoppage.coef.goal";
        public const string CoefSubstitution = "match.stoppage.coef.substitution";
        public const string CoefInjury = "match.stoppage.coef.injury";
        public const string CoefVar = "match.stoppage.coef.var";
        public const string CoefCard = "match.stoppage.coef.card";
        public const string CoefDissent = "match.stoppage.coef.dissent";

        public const string GoalCelebration = "standin.goalCelebration";
        public const string SubDelay = "standin.subDelay";
        public const string RestartDelay = "standin.restartDelay";
        public const string KickoffDelay = "standin.kickoffDelay";
        public const string HoldTime = "standin.holdTime";
        public const string ShotRange = "standin.shotRange";
        public const string ShotProb = "standin.shotProb";
        public const string ShotSpeed = "standin.shotSpeed";
        public const string PassRange = "standin.passRange";
        public const string PassExtraSpeed = "standin.passExtraSpeed";
        public const string ClaimRadius = "standin.claimRadius";
        public const string ClaimMaxZ = "standin.claimMaxZ";
        public const string ClaimMaxSpeed = "standin.claimMaxSpeed";
        public const string TackleRange = "standin.tackleRange";
        public const string TackleProb = "standin.tackleProb";
        public const string ShapeShiftX = "standin.shapeShiftX";
        public const string ShapeShiftY = "standin.shapeShiftY";
        public const string PenaltyScoreProb = "standin.penaltyScoreProb";
        public const string CarrySpeedRatio = "standin.carrySpeedRatio";
        public const string RestartWaitMax = "standin.restartWaitMax";

        // specs/058 데드볼 이동
        public const string DbGoalCelebration = "deadball.goalCelebration";
        public const string DbArriveRadius = "deadball.arriveRadius";
        public const string DbReturnSpeedMin = "deadball.returnSpeedMin";
        public const string DbReturnSpeedMax = "deadball.returnSpeedMax";
        public const string DbRestartArriveRadius = "deadball.restartArriveRadius";
        public const string DbRestartArriveShare = "deadball.restartArriveShare";
        public const string DbBallOutRelocate = "deadball.ballOutRelocate";
        public const string DbRelocateMaxWait = "deadball.relocateMaxWait";
        public const string DbWalkOffMargin = "deadball.walkOffMargin";
    }
}
