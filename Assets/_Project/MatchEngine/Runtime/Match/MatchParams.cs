using System;
using RoadToPro.Core.Time;
using RoadToPro.Core.Tuning;
using RoadToPro.MatchEngine.Match.Events;
using T = RoadToPro.MatchEngine.Match.MatchTuningIds;

namespace RoadToPro.MatchEngine.Match
{
    /// <summary>
    /// 경기 루프 파라미터 (specs/006). 정지 가산 계수(FR-20, 기본 0)와 대체 단계 값([대체], 디자인 값 아님).
    /// 시간 값은 결정 틱으로 바꿔 둔다.
    /// </summary>
    public sealed class MatchParams
    {
        /// <summary>요인별 가산 (틱). 인덱스는 <see cref="EventCodes"/>.Stop*.</summary>
        public readonly long[] StoppageCoefTicks = new long[EventCodes.StopKinds];

        public readonly long GoalCelebrationTicks, SubDelayTicks, RestartDelayTicks, KickoffDelayTicks, HoldTicks;
        public readonly float ShotRange, ShotProb, ShotSpeed, PassRange, PassExtraSpeed;
        public readonly float ClaimRadius, ClaimMaxZ, ClaimMaxSpeed, TackleRange, TackleProb;
        public readonly float ShapeShiftX, ShapeShiftY, PenaltyScoreProb, CarrySpeedRatio;
        public readonly ulong Hash;

        MatchParams(TuningBundle t)
        {
            if (t == null) throw new ArgumentNullException(nameof(t));
            StoppageCoefTicks[EventCodes.StopGoal] = Ticks(t.Get(T.CoefGoal));
            StoppageCoefTicks[EventCodes.StopSubstitution] = Ticks(t.Get(T.CoefSubstitution));
            StoppageCoefTicks[EventCodes.StopInjury] = Ticks(t.Get(T.CoefInjury));
            StoppageCoefTicks[EventCodes.StopVar] = Ticks(t.Get(T.CoefVar));
            StoppageCoefTicks[EventCodes.StopCard] = Ticks(t.Get(T.CoefCard));
            StoppageCoefTicks[EventCodes.StopDissent] = Ticks(t.Get(T.CoefDissent));
            GoalCelebrationTicks = Ticks(t.Get(T.GoalCelebration));
            SubDelayTicks = Ticks(t.Get(T.SubDelay));
            RestartDelayTicks = Ticks(t.Get(T.RestartDelay));
            KickoffDelayTicks = Ticks(t.Get(T.KickoffDelay));
            HoldTicks = Ticks(t.Get(T.HoldTime));
            ShotRange = t.Get(T.ShotRange);
            ShotProb = t.Get(T.ShotProb);
            ShotSpeed = t.Get(T.ShotSpeed);
            PassRange = t.Get(T.PassRange);
            PassExtraSpeed = t.Get(T.PassExtraSpeed);
            ClaimRadius = t.Get(T.ClaimRadius);
            ClaimMaxZ = t.Get(T.ClaimMaxZ);
            ClaimMaxSpeed = t.Get(T.ClaimMaxSpeed);
            TackleRange = t.Get(T.TackleRange);
            TackleProb = t.Get(T.TackleProb);
            ShapeShiftX = t.Get(T.ShapeShiftX);
            ShapeShiftY = t.Get(T.ShapeShiftY);
            PenaltyScoreProb = t.Get(T.PenaltyScoreProb);
            CarrySpeedRatio = t.Get(T.CarrySpeedRatio);
            Hash = t.Hash;
        }

        public static MatchParams Create(TuningBundle t) => new MatchParams(t);

        static long Ticks(float seconds) => (long)Math.Round(seconds * SimClock.TicksPerSecond, MidpointRounding.AwayFromZero);
    }

    /// <summary>경기 엔진 튜닝 묶음: 공(004)·운동(005)·경기(006). 피치 크기·표면은 경기 설정에서 온다.</summary>
    public sealed class MatchTuning
    {
        public readonly TuningBundle Ball;
        public readonly TuningBundle Kinematics;
        public readonly TuningBundle Match;
        /// <summary>팀 형태 데이터 (specs/009). 전술을 쓰는 경기에만 필요하다.</summary>
        public readonly Tactics.ShapeData Shape;
        /// <summary>시나리오 데이터 (specs/013). 전술을 쓰는 경기에서만 쓰고, 없으면 대체 단계 그대로다.</summary>
        public readonly Scenarios.ScenarioData Scenario;

        public MatchTuning(TuningBundle ball, TuningBundle kinematics, TuningBundle match, Tactics.ShapeData shape = null, Scenarios.ScenarioData scenario = null)
        {
            Shape = shape;
            Scenario = scenario;
            Ball = ball ?? throw new ArgumentNullException(nameof(ball));
            Kinematics = kinematics ?? throw new ArgumentNullException(nameof(kinematics));
            Match = match ?? throw new ArgumentNullException(nameof(match));
        }

        /// <summary>세 묶음 해시를 합친 튜닝 해시 (FR-10·40, 001 FR-81).</summary>
        public ulong Hash
        {
            get
            {
                var h = Core.Hashing.StableHasher.Create();
                h.Add(Ball.Hash);
                h.Add(Kinematics.Hash);
                h.Add(Match.Hash);
                if (Shape != null) { h.Add(Shape.Tuning.Hash); h.Add(Shape.Formations.Hash); }
                if (Scenario != null) { h.Add(Scenario.Tuning.Hash); h.Add(Scenario.Library.Hash); }
                return h.Value;
            }
        }
    }
}
