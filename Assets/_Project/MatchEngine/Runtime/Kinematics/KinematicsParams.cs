using RoadToPro.Core.Math;
using RoadToPro.Core.Pitch;
using RoadToPro.Core.Time;
using RoadToPro.Core.Tuning;
using Ids = RoadToPro.MatchEngine.Kinematics.KinematicsTuningIds;

namespace RoadToPro.MatchEngine.Kinematics
{
    /// <summary>
    /// 운동학 공통 파라미터 (Data/tuning/kinematics.json). 생성 시 한 번 읽고, 서브스텝에서는 필드만 읽는다. 불변.
    /// </summary>
    public sealed class KinematicsParams
    {
        public readonly PitchSpec Pitch;
        public readonly float Dt;

        // 매핑 (FR-01~06)
        public readonly float VmaxBase, VmaxRange, VmaxExp;
        public readonly float AccelBase, AccelRange;
        public readonly float LatBase, LatRange, DecelBase, DecelRange;
        public readonly float PivotBaseDeg, PivotRangeDeg;
        public readonly float PivotEnter, PivotExit;
        public readonly float LossWorst, LossBest, CapWorst, CapBest;
        public readonly float KfWorst, KfBest, RecWorst, RecBest;
        public readonly float PerceptionBase, PerceptionRange, MotorDelay;

        // 조향 (FR-20~27)
        public readonly float ArriveDecelFactor, ArriveTolerance, ArriveStopSpeed;
        public readonly float PursueMaxT, InterceptMaxT, ControlRadius, TtiTurnCost;
        public readonly float SepRadius, BodyRadius, SepPushSpeed;
        public readonly float MarkOffset;
        public readonly float JockeyDistance, JockeySpeedRatio, JockeyMinApproach, JockeyBackpedalMax, JockeySideOffset;
        public readonly float FollowGain;
        public readonly float WSeparation, WBehavior, WFormation;

        // 피로 (FR-30~36)
        public readonly float CritRatio, RecoverRate, AcuteDrain, ChronicDistRate, ChronicHirRate;
        public readonly float VmaxAcute, VmaxChronic, AccAcute, AccChronic;
        public readonly float CurveAcute, CurveChronic, CurveMin, ReactionAdd;

        // 기록 (FR-50)
        public readonly float HirSpeed, SprintSpeed, AccThreshold, AccMinDuration;
        public readonly int AccMinSubsteps;

        // 경계
        public readonly float BoundsMargin;
        public readonly float MinX, MaxX, MinY, MaxY;

        public static KinematicsParams Create(TuningBundle t, PitchSpec pitch) => new KinematicsParams(t, pitch);

        KinematicsParams(TuningBundle t, PitchSpec pitch)
        {
            Pitch = pitch;
            Dt = SimClock.PhysicsDt;

            VmaxBase = t.Get(Ids.VmaxBase); VmaxRange = t.Get(Ids.VmaxRange); VmaxExp = t.Get(Ids.VmaxExp);
            AccelBase = t.Get(Ids.AccelBase); AccelRange = t.Get(Ids.AccelRange);
            LatBase = t.Get(Ids.LatBase); LatRange = t.Get(Ids.LatRange);
            DecelBase = t.Get(Ids.DecelBase); DecelRange = t.Get(Ids.DecelRange);
            PivotBaseDeg = t.Get(Ids.PivotBase); PivotRangeDeg = t.Get(Ids.PivotRange);
            PivotEnter = t.Get(Ids.PivotEnter); PivotExit = t.Get(Ids.PivotExit);
            LossWorst = t.Get(Ids.BalanceLossWorst); LossBest = t.Get(Ids.BalanceLossBest);
            CapWorst = t.Get(Ids.BalanceCapWorst); CapBest = t.Get(Ids.BalanceCapBest);
            KfWorst = t.Get(Ids.StaminaKfWorst); KfBest = t.Get(Ids.StaminaKfBest);
            RecWorst = t.Get(Ids.StaminaRecWorst); RecBest = t.Get(Ids.StaminaRecBest);
            PerceptionBase = t.Get(Ids.PerceptionBase); PerceptionRange = t.Get(Ids.PerceptionRange);
            MotorDelay = t.Get(Ids.MotorDelay);

            ArriveDecelFactor = t.Get(Ids.ArriveDecelFactor); ArriveTolerance = t.Get(Ids.ArriveTolerance); ArriveStopSpeed = t.Get(Ids.ArriveStopSpeed);
            PursueMaxT = t.Get(Ids.PursueMaxT); InterceptMaxT = t.Get(Ids.InterceptMaxT); ControlRadius = t.Get(Ids.InterceptControlRadius);
            TtiTurnCost = t.Get(Ids.TtiTurnCost);
            SepRadius = t.Get(Ids.SepRadius); BodyRadius = t.Get(Ids.SepBodyRadius); SepPushSpeed = t.Get(Ids.SepPushSpeed);
            MarkOffset = t.Get(Ids.MarkOffset);
            JockeyDistance = t.Get(Ids.JockeyDistance); JockeySpeedRatio = t.Get(Ids.JockeySpeedRatio);
            JockeyMinApproach = t.Get(Ids.JockeyMinApproach); JockeyBackpedalMax = t.Get(Ids.JockeyBackpedalMax);
            JockeySideOffset = t.Get(Ids.JockeySideOffset);
            FollowGain = t.Get(Ids.FollowGain);
            WSeparation = t.Get(Ids.CombineWSeparation); WBehavior = t.Get(Ids.CombineWBehavior); WFormation = t.Get(Ids.CombineWFormation);

            CritRatio = t.Get(Ids.FatigueCritRatio); RecoverRate = t.Get(Ids.FatigueRecoverRate);
            AcuteDrain = t.Get(Ids.FatigueAcuteDrain);
            ChronicDistRate = t.Get(Ids.FatigueChronicDistRate); ChronicHirRate = t.Get(Ids.FatigueChronicHirRate);
            VmaxAcute = t.Get(Ids.FatigueVmaxAcute); VmaxChronic = t.Get(Ids.FatigueVmaxChronic);
            AccAcute = t.Get(Ids.FatigueAccAcute); AccChronic = t.Get(Ids.FatigueAccChronic);
            CurveAcute = t.Get(Ids.FatigueCurveAcute); CurveChronic = t.Get(Ids.FatigueCurveChronic); CurveMin = t.Get(Ids.FatigueCurveMin);
            ReactionAdd = t.Get(Ids.FatigueReactionAdd);

            HirSpeed = t.Get(Ids.StatsHirSpeed); SprintSpeed = t.Get(Ids.StatsSprintSpeed);
            AccThreshold = t.Get(Ids.StatsAccThreshold); AccMinDuration = t.Get(Ids.StatsAccMinDuration);
            AccMinSubsteps = (int)DMath.Floor(AccMinDuration / Dt + 0.5f);

            BoundsMargin = t.Get(Ids.BoundsMargin);
            MinX = -BoundsMargin; MaxX = pitch.LengthM + BoundsMargin;
            MinY = -BoundsMargin; MaxY = pitch.WidthM + BoundsMargin;

            // 조합 검증 (경계 상황: 매핑 결과 Vmax ≤ 0·τ ≤ 0 불가, 피로 효과가 속도를 0 이하로 만들 수 없음)
            if (!(PivotExit > PivotEnter)) throw new TuningLoadException(Ids.PivotExit, "피벗 해제 속도는 진입 속도보다 커야 한다 (히스테리시스)");
            if (!(VmaxAcute + VmaxChronic < 0.9f)) throw new TuningLoadException(Ids.FatigueVmaxAcute, "피로 속도 저하 합이 0.9 이상");
            if (!(AccAcute + AccChronic < 0.9f)) throw new TuningLoadException(Ids.FatigueAccAcute, "피로 가속 저하 합이 0.9 이상");
            if (!(PerceptionBase - PerceptionRange > 0f)) throw new TuningLoadException(Ids.PerceptionRange, "인지지연이 0 이하가 될 수 있다");
            if (!(SepRadius > 2f * BodyRadius)) throw new TuningLoadException(Ids.SepRadius, "Separation 반경이 몸 지름 이하");
            if (!(JockeySpeedRatio > 0f)) throw new TuningLoadException(Ids.JockeySpeedRatio, "저킹 속도 비율 ≤ 0");
        }
    }
}
