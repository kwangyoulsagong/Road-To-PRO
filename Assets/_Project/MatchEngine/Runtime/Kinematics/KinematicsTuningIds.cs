namespace RoadToPro.MatchEngine.Kinematics
{
    /// <summary>Data/tuning/kinematics.json 튜닝 id 상수 (값은 데이터에만, specs/005 plan).</summary>
    public static class KinematicsTuningIds
    {
        public const string VmaxBase = "kin.vmax.base";
        public const string VmaxRange = "kin.vmax.range";
        public const string VmaxExp = "kin.vmax.exp";
        public const string AccelBase = "kin.accel.base";
        public const string AccelRange = "kin.accel.range";
        public const string LatBase = "kin.lat.base";
        public const string LatRange = "kin.lat.range";
        public const string DecelBase = "kin.decel.base";
        public const string DecelRange = "kin.decel.range";
        public const string PivotBase = "kin.pivot.base";
        public const string PivotRange = "kin.pivot.range";
        public const string PivotEnter = "kin.pivot.enter";
        public const string PivotExit = "kin.pivot.exit";
        public const string BalanceLossWorst = "kin.balance.lossWorst";
        public const string BalanceLossBest = "kin.balance.lossBest";
        public const string BalanceCapWorst = "kin.balance.capWorst";
        public const string BalanceCapBest = "kin.balance.capBest";
        public const string StaminaKfWorst = "kin.stamina.kfWorst";
        public const string StaminaKfBest = "kin.stamina.kfBest";
        public const string StaminaRecWorst = "kin.stamina.recWorst";
        public const string StaminaRecBest = "kin.stamina.recBest";
        public const string PerceptionBase = "kin.perception.base";
        public const string PerceptionRange = "kin.perception.range";
        public const string MotorDelay = "kin.motorDelay";
        public const string ArriveDecelFactor = "kin.arrive.decelFactor";
        public const string ArriveTolerance = "kin.arrive.tolerance";
        public const string ArriveStopSpeed = "kin.arrive.stopSpeed";
        public const string PursueMaxT = "kin.pursue.maxT";
        public const string InterceptMaxT = "kin.intercept.maxT";
        public const string InterceptControlRadius = "kin.intercept.controlRadius";
        public const string TtiTurnCost = "kin.tti.turnCost";
        public const string SepRadius = "kin.sep.radius";
        public const string SepBodyRadius = "kin.sep.bodyRadius";
        public const string SepPushSpeed = "kin.sep.pushSpeed";
        public const string MarkOffset = "kin.mark.offset";
        public const string JockeyDistance = "kin.jockey.distance";
        public const string JockeySpeedRatio = "kin.jockey.speedRatio";
        public const string JockeyMinApproach = "kin.jockey.minApproach";
        public const string JockeyBackpedalMax = "kin.jockey.backpedalMax";
        public const string JockeySideOffset = "kin.jockey.sideOffset";
        public const string FollowGain = "kin.follow.gain";
        public const string CombineWSeparation = "kin.combine.wSeparation";
        public const string CombineWBehavior = "kin.combine.wBehavior";
        public const string CombineWFormation = "kin.combine.wFormation";
        public const string FatigueCritRatio = "kin.fatigue.critRatio";
        public const string FatigueRecoverRate = "kin.fatigue.recoverRate";
        public const string FatigueAcuteDrain = "kin.fatigue.acuteDrain";
        public const string FatigueChronicDistRate = "kin.fatigue.chronicDistRate";
        public const string FatigueChronicHirRate = "kin.fatigue.chronicHirRate";
        public const string FatigueVmaxAcute = "kin.fatigue.vmaxAcute";
        public const string FatigueVmaxChronic = "kin.fatigue.vmaxChronic";
        public const string FatigueAccAcute = "kin.fatigue.accAcute";
        public const string FatigueAccChronic = "kin.fatigue.accChronic";
        public const string FatigueCurveAcute = "kin.fatigue.curveAcute";
        public const string FatigueCurveChronic = "kin.fatigue.curveChronic";
        public const string FatigueCurveMin = "kin.fatigue.curveMin";
        public const string FatigueReactionAdd = "kin.fatigue.reactionAdd";
        public const string StatsHirSpeed = "kin.stats.hirSpeed";
        public const string StatsSprintSpeed = "kin.stats.sprintSpeed";
        public const string StatsAccThreshold = "kin.stats.accThreshold";
        public const string StatsAccMinDuration = "kin.stats.accMinDuration";
        public const string BoundsMargin = "kin.bounds.margin";
    }
}
