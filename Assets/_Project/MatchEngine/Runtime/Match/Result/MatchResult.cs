using System;
using System.Collections.Generic;
using RoadToPro.Core.Hashing;
using RoadToPro.Core.Time;
using RoadToPro.MatchEngine.Kinematics;
using RoadToPro.MatchEngine.Match.Events;
using RoadToPro.MatchEngine.Match.Flow;
using RoadToPro.MatchEngine.Match.Input;

namespace RoadToPro.MatchEngine.Match.Result
{
    public struct GoalRecord
    {
        public long Seq;
        public long Tick;
        public MatchTime Time;
        public int Team;
        public int ScorerId;
        public bool OwnGoal;
    }

    public struct CardRecord
    {
        public long Tick;
        public MatchTime Time;
        public int Team;
        public int PlayerId;
        public bool Red;
    }

    public struct SubstitutionRecord
    {
        public long Tick;
        public MatchTime Time;
        public int Team;
        public int InId;
        public int OutId;
        public bool Concussion;
    }

    public struct InjuryRecord
    {
        public long Tick;
        public MatchTime Time;
        public int Team;
        public int PlayerId;
    }

    public struct PlayerLine
    {
        public int Id;
        public int Team;
        public PlayerStatus FinalStatus;
        public long PlayedTicks;
        public MotionStats Motion;
    }

    /// <summary>
    /// 경기 결과 공통 형식 (specs/006 FR-40·41). 전체 정밀(006)과 저정밀(016)이 같은 형식을 채우고 커리어(024)는 구분 없이 소비한다.
    /// 이벤트 로그·체크포인트는 전체 정밀만(저정밀은 선택). 스탯 입력·평점은 015가 채운다(지금은 null).
    /// </summary>
    public sealed class MatchResult
    {
        public const int EngineVersion = 1;

        public int[] RegularGoals = new int[2];
        public int[] ExtraGoals = new int[2];
        public int[] ShootoutGoals = new int[2];
        public bool ExtraTimePlayed;
        public bool ShootoutPlayed;

        public GoalRecord[] Goals = Array.Empty<GoalRecord>();
        public CardRecord[] Cards = Array.Empty<CardRecord>();
        public SubstitutionRecord[] Substitutions = Array.Empty<SubstitutionRecord>();
        public InjuryRecord[] Injuries = Array.Empty<InjuryRecord>();
        public PlayerLine[] Players = Array.Empty<PlayerLine>();
        /// <summary>015 스탯 입력 (자리).</summary>
        public object StatsInput;
        /// <summary>015 평점 (자리).</summary>
        public object Ratings;

        public bool IsFullPrecision = true;
        public SimEvent[] EventLog;
        public int EventSchemaVersion = SimEventLog.SchemaVersion;
        public ulong EventLogHash;
        public Checkpoint[] Checkpoints = Array.Empty<Checkpoint>();
        public ulong FinalStateHash;
        public ExternalInput[] Inputs = Array.Empty<ExternalInput>();
        public long[] PeriodLengths = new long[4];

        public ulong Seed;
        public ulong TuningHash;
        public int Engine = EngineVersion;

        public EndReason Reason;
        public long EndTick;
        public int FailedStage = -1;
        public long FailedTick = -1;
        public string FailureMessage;

        public int Score(int team) => RegularGoals[team] + ExtraGoals[team];

        /// <summary>승자 (0·1), 무승부 −1. 승부차기 포함.</summary>
        public int Winner
        {
            get
            {
                if (Score(0) != Score(1)) return Score(0) > Score(1) ? 0 : 1;
                if (ShootoutPlayed && ShootoutGoals[0] != ShootoutGoals[1]) return ShootoutGoals[0] > ShootoutGoals[1] ? 0 : 1;
                return -1;
            }
        }

        /// <summary>
        /// 형식 검사 (FR-41, AC-20): 필수 필드·배열 크기, 골 기록 수 = 스코어, 이벤트 로그 일련번호 연속·정렬(있으면), 실패면 원인.
        /// 문제 목록이 비면 통과.
        /// </summary>
        public List<string> Validate()
        {
            var p = new List<string>();
            if (RegularGoals == null || RegularGoals.Length != 2 || ExtraGoals == null || ExtraGoals.Length != 2 || ShootoutGoals == null || ShootoutGoals.Length != 2)
                p.Add("스코어 배열은 팀 2개");
            else
            {
                for (int t = 0; t < 2; t++)
                {
                    if (RegularGoals[t] < 0 || ExtraGoals[t] < 0 || ShootoutGoals[t] < 0) p.Add("음수 스코어");
                    if (!ExtraTimePlayed && ExtraGoals[t] != 0) p.Add("연장 없이 연장 득점");
                    if (!ShootoutPlayed && ShootoutGoals[t] != 0) p.Add("승부차기 없이 승부차기 득점");
                }
                if (Goals != null)
                {
                    var c = new int[2];
                    foreach (var g in Goals) if (g.Team == 0 || g.Team == 1) c[g.Team]++; else p.Add($"골 팀 값 {g.Team}");
                    if (c[0] != Score(0) || c[1] != Score(1)) p.Add($"골 기록 {c[0]}-{c[1]} ≠ 스코어 {Score(0)}-{Score(1)}");
                }
            }
            if (Goals == null || Cards == null || Substitutions == null || Injuries == null || Players == null) p.Add("기록 배열 null");
            if (Players != null && Players.Length < 22) p.Add($"선수 줄 {Players.Length} < 22");
            if (PeriodLengths == null || PeriodLengths.Length != 4) p.Add("기간 길이 배열은 4");
            if (Reason == EndReason.None) p.Add("종료 사유 없음");
            if (Reason == EndReason.Failed && (FailedTick < 0 || string.IsNullOrEmpty(FailureMessage))) p.Add("실패 원인 없음");
            if (Engine <= 0) p.Add("엔진 버전 없음");
            if (EventLog != null)
            {
                if (!IsFullPrecision && EventLog.Length == 0) { }
                for (int i = 0; i < EventLog.Length; i++)
                {
                    if (EventLog[i].Seq != i + 1) { p.Add($"이벤트 일련번호 불연속 @{i}"); break; }
                    if (i > 0 && SimEvent.CompareKey(in EventLog[i - 1], in EventLog[i]) > 0) { p.Add($"이벤트 정렬 위반 @{i}"); break; }
                }
            }
            else if (IsFullPrecision) p.Add("전체 정밀인데 이벤트 로그 없음");
            return p;
        }
    }
}
