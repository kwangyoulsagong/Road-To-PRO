using RoadToPro.Core.Math;

namespace RoadToPro.MatchEngine.Kinematics
{
    /// <summary>
    /// 선수별 운동 파라미터 (specs/005 FR-01~07). 능력치 → 물리 매핑을 생성 시 한 번만 계산한다. 불변 값 타입.
    /// </summary>
    public readonly struct PlayerMotionProfile
    {
        /// <summary>최고속도 Vmax (m/s), FR-01.</summary>
        public readonly float MaxSpeed;
        /// <summary>초기 가속 a0 (m/s²), FR-02.</summary>
        public readonly float MaxAccel;
        /// <summary>τ = Vmax/a0 (s).</summary>
        public readonly float Tau;
        /// <summary>최대 횡가속 (m/s²), FR-03.</summary>
        public readonly float MaxLateralAccel;
        /// <summary>감속 상한 (m/s², 양수), FR-03·13.</summary>
        public readonly float MaxDecel;
        /// <summary>피벗 회전속도 상한 (rad/s), FR-03·12.</summary>
        public readonly float PivotTurnRate;
        /// <summary>접촉 시 속도 손실 비율, FR-04.</summary>
        public readonly float ContactLoss;
        /// <summary>접촉 중 a_cap 초과 허용 배율, FR-04.</summary>
        public readonly float ContactCapScale;
        /// <summary>피로 누적률 배율 k_f, FR-05.</summary>
        public readonly float FatigueRateScale;
        /// <summary>W′ 회복률 배율, FR-05.</summary>
        public readonly float RecoveryScale;
        /// <summary>인지지연 (s), FR-06. 예측력·집중력 평균 (plan [제안] ②).</summary>
        public readonly float PerceptionDelay;
        /// <summary>운동 개시 지연 (s), FR-06 (plan [제안] 0.075 고정).</summary>
        public readonly float MotorDelay;
        /// <summary>서브스텝당 피벗 회전 상한 각의 cos·sin (미리 계산, 서브스텝 삼각함수 제거).</summary>
        public readonly float PivotStepCos, PivotStepSin;

        /// <summary>인지 + 운동 개시 지연 (피로 가산 전).</summary>
        public float ReactionDelay => PerceptionDelay + MotorDelay;

        PlayerMotionProfile(float vmax, float a0, float lat, float dec, float pivot, float loss, float cap, float kf, float rec, float perc, float motor, float dt)
        {
            PivotStepCos = DMath.Cos(pivot * dt);
            PivotStepSin = DMath.Sin(pivot * dt);
            MaxSpeed = vmax; MaxAccel = a0; Tau = vmax / a0;
            MaxLateralAccel = lat; MaxDecel = dec; PivotTurnRate = pivot;
            ContactLoss = loss; ContactCapScale = cap; FatigueRateScale = kf; RecoveryScale = rec;
            PerceptionDelay = perc; MotorDelay = motor;
        }

        public static PlayerMotionProfile Create(in KinematicAttributes a, KinematicsParams p)
        {
            var sPace = KinematicAttributes.Normalized(a.Pace);
            var sAcc = KinematicAttributes.Normalized(a.Acceleration);
            var sAgi = KinematicAttributes.Normalized(a.Agility);
            var sBal = KinematicAttributes.Normalized(a.Balance);
            var sSta = KinematicAttributes.Normalized(a.Stamina);
            var sCog = 0.5f * (KinematicAttributes.Normalized(a.Anticipation) + KinematicAttributes.Normalized(a.Concentration));

            var vmax = p.VmaxBase + p.VmaxRange * (sPace > 0f ? DMath.Pow(sPace, p.VmaxExp) : 0f);
            var a0 = p.AccelBase + p.AccelRange * sAcc;
            var lat = p.LatBase + p.LatRange * sAgi;
            var dec = p.DecelBase + p.DecelRange * sAgi;
            var pivot = (p.PivotBaseDeg + p.PivotRangeDeg * sAgi) * (DMath.Pi / 180f);
            var loss = Lerp(p.LossWorst, p.LossBest, sBal);
            var cap = Lerp(p.CapWorst, p.CapBest, sBal);
            var kf = Lerp(p.KfWorst, p.KfBest, sSta);
            var rec = Lerp(p.RecWorst, p.RecBest, sSta);
            var perc = p.PerceptionBase - p.PerceptionRange * sCog;
            return new PlayerMotionProfile(vmax, a0, lat, dec, pivot, loss, cap, kf, rec, perc, p.MotorDelay, p.Dt);
        }

        static float Lerp(float a, float b, float t) => a + (b - a) * t;
    }
}
