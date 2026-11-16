using System.Collections.Generic;
using System.Globalization;
using System.Text;
using RoadToPro.Core.Time;
using RoadToPro.MatchEngine.Actions;
using RoadToPro.MatchEngine.Match;
using RoadToPro.MatchEngine.Match.Events;

namespace RoadToPro.Tests.EditMode.MatchEngine
{
    /// <summary>
    /// specs/081 FR-01: 경기 양상 지표 집계. 지표 정의는 070 `DuelMatchTests`·071 `ShotMatchTests` 측정과 같은 이벤트 집계를 쓴다
    /// (015 스탯 정의가 생기면 017 FR-23에서 교체). 합계(<see cref="Totals"/>)를 경기마다 더한 뒤 <see cref="PerMatch"/>로 경기당 값·비율을 낸다.
    /// </summary>
    public static class MatchFeelMetrics
    {
        /// <summary>패스 결과 분류 (T04 패스 실패 원인 분해).</summary>
        public enum PassFate { Received, Intercepted, LostLoose, KeptLoose, Out, Offside, Keeper, Aerial, Other }

        public sealed class Totals
        {
            public double Matches, Goals, Shots, ShotXg, HeaderShots, HeaderGoals, Saves, ShotOnTarget, ShotGoals, OwnGoals, Passes, Received, TakeOns, TakeOnOk, Tackles, TacklesWon,
                Interceptions, Contacts, Possessions, SeqTicks, SeqCount, Touches, Steals, Crosses, CrossOk, Contests, BothTeamContests, EmptyContests, Headers;
            public readonly double[] PassFates = new double[9];

            public void Add(Totals o)
            {
                Matches += o.Matches; Goals += o.Goals; ShotOnTarget += o.ShotOnTarget; ShotGoals += o.ShotGoals; OwnGoals += o.OwnGoals; Shots += o.Shots; ShotXg += o.ShotXg; HeaderShots += o.HeaderShots; HeaderGoals += o.HeaderGoals; Saves += o.Saves;
                Passes += o.Passes; Received += o.Received; TakeOns += o.TakeOns; TakeOnOk += o.TakeOnOk; Tackles += o.Tackles; TacklesWon += o.TacklesWon;
                Interceptions += o.Interceptions; Contacts += o.Contacts; Possessions += o.Possessions; SeqTicks += o.SeqTicks; SeqCount += o.SeqCount;
                Touches += o.Touches; Steals += o.Steals; Crosses += o.Crosses; CrossOk += o.CrossOk; Contests += o.Contests; BothTeamContests += o.BothTeamContests;
                EmptyContests += o.EmptyContests; Headers += o.Headers;
                for (int i = 0; i < PassFates.Length; i++) PassFates[i] += o.PassFates[i];
            }
        }

        /// <summary>경기 1개의 이벤트 로그를 센다 (득점 = Goal 이벤트). 경합·드리블 터치 카운터는 <see cref="AddCounters"/>.</summary>
        public static Totals Count(SimEventLog log)
        {
            var t = new Totals { Matches = 1 };
            long lastPossTick = -1;
            for (int k = 0; k < log.Count; k++)
            {
                ref readonly var e = ref log[k];
                switch (e.Kind)
                {
                    case SimEventKind.Goal:
                        t.Goals++;
                        if (e.A == 1) t.OwnGoals++;
                        break;
                    case SimEventKind.Shot:
                        t.Shots++;
                        t.ShotXg += e.F1;
                        var head = (e.A & ActionSystem.ShotFlagHeader) != 0;
                        if (head) t.HeaderShots++;
                        var so = ShotOutcomeOf(log, k);
                        if (so != ShotOutcome.Off) t.ShotOnTarget++;
                        if (so == ShotOutcome.Goal) { t.ShotGoals++; if (head) t.HeaderGoals++; }
                        break;
                    case SimEventKind.Save:
                    case SimEventKind.Catch:
                    case SimEventKind.Parry: t.Saves++; break;
                    case SimEventKind.Pass:
                        t.Passes++;
                        t.PassFates[(int)FateOf(log, k)]++;
                        break;
                    case SimEventKind.Cross:
                        t.Crosses++;
                        var f = FateOf(log, k);
                        if (f == PassFate.Received || f == PassFate.KeptLoose || (f == PassFate.Aerial && AerialWonBy(log, k))) t.CrossOk++;
                        break;
                    case SimEventKind.PassReceived: t.Received++; break;
                    case SimEventKind.Dribble: t.TakeOns++; if ((e.B & EventCodes.DribbleSuccess) != 0) t.TakeOnOk++; break;
                    case SimEventKind.Tackle: t.Tackles++; if ((e.A & EventCodes.TackleWon) != 0) t.TacklesWon++; break;
                    case SimEventKind.Interception: t.Interceptions++; break;
                    case SimEventKind.Contact: t.Contacts++; break;
                    case SimEventKind.PossessionChange:
                        t.Possessions++;
                        if (lastPossTick >= 0) { t.SeqTicks += e.Tick - lastPossTick; t.SeqCount++; }
                        lastPossTick = e.Tick;
                        break;
                }
            }
            return t;
        }

        /// <summary>시뮬 상태 밖 진단 누계 (경합·헤더·드리블 터치·이격 탈취).</summary>
        public static void AddCounters(Totals t, MatchContext ctx)
        {
            var a = ctx.Actions.Aerials;
            t.Contests += a.Contests;
            t.BothTeamContests += a.BothTeamContests;
            t.EmptyContests += a.EmptyContests;
            t.Headers += a.Headers;
            t.Touches += ctx.Actions.Duels.Touches;
            t.Steals += ctx.Actions.Duels.Steals;
        }

        public enum ShotOutcome { Goal, Saved, Off }

        /// <summary>
        /// 슈팅(인덱스 k)의 첫 결과 (2026-11-16 T11 정의 수정): 골 → Goal, GK 처리(선방·캐치·쳐내기·펀칭·덮치기) → Saved(유효), 그 밖(골킥·골대·블록·가로채기·라인 아웃) → Off.
        /// 전에는 경기 합계 (선방 이벤트 + 득점) ÷ 슈팅이라 슈팅 없는 득점과 한 선방의 여러 이벤트(Save + Catch)를 같이 세 100 %를 넘었다.
        /// </summary>
        public static ShotOutcome ShotOutcomeOf(SimEventLog log, int k)
        {
            for (int j = k + 1; j < log.Count; j++)
            {
                ref readonly var e = ref log[j];
                switch (e.Kind)
                {
                    case SimEventKind.Goal: return ShotOutcome.Goal;
                    case SimEventKind.Save:
                    case SimEventKind.Catch:
                    case SimEventKind.Parry:
                    case SimEventKind.Punch:
                    case SimEventKind.Smother: return ShotOutcome.Saved;
                    case SimEventKind.Contact:
                    case SimEventKind.DiveStart:
                    case SimEventKind.KeeperRush:
                    case SimEventKind.PhaseChange:
                    case SimEventKind.Header:       // 헤딩슛 기록 이벤트 (071, 같은 틱)
                    case SimEventKind.WoodworkHit:  // 골대 맞고 들어가면 골, 나가면 다음 사건이 정한다
                        continue;
                }
                if ((int)e.Kind >= 100) return ShotOutcome.Off;
            }
            return ShotOutcome.Off;
        }

        /// <summary>패스·크로스(인덱스 k) 다음의 첫 결과 사건으로 분류한다.</summary>
        public static PassFate FateOf(SimEventLog log, int k)
        {
            var team = log[k].Team;
            for (int j = k + 1; j < log.Count; j++)
            {
                ref readonly var e = ref log[j];
                switch (e.Kind)
                {
                    case SimEventKind.PassReceived: return e.Team == team ? PassFate.Received : PassFate.Intercepted;
                    case SimEventKind.Interception: return PassFate.Intercepted;
                    case SimEventKind.BallRecovered: return e.Team == team ? PassFate.KeptLoose : PassFate.LostLoose;
                    case SimEventKind.ThrowIn:
                    case SimEventKind.GoalKick:
                    case SimEventKind.Corner: return PassFate.Out;
                    case SimEventKind.Offside: return PassFate.Offside;
                    case SimEventKind.Catch:
                    case SimEventKind.Save:
                    case SimEventKind.Parry:
                    case SimEventKind.Punch:
                    case SimEventKind.Smother: return PassFate.Keeper;
                    case SimEventKind.Header: return PassFate.Aerial;
                    case SimEventKind.Pass:
                    case SimEventKind.Cross:
                    case SimEventKind.Shot:
                    case SimEventKind.Clearance:
                    case SimEventKind.Tackle:
                    case SimEventKind.Foul:
                    case SimEventKind.Goal:
                        // 헤딩 패스·헤딩슛(부위 Head)은 공중볼 경합 결과 — 같은 팀이면 이어받은 것
                        if (e.Part == BodyPart.Head) return PassFate.Aerial;
                        return PassFate.Other;
                }
            }
            return PassFate.Other;
        }

        static bool AerialWonBy(SimEventLog log, int k)
        {
            var team = log[k].Team;
            for (int j = k + 1; j < log.Count; j++)
            {
                ref readonly var e = ref log[j];
                if (e.Kind == SimEventKind.Header || e.Part == BodyPart.Head) return e.Team == team;
            }
            return false;
        }

        static double Div(double a, double b) => b > 0 ? a / b : 0;

        /// <summary>FR-01 지표 (경기당 값·비율). 순서 고정 — 리포트 표 순서.</summary>
        public static List<KeyValuePair<string, double>> PerMatch(Totals t)
        {
            var n = t.Matches > 0 ? t.Matches : 1;
            var defActs = t.Tackles + t.Interceptions;
            return new List<KeyValuePair<string, double>>
            {
                new("goals", t.Goals / n),
                new("shots", t.Shots / n),
                new("passes", t.Passes / n),
                new("passSuccess", Div(t.Received, t.Passes)),
                new("takeOns", t.TakeOns / n),
                new("takeOnSuccess", Div(t.TakeOnOk, t.TakeOns)),
                new("tackles", t.Tackles / n),
                new("tackleSuccess", Div(t.TacklesWon, t.Tackles)),
                new("dribbleSteals", t.Steals / n),
                new("stealPerTouch", Div(t.Steals, t.Touches)),
                new("possessionChanges", t.Possessions / n),
                new("seqSeconds", Div(t.SeqTicks, t.SeqCount) / SimClock.TicksPerSecond),
                new("ppda", Div(t.Passes, defActs)),
                new("contacts", t.Contacts / n),
                new("onTargetRatio", Div(t.ShotOnTarget, t.Shots)),
                new("conversion", Div(t.ShotGoals, t.Shots)),
                new("xgPerShot", Div(t.ShotXg, t.Shots)),
                new("gkSaveRate", Div(t.ShotOnTarget - t.ShotGoals, t.ShotOnTarget)),
                new("crosses", t.Crosses / n),
                new("crossSuccess", Div(t.CrossOk, t.Crosses)),
                new("contests", t.Contests / n),
                new("contestsBothTeams", t.BothTeamContests / n),
                new("contestsEmpty", t.EmptyContests / n),
                new("headers", t.Headers / n),
                new("headerConversion", Div(t.HeaderGoals, t.HeaderShots)),
                new("goalsWithoutShot", (t.Goals - t.ShotGoals) / n),
                new("ownGoals", t.OwnGoals / n),
            };
        }

        /// <summary>패스 결과 분포 (패스 대비 비율, T04).</summary>
        public static List<KeyValuePair<string, double>> PassFateShares(Totals t)
        {
            var list = new List<KeyValuePair<string, double>>();
            for (int i = 0; i < t.PassFates.Length; i++) list.Add(new(((PassFate)i).ToString(), Div(t.PassFates[i], t.Passes)));
            return list;
        }

        public static string ToJson(Totals t, string label, ulong firstSeed, string commit)
        {
            var c = CultureInfo.InvariantCulture;
            var sb = new StringBuilder();
            sb.Append("{\n  \"label\": \"").Append(label).Append("\",\n  \"matches\": ").Append(((int)t.Matches).ToString(c))
              .Append(",\n  \"seeds\": \"").Append(firstSeed.ToString(c)).Append("+i\",\n  \"commit\": \"").Append(commit).Append("\",\n  \"perMatch\": {\n");
            var m = PerMatch(t);
            for (int i = 0; i < m.Count; i++) sb.Append("    \"").Append(m[i].Key).Append("\": ").Append(m[i].Value.ToString("0.####", c)).Append(i + 1 < m.Count ? ",\n" : "\n");
            sb.Append("  },\n  \"passFate\": {\n");
            var f = PassFateShares(t);
            for (int i = 0; i < f.Count; i++) sb.Append("    \"").Append(f[i].Key).Append("\": ").Append(f[i].Value.ToString("0.####", c)).Append(i + 1 < f.Count ? ",\n" : "\n");
            sb.Append("  }\n}\n");
            return sb.ToString();
        }
    }
}
