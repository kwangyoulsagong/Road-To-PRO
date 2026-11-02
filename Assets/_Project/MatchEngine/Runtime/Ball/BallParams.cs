using System;
using RoadToPro.Core.Math;
using RoadToPro.Core.Pitch;
using RoadToPro.Core.Time;
using RoadToPro.Core.Tuning;

namespace RoadToPro.MatchEngine.Ball
{
    /// <summary>
    /// 공 물리 파라미터 (specs/004 FR-01~07). 튜닝 묶음 + 공 종류 + 표면 조건 + 피치로 한 번 만들고 경기 동안 바꾸지 않는다.
    /// 서브스텝에서 튜닝 묶음을 조회하지 않도록 미리 계산한 값(β, 스핀 감쇠 배율, 표면별 e·a·b)을 가진다.
    /// </summary>
    public sealed class BallParams
    {
        public readonly BallType Type;
        public readonly SurfaceCondition Surface;
        public readonly PitchSpec Pitch;
        public readonly float Dt;

        public readonly float Mass;
        public readonly float Radius;
        public readonly float Gravity;
        /// <summary>β = ρA/(2m) (FR-01).</summary>
        public readonly float Beta;

        public readonly float CdLow;
        public readonly float CdHigh;
        public readonly float CdCritSpeed;
        public readonly float CdTransitionW;

        public readonly float ClMax;
        public readonly float ClSpScale;
        public readonly float MagnusMinSpeed;
        public readonly float MagnusFadeWidth;

        public readonly float SpinDecay;
        /// <summary>서브스텝당 스핀 배율 exp(−k·dt) (FR-04).</summary>
        public readonly float SpinDecayFactor;

        public readonly float Restitution;
        public readonly float Mu;
        public readonly float GripKeep;
        public readonly float GripSpin;
        public readonly float ToRollVz;

        public readonly float RollA;
        public readonly float RollB;
        public readonly float ToRestSpeed;

        public readonly float LimitSpeed;
        public readonly float LimitSpin;

        public readonly float PostRadius;
        public readonly float PostRestitution;
        public readonly float NetDepth;
        public readonly float NetDamping;

        /// <summary>골 폭 7.32 m · 높이 2.44 m (기획서 5A.1, FR-40). 규격이라 튜닝이 아니다.</summary>
        public const float GoalWidth = 7.32f;
        public const float GoalHeight = 2.44f;

        BallParams(TuningBundle t, BallType type, SurfaceCondition surface, PitchSpec pitch)
        {
            Type = type;
            Surface = surface;
            Pitch = pitch;
            Dt = SimClock.PhysicsDt;

            Mass = t.Get(BallTuningIds.Mass);
            Radius = t.Get(BallTuningIds.Radius);
            Gravity = t.Get(BallTuningIds.Gravity);
            var rho = t.Get(BallTuningIds.AirDensity);
            Beta = rho * DMath.Pi * Radius * Radius / (2f * Mass);

            if (type == BallType.Panel32)
            {
                CdLow = t.Get(BallTuningIds.Panel32CdLow);
                CdHigh = t.Get(BallTuningIds.Panel32CdHigh);
                CdCritSpeed = t.Get(BallTuningIds.Panel32VCrit);
                CdTransitionW = t.Get(BallTuningIds.Panel32W);
            }
            else
            {
                CdLow = t.Get(BallTuningIds.ModernCdLow);
                CdHigh = t.Get(BallTuningIds.ModernCdHigh);
                CdCritSpeed = t.Get(BallTuningIds.ModernVCrit);
                CdTransitionW = t.Get(BallTuningIds.ModernW);
            }

            ClMax = t.Get(BallTuningIds.ClMax);
            ClSpScale = t.Get(BallTuningIds.SpScale);
            MagnusMinSpeed = t.Get(BallTuningIds.MagnusMinSpeed);
            MagnusFadeWidth = t.Get(BallTuningIds.MagnusFadeWidth);

            SpinDecay = t.Get(BallTuningIds.SpinDecay);
            SpinDecayFactor = DMath.Exp(-SpinDecay * Dt);

            Mu = t.Get(BallTuningIds.Mu);
            GripKeep = t.Get(BallTuningIds.GripKeep);
            GripSpin = t.Get(BallTuningIds.GripSpin);
            ToRollVz = t.Get(BallTuningIds.ToRollVz);

            var a = t.Get(BallTuningIds.RollA);
            var b = t.Get(BallTuningIds.RollB);
            ToRestSpeed = t.Get(BallTuningIds.ToRestSpeed);

            // 표면 조건 → e·a·b (plan "표면 조건 → 파라미터 매핑")
            switch (surface)
            {
                case SurfaceCondition.Dry:
                    Restitution = t.Get(BallTuningIds.EDry);
                    break;
                case SurfaceCondition.Wet:
                    Restitution = t.Get(BallTuningIds.EWet);
                    a *= t.Get(BallTuningIds.WetShortA);
                    break;
                case SurfaceCondition.Artificial:
                    Restitution = t.Get(BallTuningIds.EArtificial);
                    break;
                case SurfaceCondition.Flooded:
                    Restitution = t.Get(BallTuningIds.EWet);
                    a *= t.Get(BallTuningIds.FloodedA);
                    b *= t.Get(BallTuningIds.FloodedB);
                    break;
                case SurfaceCondition.LongGrass:
                    Restitution = t.Get(BallTuningIds.EDry);
                    a *= t.Get(BallTuningIds.LongGrassA);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(surface), surface, "알 수 없는 표면 조건");
            }
            RollA = a;
            RollB = b;

            LimitSpeed = t.Get(BallTuningIds.LimitSpeed);
            LimitSpin = t.Get(BallTuningIds.LimitSpin);
            PostRadius = t.Get(BallTuningIds.PostRadius);
            PostRestitution = t.Get(BallTuningIds.PostRestitution);
            NetDepth = t.Get(BallTuningIds.NetDepth);
            NetDamping = t.Get(BallTuningIds.NetDamping);

            // 조합 후 2차 확인 (경계 상황: a < 0, e > 1 → 로딩 시 거부)
            if (!(RollA >= 0f)) throw new TuningLoadException(BallTuningIds.RollA, "표면 보정 후 a < 0");
            if (!(RollB >= 0f)) throw new TuningLoadException(BallTuningIds.RollB, "표면 보정 후 b < 0");
            if (!(Restitution >= 0f && Restitution <= 1f)) throw new TuningLoadException(BallTuningIds.EDry, "반발계수가 [0, 1] 밖");
            if (!(PostRestitution >= 0f && PostRestitution <= 1f)) throw new TuningLoadException(BallTuningIds.PostRestitution, "포스트 반발계수가 [0, 1] 밖");
            if (!(CdHigh <= CdLow)) throw new TuningLoadException(BallTuningIds.Panel32CdHigh, "CdHigh > CdLow");

            RollTable = new BallRollTable(this);
        }

        /// <summary>땅볼 역산용 표 (FR-33, 생성 시 1회).</summary>
        public BallRollTable RollTable { get; }

        /// <summary>공 종류를 지정해 만든다.</summary>
        public static BallParams Create(TuningBundle tuning, BallType type, SurfaceCondition surface, PitchSpec pitch)
        {
            if (tuning == null) throw new ArgumentNullException(nameof(tuning));
            if (!(pitch.LengthM > 0f && pitch.WidthM > 0f)) throw new ArgumentException("피치 크기는 양수여야 한다", nameof(pitch));
            return new BallParams(tuning, type, surface, pitch);
        }

        /// <summary>튜닝의 기본 공 종류(<c>ball.defaultType</c>)로 만든다.</summary>
        public static BallParams Create(TuningBundle tuning, SurfaceCondition surface, PitchSpec pitch) =>
            Create(tuning, DefaultType(tuning), surface, pitch);

        public static BallType DefaultType(TuningBundle tuning) =>
            tuning.Get(BallTuningIds.DefaultType) >= 0.5f ? BallType.Modern : BallType.Panel32;

        /// <summary>골대 중앙 y.</summary>
        public float GoalCenterY => Pitch.WidthM * 0.5f;
    }
}
