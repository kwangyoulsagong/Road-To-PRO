using System;
using System.Collections.Generic;
using RoadToPro.MatchEngine.Match.Events;

namespace RoadToPro.MatchEngine.Match.Flow
{
    public struct PenaltyOutcome
    {
        public bool Scored;
        /// <summary>이 킥에서 막는 팀 골키퍼가 다쳤다 (경계 상황).</summary>
        public bool KeeperInjured;
    }

    /// <summary>승부차기·페널티 킥 판정 (008·011 자리). 대체: 고정 확률.</summary>
    public interface IPenaltyJudge
    {
        PenaltyOutcome Kick(MatchContext ctx, int team, int kickerId, int keeperId);
    }

    public sealed class StandInPenaltyJudge : IPenaltyJudge
    {
        public PenaltyOutcome Kick(MatchContext ctx, int team, int kickerId, int keeperId) =>
            new PenaltyOutcome { Scored = ctx.FlowRng.Bernoulli(ctx.Params.PenaltyScoreProb) };
    }

    /// <summary>
    /// 승부차기 (specs/006 FR-15, AC-10). 5명씩 교대(선축은 동전), 수학적으로 결정되면 바로 끝, 이후 서든데스.
    /// 키커 순서: 팀 설정 순서, 없으면 경기장 위 선수 id 순. 모두 차면 처음부터 다시.
    /// GK 부상: 교체가 남았으면 벤치 첫 선수로 교체, 없으면 키커 목록 첫 선수가 GK를 맡는다.
    /// 한 틱 안에서 끝나므로 상태를 스냅샷에 넣지 않는다.
    /// </summary>
    public static class PenaltyShootout
    {
        public const int MaxRounds = 200; // 이벤트 대기열(틱당 512건) 안 (리뷰 R4)

        public static void Run(MatchContext ctx, IPenaltyJudge judge, int[] score)
        {
            var rounds = ctx.Setup.Rules.ShootoutRounds;
            var first = ctx.FlowRng.Bernoulli(0.5f) ? 0 : 1;
            var order = new[] { Order(ctx, 0), Order(ctx, 1) };
            var keeper = new[] { Keeper(ctx, 0, order[0]), Keeper(ctx, 1, order[1]) };
            var idx = new int[2];
            var taken = new int[2];
            score[0] = 0;
            score[1] = 0;
            for (int r = 0; r < MaxRounds; r++)
            {
                for (int k = 0; k < 2; k++)
                {
                    var t = k == 0 ? first : 1 - first;
                    var d = 1 - t;
                    if (order[t].Length == 0) { taken[t]++; continue; }
                    var kicker = order[t][idx[t] % order[t].Length];
                    idx[t]++;
                    var outcome = judge.Kick(ctx, t, kicker, keeper[d]);
                    taken[t]++;
                    if (outcome.Scored) score[t]++;
                    var e = SimEvent.Make(SimEventKind.ShootoutKick, t, kicker, keeper[d]);
                    e.Substep = SimEvent.TickEnd;
                    e.A = outcome.Scored ? 1 : 0;
                    e.B = r + 1;
                    ctx.Emit(e);
                    if (outcome.KeeperInjured) keeper[d] = ReplaceKeeper(ctx, d, keeper[d], order[d]);
                    if (Decided(r, rounds, taken, score)) { End(ctx, score); return; }
                }
            }
            // 사실상 도달 불가 (확률 0에 가까움): 동전으로 끝낸다
            score[ctx.FlowRng.Bernoulli(0.5f) ? 0 : 1]++;
            End(ctx, score);
        }

        static bool Decided(int round, int rounds, int[] taken, int[] score)
        {
            if (round < rounds)
            {
                // 남은 킥을 다 넣어도 못 따라잡으면 끝
                var rem0 = rounds - taken[0];
                var rem1 = rounds - taken[1];
                if (score[0] > score[1] + rem1) return true;
                if (score[1] > score[0] + rem0) return true;
                return false;
            }
            return taken[0] == taken[1] && score[0] != score[1];
        }

        static void End(MatchContext ctx, int[] score)
        {
            var e = SimEvent.Make(SimEventKind.ShootoutEnd, score[0] > score[1] ? 0 : 1);
            e.Substep = SimEvent.TickEnd;
            e.A = score[0];
            e.B = score[1];
            ctx.Emit(e);
        }

        static int[] Order(MatchContext ctx, int team)
        {
            var on = new List<int>();
            for (int s = team * Squad.SlotsPerTeam; s < (team + 1) * Squad.SlotsPerTeam; s++)
                if (ctx.Squad.IsSlotFilled(s)) on.Add(ctx.Squad.PlayerAt(s));
            var given = ctx.Setup.Team(team).PenaltyOrder;
            if (given != null && given.Length > 0)
            {
                var list = new List<int>();
                foreach (var id in given) if (on.Contains(id)) list.Add(id);
                if (list.Count > 0) return list.ToArray();
            }
            on.Sort();
            return on.ToArray();
        }

        static int Keeper(MatchContext ctx, int team, int[] order)
        {
            var gk = team * Squad.SlotsPerTeam;
            if (ctx.Squad.IsSlotFilled(gk)) return ctx.Squad.PlayerAt(gk);
            return order.Length > 0 ? order[0] : -1;
        }

        static int ReplaceKeeper(MatchContext ctx, int team, int keeperId, int[] order)
        {
            if (ctx.Flow.Subs.CanSubstitute(team, ctx.Setup.Rules))
            {
                foreach (var benchId in ctx.Setup.Team(team).Bench ?? Array.Empty<int>())
                {
                    if (ctx.Squad.StatusOf(benchId) != PlayerStatus.Bench) continue;
                    ctx.Flow.Subs.Enqueue(new SubstitutionRequest { Team = team, OutId = keeperId, InId = benchId });
                    if (ctx.Flow.Subs.Process(ctx, true, -1, false) == 1) return benchId;
                    break;
                }
            }
            return order.Length > 0 ? order[0] : keeperId;
        }
    }
}
