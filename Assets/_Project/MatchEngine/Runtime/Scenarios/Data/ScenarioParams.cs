using System;
using RoadToPro.Core.Tuning;

namespace RoadToPro.MatchEngine.Scenarios
{
    /// <summary>시나리오 튜닝 + 라이브러리 (MatchTuning.Scenario). 해시는 경기 결과의 튜닝 해시에 들어간다.</summary>
    public sealed class ScenarioData
    {
        public readonly TuningBundle Tuning;
        public readonly ScenarioLibrary Library;

        public ScenarioData(TuningBundle tuning, ScenarioLibrary library)
        {
            Tuning = tuning ?? throw new ArgumentNullException(nameof(tuning));
            Library = library ?? throw new ArgumentNullException(nameof(library));
        }
    }

    /// <summary>불변 파라미터 (Data/tuning/scenario.json). 틱 경로에서 사전 조회가 없도록 필드로 펼친다.</summary>
    public sealed class ScenarioParams
    {
        public const int TicksPerSecond = 10;

        public readonly float TauMax, TauMin, DecayFactor, TacticGain, ModifierMin, ModifierMax;
        public readonly float PriorOpenPlay, PriorSetPiece, PriorPenalty, PriorCounter;
        public readonly float DefaultSuccessRate, DefaultXg, ArriveM, HighLineX;
        public readonly float AerialSpeed, MoveAheadM, TakeOnAheadM, LaneOffsetM;
        public readonly long DecayRecoverTicks, StepLimitTicks, ScenarioLimitTicks, CounterWindowTicks;
        public readonly int ChainLimit, ReevalTicks;

        public ScenarioParams(TuningBundle t)
        {
            TauMax = t.Get("scenario.tauMax");
            TauMin = t.Get("scenario.tauMin");
            DecayFactor = t.Get("scenario.decayFactor");
            DecayRecoverTicks = Ticks(t.Get("scenario.decayRecoverS"));
            StepLimitTicks = Ticks(t.Get("scenario.stepLimitS"));
            ScenarioLimitTicks = Ticks(t.Get("scenario.scenarioLimitS"));
            ChainLimit = (int)t.Get("scenario.chainLimit");
            CounterWindowTicks = Ticks(t.Get("scenario.counterWindowS"));
            PriorOpenPlay = t.Get("scenario.priorOpenPlay");
            PriorSetPiece = t.Get("scenario.priorSetPiece");
            PriorPenalty = t.Get("scenario.priorPenalty");
            PriorCounter = t.Get("scenario.priorCounter");
            DefaultSuccessRate = t.Get("scenario.defaultSuccessRate");
            DefaultXg = t.Get("scenario.defaultXg");
            TacticGain = t.Get("scenario.tacticGain");
            ModifierMin = t.Get("scenario.modifierMin");
            ModifierMax = t.Get("scenario.modifierMax");
            ArriveM = t.Get("scenario.arriveM");
            HighLineX = t.Get("scenario.highLineX");
            ReevalTicks = (int)t.Get("scenario.reevalTicks");
            AerialSpeed = t.Get("scenario.aerialFlightSpeed");
            MoveAheadM = t.Get("scenario.moveAheadM");
            TakeOnAheadM = t.Get("scenario.takeOnAheadM");
            LaneOffsetM = t.Get("scenario.laneOffsetM");
        }

        public static long Ticks(float seconds) => seconds < 0f ? -(long)(-seconds * TicksPerSecond + 0.5f) : (long)(seconds * TicksPerSecond + 0.5f);
    }
}
