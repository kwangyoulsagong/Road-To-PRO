using System;
using System.Collections.Generic;
using RoadToPro.MatchEngine.Tactics;

namespace RoadToPro.MatchEngine.Scenarios
{
    /// <summary>시나리오 분류 (FR-01).</summary>
    public enum ScenarioCategory : byte { BU = 0, PR = 1, FT = 2, FN = 3, CT = 4, DEF = 5, TRN = 6, SP = 7, GKP = 8, SPC = 9 }

    /// <summary>시나리오를 고르는 팀의 입장.</summary>
    public enum ScenarioSide : byte { Attack = 0, Defense = 1 }

    /// <summary>단계 실행 분류 (플랜 "실행 클래스 7종").</summary>
    public enum VerbClass : byte { Move = 0, Carry = 1, TakeOn = 2, Pass = 3, Shoot = 4, Defend = 5, Wait = 6 }

    /// <summary>단계 동사 닫힌 목록 (FR-03). 값은 <see cref="ScenarioVerbs"/> 표의 위치다.</summary>
    public enum ScenarioVerb : ushort
    {
        CheckIn, CheckOut, Drop, Spin, RunInBehind, Overlap, Underlap, ThirdManRun, BlindSideRun, DiagonalRun,
        NearPostRun, FarPostRun, ArriveLate, CrashBox, RotateTo, UnderlapSupport, DecoyPullCb,
        Carry, CarryLine, TakeOn, CutInside,
        PassShort, LayOff, WallReturn, PassThrough, PassBetweenLines, SwitchLong, BallOverTop, Chip, Knockdown, FlickOn,
        CrossEarly, CrossDrivenLow, CrossLofted, Cutback, CutbackTo, Deliver,
        Shoot,
        Dummy, HoldWidth, Pin, Decoy, Screen, RestDefense, Receive, ReceiveFacingForward,
        CurveRun, Engage, MarkLane, TightMark, CoverShadow, TrackSt, SprintTo, FlickHeader, AttackZone, StandInFront, DecoyRun,
    }

    public static class ScenarioVerbs
    {
        struct Row { public string Name; public VerbClass Class; public bool Air; }

        static Row R(string n, VerbClass c, bool air = false) => new Row { Name = n, Class = c, Air = air };

        static readonly Row[] Table =
        {
            R("check_in", VerbClass.Move), R("check_out", VerbClass.Move), R("drop", VerbClass.Move), R("spin", VerbClass.Move),
            R("run_in_behind", VerbClass.Move), R("overlap", VerbClass.Move), R("underlap", VerbClass.Move), R("third_man_run", VerbClass.Move),
            R("blind_side_run", VerbClass.Move), R("diagonal_run", VerbClass.Move), R("near_post_run", VerbClass.Move), R("far_post_run", VerbClass.Move),
            R("arrive_late", VerbClass.Move), R("crash_box", VerbClass.Move), R("rotate_to", VerbClass.Move), R("underlap_support", VerbClass.Move), R("decoy_pull_cb", VerbClass.Move),
            R("carry", VerbClass.Carry), R("carry_line", VerbClass.Carry), R("take_on", VerbClass.TakeOn), R("cut_inside", VerbClass.Carry),
            R("pass_short", VerbClass.Pass), R("lay_off", VerbClass.Pass), R("wall_return", VerbClass.Pass), R("pass_through", VerbClass.Pass),
            R("pass_between_lines", VerbClass.Pass), R("switch_long", VerbClass.Pass, true), R("ball_over_top", VerbClass.Pass, true), R("chip", VerbClass.Pass, true),
            R("knockdown", VerbClass.Move), R("flick_on", VerbClass.Move),
            R("cross_early", VerbClass.Pass, true), R("cross_driven_low", VerbClass.Pass), R("cross_lofted", VerbClass.Pass, true),
            R("cutback", VerbClass.Pass), R("cutback_to", VerbClass.Pass), R("deliver", VerbClass.Pass, true),
            R("shoot", VerbClass.Shoot),
            R("dummy", VerbClass.Wait), R("hold_width", VerbClass.Wait), R("pin", VerbClass.Wait), R("decoy", VerbClass.Wait), R("screen", VerbClass.Wait),
            R("rest_defense", VerbClass.Wait), R("receive", VerbClass.Wait), R("receive_facing_forward", VerbClass.Wait),
            R("curve_run", VerbClass.Defend), R("engage", VerbClass.Defend), R("mark_lane", VerbClass.Defend), R("tight_mark", VerbClass.Defend),
            R("cover_shadow", VerbClass.Defend), R("track_st", VerbClass.Defend), R("sprint_to", VerbClass.Move), R("flick_header", VerbClass.Move),
            R("attack_zone", VerbClass.Move), R("stand_in_front", VerbClass.Defend), R("decoy_run", VerbClass.Move),
        };

        static readonly Dictionary<string, ScenarioVerb> ByName = Build();

        static Dictionary<string, ScenarioVerb> Build()
        {
            var d = new Dictionary<string, ScenarioVerb>(StringComparer.Ordinal);
            for (int i = 0; i < Table.Length; i++) d[Table[i].Name] = (ScenarioVerb)i;
            return d;
        }

        public static bool TryParse(string name, out ScenarioVerb v) => ByName.TryGetValue(name, out v);
        public static string NameOf(ScenarioVerb v) => Table[(int)v].Name;
        public static VerbClass ClassOf(ScenarioVerb v) => Table[(int)v].Class;
        public static bool IsAir(ScenarioVerb v) => Table[(int)v].Air;
        public static bool IsReceive(ScenarioVerb v) => v == ScenarioVerb.Receive || v == ScenarioVerb.ReceiveFacingForward;
    }

    /// <summary>역할 비트 (research[0] § 0 역할 표기).</summary>
    [Flags]
    public enum PositionMask : ushort
    {
        None = 0, GK = 1 << 0, CB = 1 << 1, WCB = 1 << 2, FB = 1 << 3, IFB = 1 << 4, WB = 1 << 5, DM = 1 << 6, CM = 1 << 7,
        AM = 1 << 8, W = 1 << 9, IW = 1 << 10, ST = 1 << 11, F9 = 1 << 12, TM = 1 << 13, SS = 1 << 14,
        AnyField = 0x7FFF & ~GK,
    }

    public static class Positions
    {
        public static bool TryParse(string s, out PositionMask m)
        {
            switch (s)
            {
                case "GK": m = PositionMask.GK; return true;
                case "CB": m = PositionMask.CB; return true;
                case "WCB": m = PositionMask.WCB; return true;
                case "FB": m = PositionMask.FB; return true;
                case "IFB": m = PositionMask.IFB; return true;
                case "WB": m = PositionMask.WB; return true;
                case "DM": m = PositionMask.DM; return true;
                case "CM": m = PositionMask.CM; return true;
                case "AM": m = PositionMask.AM; return true;
                case "W": m = PositionMask.W; return true;
                case "IW": m = PositionMask.IW; return true;
                case "IF": m = PositionMask.IW; return true; // 기획서 5.1.1 표기
                case "ST": m = PositionMask.ST; return true;
                case "F9": m = PositionMask.F9; return true;
                case "TM": m = PositionMask.TM; return true;
                case "SS": m = PositionMask.SS; return true;
                case "any": m = PositionMask.AnyField | PositionMask.GK; return true;
                default: m = PositionMask.None; return false;
            }
        }

        /// <summary>009 포메이션 슬롯의 기본 포지션이 채울 수 있는 역할.</summary>
        public static PositionMask FromFormationPos(SlotPosition pos)
        {
            switch (pos)
            {
                case SlotPosition.GK: return PositionMask.GK;
                case SlotPosition.CB: return PositionMask.CB | PositionMask.WCB;
                case SlotPosition.FB: return PositionMask.FB | PositionMask.IFB | PositionMask.WB;
                case SlotPosition.DM: return PositionMask.DM;
                case SlotPosition.CM: return PositionMask.CM;
                case SlotPosition.AM: return PositionMask.AM;
                case SlotPosition.W: return PositionMask.W | PositionMask.IW;
                case SlotPosition.ST: return PositionMask.ST | PositionMask.F9 | PositionMask.TM | PositionMask.SS;
                default: return PositionMask.None;
            }
        }
    }

    public enum RoleSideFilter : byte { Any = 0, Left = 1, Right = 2, BallSide = 3, FarSide = 4 }

    /// <summary>공 상태 트리거 비트 (FR-10).</summary>
    [Flags]
    public enum BallStateMask : byte { None = 0, Control = 1, Bouncing = 2, Aerial = 4, Dead = 8, Any = 15 }

    /// <summary>상대 블록 비트 (FR-10, 플랜 매핑표).</summary>
    [Flags]
    public enum OppBlockMask : byte { None = 0, HP = 1, MB = 2, LB = 4, HL = 8, UNB = 16 }

    /// <summary>트리거 이벤트 비트 (FR-10).</summary>
    [Flags]
    public enum TriggerEventMask : uint
    {
        None = 0, BackPass = 1, BadTouch = 2, BackToGoalReceive = 4, WideReceive = 8, GkReceive = 16, Regain = 32, Loss = 64, OppReceive = 128,
        CornerAward = 256, FreeKickAward = 512, ThrowInAward = 1024, GoalKickAward = 2048, PenaltyAward = 4096, KickoffAward = 8192,
    }

    public static class TriggerEvents
    {
        public static bool TryParse(string s, out TriggerEventMask m)
        {
            switch (s)
            {
                case "back_pass": m = TriggerEventMask.BackPass; return true;
                case "bad_touch": m = TriggerEventMask.BadTouch; return true;
                case "back_to_goal_receive": m = TriggerEventMask.BackToGoalReceive; return true;
                case "wide_receive": m = TriggerEventMask.WideReceive; return true;
                case "gk_receive": m = TriggerEventMask.GkReceive; return true;
                case "regain": m = TriggerEventMask.Regain; return true;
                case "loss": m = TriggerEventMask.Loss; return true;
                case "opp_receive": m = TriggerEventMask.OppReceive; return true;
                case "corner_award": m = TriggerEventMask.CornerAward; return true;
                case "free_kick_award": m = TriggerEventMask.FreeKickAward; return true;
                case "throw_in_award": m = TriggerEventMask.ThrowInAward; return true;
                case "goal_kick_award": m = TriggerEventMask.GoalKickAward; return true;
                case "penalty_award": m = TriggerEventMask.PenaltyAward; return true;
                case "kickoff": m = TriggerEventMask.KickoffAward; return true;
                default: m = TriggerEventMask.None; return false;
            }
        }
    }

    /// <summary>공간·수 조건 필드 (FR-10, 공간은 007 자리).</summary>
    public enum SpaceField : byte
    {
        SpaceAheadM = 0, FbFrontSpaceM = 1, BehindSpaceM = 2, NearestPresserDistM = 3, LineHeight = 4, DefendersAhead = 5, AttackersAhead = 6, P3UnmarkedInFront = 7,
    }

    public static class SpaceFields
    {
        public const int Count = 8;

        public static bool TryParse(string s, out SpaceField f)
        {
            switch (s)
            {
                case "spaceAheadM": f = SpaceField.SpaceAheadM; return true;
                case "fbFrontSpaceM": f = SpaceField.FbFrontSpaceM; return true;
                case "behindSpaceM": f = SpaceField.BehindSpaceM; return true;
                case "nearestPresserDistM": f = SpaceField.NearestPresserDistM; return true;
                case "lineHeight": f = SpaceField.LineHeight; return true;
                case "defendersAhead": f = SpaceField.DefendersAhead; return true;
                case "attackersAhead": f = SpaceField.AttackersAhead; return true;
                case "p3UnmarkedInFront": f = SpaceField.P3UnmarkedInFront; return true;
                default: f = default; return false;
            }
        }
    }

    public enum CompareOp : byte { Ge = 0, Le = 1, Gt = 2, Lt = 3, Eq = 4 }

    /// <summary>상황 분기 조건 닫힌 목록 (FR-42). 대체 입력 동안 판정할 수 없는 것은 거짓이다 (008·011 연결 때 채운다).</summary>
    public enum BranchCondition : byte
    {
        Always = 0, Regain, ShotLaneOpen, CbStepsUp, DefenderDrops, P2Unpressed,
        FbMarkerFollowsOverlap, OppLongClear, OppWallPassSuccess, Foul, FirstContactLost, GkPunch, ClearToCounter,
    }

    public static class BranchConditions
    {
        public static bool TryParse(string s, out BranchCondition c)
        {
            switch (s)
            {
                case "always": c = BranchCondition.Always; return true;
                case "regain": c = BranchCondition.Regain; return true;
                case "shot_lane_open": c = BranchCondition.ShotLaneOpen; return true;
                case "cb_steps_up": c = BranchCondition.CbStepsUp; return true;
                case "defender_drops": c = BranchCondition.DefenderDrops; return true;
                case "P2_unpressed": c = BranchCondition.P2Unpressed; return true;
                case "fb_marker_follows_overlap": c = BranchCondition.FbMarkerFollowsOverlap; return true;
                case "opp_long_clear": c = BranchCondition.OppLongClear; return true;
                case "opp_wall_pass_success": c = BranchCondition.OppWallPassSuccess; return true;
                case "foul": c = BranchCondition.Foul; return true;
                case "first_contact_lost": c = BranchCondition.FirstContactLost; return true;
                case "gk_punch": c = BranchCondition.GkPunch; return true;
                case "clear_to_counter": c = BranchCondition.ClearToCounter; return true;
                default: c = default; return false;
            }
        }
    }

    /// <summary>시나리오 종료 사유 (FR-43·54). 번호는 이벤트 로그(<c>SimEvent.B</c>)에 들어가므로 바꾸지 않는다.</summary>
    public enum ScenarioEndReason : byte
    {
        Completed = 0, Chained = 1, Interception = 2, Tackle = 3, PassFailed = 4, BallDead = 5, Whistle = 6, CastLost = 7,
        StepTimeout = 8, ScenarioTimeout = 9, BranchFallback = 10, ChainLimit = 11, TriggerGone = 12, PossessionLost = 13,
    }

    public static class ScenarioEndReasons
    {
        public const int Count = 14;
        /// <summary>폴백으로 집계하는 종료 (완주·체이닝 제외).</summary>
        public static bool IsFallback(ScenarioEndReason r) => r != ScenarioEndReason.Completed && r != ScenarioEndReason.Chained;
    }

    public enum StepOutcome : byte { None = 0, Success = 1, Fail = 2, Timeout = 3 }

    /// <summary>국면 비트 (FR-10, 009 값).</summary>
    public static class PhaseBits
    {
        public const ushort All = 0x1FF;
        public static ushort Of(TeamPhase p) => (ushort)(1 << (int)p);

        /// <summary>국면 표기 → 비트. 수비 문서 표기는 매핑한다 (FR-10, AC-18). PRESS는 압박 개시 조건을 함께 건다.</summary>
        public static bool TryParse(string s, out ushort mask, out bool requirePress)
        {
            requirePress = false;
            switch (s)
            {
                case "BuildUp": mask = Of(TeamPhase.BuildUp); return true;
                case "Progression": mask = Of(TeamPhase.Progression); return true;
                case "FinalThird": mask = Of(TeamPhase.FinalThird); return true;
                case "AttTransition": case "TRANS_A": mask = Of(TeamPhase.AttTransition); return true;
                case "DefTransition": case "TRANS_D": mask = Of(TeamPhase.DefTransition); return true;
                case "DefHigh": mask = Of(TeamPhase.DefHigh); return true;
                case "DefMid": mask = Of(TeamPhase.DefMid); return true;
                case "DefLow": case "LOW_BLOCK": mask = Of(TeamPhase.DefLow); return true;
                case "SetPiece": case "SET_PIECE": mask = Of(TeamPhase.SetPiece); return true;
                case "SET_DEF": mask = (ushort)(Of(TeamPhase.DefHigh) | Of(TeamPhase.DefMid) | Of(TeamPhase.DefLow)); return true;
                case "PRESS": mask = Of(TeamPhase.DefHigh); requirePress = true; return true;
                default: mask = 0; return false;
            }
        }
    }
}
