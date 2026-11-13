using System;
using RoadToPro.Core.Tuning;
using Ids = RoadToPro.MatchEngine.Actions.ActionTuningIds;

namespace RoadToPro.MatchEngine.Actions
{
    /// <summary>
    /// 액션 모델 파라미터 (Data/tuning/action.json, specs/008 FR-04). 필드 초기값 = 파일 기본값(<c>ActionParamsTests</c>가 일치 확인).
    /// 튜닝 묶음이 없는 경기는 <see cref="Defaults"/>를 쓴다. 생성 뒤 바꾸지 않는다. 각도는 도(deg) 그대로 둔다.
    /// </summary>
    public sealed class ActionParams
    {
        public readonly float PressSigma = 0.45f;
        public readonly float PressT = 1.5f;
        public readonly float PressReaction = 0.7f;
        public readonly float PressVmax = 7.0f;
        public readonly float PressMinSpeed = 2.0f;
        public readonly float PressTouchline = 0.15f;
        public readonly float PressTouchBand = 3.0f;
        public readonly float XpB0 = 3.2f;
        public readonly float XpB1 = 0.45f;
        public readonly float XpB2 = 1.0f;
        public readonly float XpB3 = 2.0f;
        public readonly float XpB4 = 1.5f;
        public readonly float XpB5 = 0.6f;
        public readonly float XpB6 = 1.2f;
        public readonly float XpB7 = 0.5f;
        public readonly float XpLaneWidth = 3.0f;
        public readonly float PcScale = 0.5f;
        public readonly float XtDist = 60.0f;
        public readonly float LaneRefSpeed = 14.0f;
        public readonly float LoftLane = 0.3f;
        public readonly float LeadHorizon = 5.0f;
        public readonly float SpeedG0 = 10.0f;
        public readonly float SpeedG1 = 14.0f;
        public readonly float SpeedG2 = 18.0f;
        public readonly float SpeedG3 = 22.0f;
        public readonly float LoftLow = 25.0f;
        public readonly float LoftHigh = 40.0f;
        public readonly float LoftPenalty = 0.4f;
        public readonly float RecvDecayMin = 0.6f;
        public readonly float RecvSpeedLo = 12.0f;
        public readonly float RecvSpeedSpan = 12.0f;
        public readonly float RecvTouchRelief = 0.5f;
        public readonly float ErrAngleMax = 7.0f;
        public readonly float ErrAngleMin = 1.5f;
        public readonly float ErrPressK = 0.8f;
        public readonly float ErrComposureK = 0.5f;
        public readonly float ErrSpeedMax = 0.15f;
        public readonly float ErrSpeedMin = 0.04f;
        public readonly float Lead0 = 0.0f;
        public readonly float Lead1 = 2.0f;
        public readonly float Lead2 = 4.0f;
        public readonly float Lead3 = 6.0f;
        public readonly float CtrlRadius = 1.0f;
        public readonly float CtrlMaxZ = 2.5f;
        public readonly float CtrlBase = 4.5f;
        public readonly float CtrlDistK = 2.0f;
        public readonly float CtrlSpeedRef = 5.0f;
        public readonly float CtrlSpeedK = 0.1f;
        public readonly float CtrlTouchK = 1.0f;
        public readonly float CtrlPressK = 1.0f;
        public readonly float CtrlDefPenalty = 2.5f;
        public readonly float CtrlHighZ = 1.0f;
        public readonly float CtrlHighPenalty = 0.5f;
        public readonly float CtrlFreeSpeed = 2.0f;
        public readonly float XpSampleDt = 0.05f;
        public readonly float XpHorizon = 3.0f;
        public readonly float TouchSigmaBase = 1.8f;
        public readonly float TouchSigmaSlope = 1.4f;
        public readonly float TouchPressK = 0.5f;
        public readonly float TouchAngle = 60.0f;
        public readonly float TouchControlDist = 1.5f;
        public readonly float TouchHeavy = 1.5f;
        public readonly float TouchCooldown = 0.05f;
        public readonly float KickReach = 0.9f;
        public readonly float WindupInside = 0.15f;
        public readonly float WindupInstep = 0.25f;
        public readonly float WindupOutside = 0.12f;
        public readonly float WindupChip = 0.22f;
        public readonly float WindupTurnK = 0.2f;
        public readonly float InstepSpeed = 18.0f;
        public readonly float ChipMaxDist = 25.0f;
        public readonly float KickAngleThresh = 60.0f;
        public readonly float KickSigmaMul = 1.5f;
        public readonly float WeakFootK = 0.25f;
        public readonly float KickTimeout = 1.0f;
        public readonly float AfterKickSlow = 0.6f;
        public readonly float AfterDuration = 0.3f;
        public readonly float HabitOneTouch = 0.05f;
        public readonly float CtrlKeeperMul = 1.8f;
        public readonly float LeadMinRunnerSpeed = 3.0f;
        public readonly float LoftMaxApex = 40.0f;
        public readonly float ReceiveAhead = 0.3f;
        public readonly float OneTouchTicks = 2.0f;
        public readonly float StandInNoise = 0.15f;
        public readonly float StandInXtK = 10.0f;
        public readonly float StandInGainK = 0.02f;
        public readonly float StandInFloor = 0.05f;
        public readonly float ProbMin = 0.001f;
        public readonly float ProbMax = 0.999f;

        /// <summary>코드 기본값 묶음의 해시 대용.</summary>
        public readonly ulong Hash;

        ActionParams() { Hash = 0x6163_745F_6465_6661UL; }

        ActionParams(TuningBundle t)
        {
            if (t == null) throw new ArgumentNullException(nameof(t));
            PressSigma = t.Get(Ids.PressSigma);
            PressT = t.Get(Ids.PressT);
            PressReaction = t.Get(Ids.PressReaction);
            PressVmax = t.Get(Ids.PressVmax);
            PressMinSpeed = t.Get(Ids.PressMinSpeed);
            PressTouchline = t.Get(Ids.PressTouchline);
            PressTouchBand = t.Get(Ids.PressTouchBand);
            XpB0 = t.Get(Ids.XpB0);
            XpB1 = t.Get(Ids.XpB1);
            XpB2 = t.Get(Ids.XpB2);
            XpB3 = t.Get(Ids.XpB3);
            XpB4 = t.Get(Ids.XpB4);
            XpB5 = t.Get(Ids.XpB5);
            XpB6 = t.Get(Ids.XpB6);
            XpB7 = t.Get(Ids.XpB7);
            XpLaneWidth = t.Get(Ids.XpLaneWidth);
            PcScale = t.Get(Ids.PcScale);
            XtDist = t.Get(Ids.XtDist);
            LaneRefSpeed = t.Get(Ids.LaneRefSpeed);
            LoftLane = t.Get(Ids.LoftLane);
            LeadHorizon = t.Get(Ids.LeadHorizon);
            SpeedG0 = t.Get(Ids.SpeedG0);
            SpeedG1 = t.Get(Ids.SpeedG1);
            SpeedG2 = t.Get(Ids.SpeedG2);
            SpeedG3 = t.Get(Ids.SpeedG3);
            LoftLow = t.Get(Ids.LoftLow);
            LoftHigh = t.Get(Ids.LoftHigh);
            LoftPenalty = t.Get(Ids.LoftPenalty);
            RecvDecayMin = t.Get(Ids.RecvDecayMin);
            RecvSpeedLo = t.Get(Ids.RecvSpeedLo);
            RecvSpeedSpan = t.Get(Ids.RecvSpeedSpan);
            RecvTouchRelief = t.Get(Ids.RecvTouchRelief);
            ErrAngleMax = t.Get(Ids.ErrAngleMax);
            ErrAngleMin = t.Get(Ids.ErrAngleMin);
            ErrPressK = t.Get(Ids.ErrPressK);
            ErrComposureK = t.Get(Ids.ErrComposureK);
            ErrSpeedMax = t.Get(Ids.ErrSpeedMax);
            ErrSpeedMin = t.Get(Ids.ErrSpeedMin);
            Lead0 = t.Get(Ids.Lead0);
            Lead1 = t.Get(Ids.Lead1);
            Lead2 = t.Get(Ids.Lead2);
            Lead3 = t.Get(Ids.Lead3);
            CtrlRadius = t.Get(Ids.CtrlRadius);
            CtrlMaxZ = t.Get(Ids.CtrlMaxZ);
            CtrlBase = t.Get(Ids.CtrlBase);
            CtrlDistK = t.Get(Ids.CtrlDistK);
            CtrlSpeedRef = t.Get(Ids.CtrlSpeedRef);
            CtrlSpeedK = t.Get(Ids.CtrlSpeedK);
            CtrlTouchK = t.Get(Ids.CtrlTouchK);
            CtrlPressK = t.Get(Ids.CtrlPressK);
            CtrlDefPenalty = t.Get(Ids.CtrlDefPenalty);
            CtrlHighZ = t.Get(Ids.CtrlHighZ);
            CtrlHighPenalty = t.Get(Ids.CtrlHighPenalty);
            CtrlFreeSpeed = t.Get(Ids.CtrlFreeSpeed);
            XpSampleDt = t.Get(Ids.XpSampleDt);
            XpHorizon = t.Get(Ids.XpHorizon);
            TouchSigmaBase = t.Get(Ids.TouchSigmaBase);
            TouchSigmaSlope = t.Get(Ids.TouchSigmaSlope);
            TouchPressK = t.Get(Ids.TouchPressK);
            TouchAngle = t.Get(Ids.TouchAngle);
            TouchControlDist = t.Get(Ids.TouchControlDist);
            TouchHeavy = t.Get(Ids.TouchHeavy);
            TouchCooldown = t.Get(Ids.TouchCooldown);
            KickReach = t.Get(Ids.KickReach);
            WindupInside = t.Get(Ids.WindupInside);
            WindupInstep = t.Get(Ids.WindupInstep);
            WindupOutside = t.Get(Ids.WindupOutside);
            WindupChip = t.Get(Ids.WindupChip);
            WindupTurnK = t.Get(Ids.WindupTurnK);
            InstepSpeed = t.Get(Ids.InstepSpeed);
            ChipMaxDist = t.Get(Ids.ChipMaxDist);
            KickAngleThresh = t.Get(Ids.KickAngleThresh);
            KickSigmaMul = t.Get(Ids.KickSigmaMul);
            WeakFootK = t.Get(Ids.WeakFootK);
            KickTimeout = t.Get(Ids.KickTimeout);
            AfterKickSlow = t.Get(Ids.AfterKickSlow);
            AfterDuration = t.Get(Ids.AfterDuration);
            HabitOneTouch = t.Get(Ids.HabitOneTouch);
            CtrlKeeperMul = t.Get(Ids.CtrlKeeperMul);
            LeadMinRunnerSpeed = t.Get(Ids.LeadMinRunnerSpeed);
            LoftMaxApex = t.Get(Ids.LoftMaxApex);
            ReceiveAhead = t.Get(Ids.ReceiveAhead);
            OneTouchTicks = t.Get(Ids.OneTouchTicks);
            StandInNoise = t.Get(Ids.StandInNoise);
            StandInXtK = t.Get(Ids.StandInXtK);
            StandInGainK = t.Get(Ids.StandInGainK);
            StandInFloor = t.Get(Ids.StandInFloor);
            ProbMin = t.Get(Ids.ProbMin);
            ProbMax = t.Get(Ids.ProbMax);
            Hash = t.Hash;
        }

        public static ActionParams Create(TuningBundle t) => t == null ? Defaults : new ActionParams(t);

        /// <summary>코드 기본값 (파일 기본값과 같다).</summary>
        public static readonly ActionParams Defaults = new ActionParams();
    }
}
