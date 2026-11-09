using System;
using RoadToPro.Core.Pitch;
using RoadToPro.Core.Tuning;

namespace RoadToPro.MatchEngine.Tactics
{
    /// <summary>팀 형태 튜닝 묶음 + 포메이션 (MatchTuning.Shape). 해시는 경기 결과의 튜닝 해시에 들어간다.</summary>
    public sealed class ShapeData
    {
        public readonly TuningBundle Tuning;
        public readonly FormationCatalog Formations;

        public ShapeData(TuningBundle tuning, FormationCatalog formations)
        {
            Tuning = tuning ?? throw new ArgumentNullException(nameof(tuning));
            Formations = formations ?? throw new ArgumentNullException(nameof(formations));
        }
    }

    /// <summary>불변 파라미터 (Data/tuning/shape.json). 틱 경로에서 사전 조회가 없도록 필드·평면 배열로 펼친다. 거리 값은 미터, 정규화 값은 표기대로.</summary>
    public sealed class ShapeParams
    {
        static readonly string[] P = { "buildUp", "progression", "finalThird", "defHigh", "defMid", "defLow" };

        public readonly float PitchL, PitchW;
        public readonly float[] LMin = new float[6], LMax = new float[6], WMin = new float[6], WMax = new float[6], LTarget = new float[6], WTarget = new float[6];
        public readonly float[] CxMin = new float[6], CxMax = new float[6], Floor = new float[6], Ceil = new float[6];
        public readonly float LTemplate, WTemplate, BiasMax, BlockLowBound, BlockHighBound, BlockDropBallX;
        public readonly float LateralMin, LateralMax, LambdaPc, WidthScaleMin, WidthScaleMax;
        public readonly float OffsideEpsM, PitchMarginM;
        public readonly int ProjIterations;
        public readonly float ThirdLow, ThirdHigh, HysteresisX;
        public readonly long MinDwellTicks, LooseTicks, AttMinTicks, AttMaxTicks, DefTicks;
        public readonly int UnalignedBehind;
        public readonly float FlightMinSpeed;
        public readonly float BlendMin, BlendMax;
        public readonly float LineK, LineVMax, DropM, StepUpM, SigmaTeamwork, SigmaFatigue, ThreatBufferM, ThreatFloorX, RunnerSpeed, SyncDelayMax, TrapStepProbMin, TrapTolSpread;
        public readonly float SigmaBase, SigmaSlope, FamiliarityMult;
        public readonly int RematchTicks;
        public readonly float SwitchPenalty, VacancyBonus;
        public readonly float WBackPass, WBadTouch, WFacingOwnGoal, WTouchline, WLoftedSlow, WNumericalSup, WGkShort, BadTouchBelow, TouchlineY, SupRadius;
        public readonly float[] Theta = new float[3], Xb = new float[3];
        public readonly float BalanceBackX, ThetaSlope, Count2At, Count3At, HoldBase, HoldIntensity;
        public readonly float PressureNear, PressureMidSpeed, PressureMid, PressureFarSpeed, PressureFar;
        public readonly float ManOffsetM, ManLeashM;
        public readonly float WallDistanceCentral, CentralHalfWidth, RuleDistance, DistanceMargin, ThrowDistance, WallSpacing, GoalSideM;
        public readonly int WallCentral, WallSide, MarkersHybrid, MarkersMan;
        public readonly float GkBaseX, GkBallX, GkBallY, GkMinX, GkMaxX, GkMinY, GkMaxY;

        ShapeParams(TuningBundle t, PitchSpec pitch)
        {
            PitchL = pitch.LengthM;
            PitchW = pitch.WidthM;
            for (int i = 0; i < 6; i++)
            {
                string Id(string n) => "shape." + P[i] + "." + n;
                LMin[i] = t.Get(Id("lMin")); LMax[i] = t.Get(Id("lMax")); WMin[i] = t.Get(Id("wMin")); WMax[i] = t.Get(Id("wMax"));
                LTarget[i] = t.Get(Id("lTarget")); WTarget[i] = t.Get(Id("wTarget"));
                CxMin[i] = t.Get(Id("cxMin")); CxMax[i] = t.Get(Id("cxMax")); Floor[i] = t.Get(Id("floor")); Ceil[i] = t.Get(Id("ceil"));
            }
            LTemplate = t.Get("shape.template.length"); WTemplate = t.Get("shape.template.width");
            BiasMax = t.Get("shape.bias.max"); BlockLowBound = t.Get("shape.block.lowBound"); BlockHighBound = t.Get("shape.block.highBound"); BlockDropBallX = t.Get("shape.block.dropBallX");
            LateralMin = t.Get("shape.lateral.min"); LateralMax = t.Get("shape.lateral.max"); LambdaPc = t.Get("shape.lambdaPc");
            WidthScaleMin = t.Get("shape.width.scaleMin"); WidthScaleMax = t.Get("shape.width.scaleMax");
            OffsideEpsM = t.Get("shape.offsideEps"); PitchMarginM = t.Get("shape.pitchMargin"); ProjIterations = (int)t.Get("shape.projIterations");
            ThirdLow = t.Get("phase.thirdLow"); ThirdHigh = t.Get("phase.thirdHigh"); HysteresisX = t.Get("phase.hysteresisX");
            MinDwellTicks = Ticks(t.Get("phase.minDwell")); LooseTicks = Ticks(t.Get("phase.looseToTransition"));
            AttMinTicks = Ticks(t.Get("phase.attTransitionMin")); AttMaxTicks = Ticks(t.Get("phase.attTransitionMax")); DefTicks = Ticks(t.Get("phase.defTransition"));
            UnalignedBehind = (int)t.Get("phase.unalignedBehind"); FlightMinSpeed = t.Get("phase.flightMinSpeed");
            BlendMin = t.Get("blend.min"); BlendMax = t.Get("blend.max");
            LineK = t.Get("line.k"); LineVMax = t.Get("line.vMax"); DropM = t.Get("line.dropM"); StepUpM = t.Get("line.stepUpM");
            SigmaTeamwork = t.Get("line.sigmaTeamwork"); SigmaFatigue = t.Get("line.sigmaFatigue"); ThreatBufferM = t.Get("line.threatBuffer"); ThreatFloorX = t.Get("line.threatFloorX"); RunnerSpeed = t.Get("line.runnerSpeed");
            SyncDelayMax = t.Get("line.syncDelayMax"); TrapStepProbMin = t.Get("line.trapStepProbMin"); TrapTolSpread = t.Get("line.trapTolSpread");
            SigmaBase = t.Get("error.sigmaBase"); SigmaSlope = t.Get("error.sigmaSlope"); FamiliarityMult = t.Get("error.familiarityMult");
            RematchTicks = (int)Ticks(t.Get("rematch.period")); SwitchPenalty = t.Get("rematch.switchPenalty"); VacancyBonus = t.Get("rematch.vacancyBonus");
            WBackPass = t.Get("press.w.backPass"); WBadTouch = t.Get("press.w.badTouch"); WFacingOwnGoal = t.Get("press.w.facingOwnGoal"); WTouchline = t.Get("press.w.touchline");
            WLoftedSlow = t.Get("press.w.loftedSlow"); WNumericalSup = t.Get("press.w.numericalSup"); WGkShort = t.Get("press.w.gkShort");
            BadTouchBelow = t.Get("press.badTouchBelow"); TouchlineY = t.Get("press.touchlineY"); SupRadius = t.Get("press.supRadius");
            Theta[0] = t.Get("press.theta.low"); Theta[1] = t.Get("press.theta.mid"); Theta[2] = t.Get("press.theta.high");
            Xb[0] = t.Get("press.xb.low"); Xb[1] = t.Get("press.xb.mid"); Xb[2] = t.Get("press.xb.high");
            BalanceBackX = t.Get("press.balanceBackX"); ThetaSlope = t.Get("press.thetaSlope"); Count2At = t.Get("press.count2At"); Count3At = t.Get("press.count3At"); HoldBase = t.Get("press.holdBase"); HoldIntensity = t.Get("press.holdIntensity");
            PressureNear = t.Get("pressure.nearM"); PressureMidSpeed = t.Get("pressure.midSpeed"); PressureMid = t.Get("pressure.midM"); PressureFarSpeed = t.Get("pressure.farSpeed"); PressureFar = t.Get("pressure.farM");
            ManOffsetM = t.Get("man.offsetM"); ManLeashM = t.Get("man.leashM");
            WallDistanceCentral = t.Get("setpiece.wallDistanceCentral"); CentralHalfWidth = t.Get("setpiece.centralHalfWidth"); WallCentral = (int)t.Get("setpiece.wallCentral"); WallSide = (int)t.Get("setpiece.wallSide");
            RuleDistance = t.Get("setpiece.ruleDistance"); DistanceMargin = t.Get("setpiece.distanceMargin"); ThrowDistance = t.Get("setpiece.throwDistance"); WallSpacing = t.Get("setpiece.wallSpacing");
            MarkersHybrid = (int)t.Get("setpiece.markersHybrid"); MarkersMan = (int)t.Get("setpiece.markersMan"); GoalSideM = t.Get("setpiece.goalSideM");
            GkBaseX = t.Get("gk.baseX"); GkBallX = t.Get("gk.ballX"); GkBallY = t.Get("gk.ballY"); GkMinX = t.Get("gk.minX"); GkMaxX = t.Get("gk.maxX"); GkMinY = t.Get("gk.minY"); GkMaxY = t.Get("gk.maxY");
        }

        public static ShapeParams Create(TuningBundle t, PitchSpec pitch) => new ShapeParams(t ?? throw new ArgumentNullException(nameof(t)), pitch);

        static long Ticks(float seconds) => (long)Math.Round(seconds * Core.Time.SimClock.TicksPerSecond, MidpointRounding.AwayFromZero);
    }
}
