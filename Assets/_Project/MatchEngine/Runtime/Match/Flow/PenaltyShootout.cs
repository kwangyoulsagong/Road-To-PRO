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
        /// <summary>074: GK 방향(−1·0·+1)·조기 시각 t_e(s)·기울기 l (대체 판정은 0).</summary>
        public int KeeperDir;
        public float Lead, Lean;
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
    /// 074 승부차기 판정 (FR-01·04): 경기 중 PK와 같은 모델을 한 틱 안에서 부른다 — 키커 셀·실행 오차(071, 압박 배수 = 071 extraSigmaMul 자리, 액션 RNG),
    /// GK 전략(<see cref="Goalkeeper.PenaltyKeeperModel"/>, 승부차기 압박 배수·이전 방향 기억, GK RNG), 004 경로 예측 + 011 선방 판정(피로 누적 v_dive).
    /// 공을 실제로 굴리지 않는다. RNG 순서(킥마다): 액션 자연 측 1 → 액션 실행 오차 → GK 기울기·t_e·라인 이탈 → GK 선방 결과.
    /// 기억·피로는 승부차기 한 번(같은 틱) 안에서만 — 팀(막는 쪽) 기준, GK가 바뀌면 피로는 0부터.
    /// 010 파이프라인(<c>Duels.Enabled</c>)이 아니면 대체 판정과 같다(고정 확률, FlowRng 1회 — 006 해시 불변).
    /// </summary>
    public sealed class SimPenaltyJudge : IPenaltyJudge
    {
        long _tick = long.MinValue;
        readonly Goalkeeper.ShootoutKeeperMemory[] _mem = new Goalkeeper.ShootoutKeeperMemory[2];
        readonly int[] _keeperId = { int.MinValue, int.MinValue };

        /// <summary>테스트용: 막는 팀의 승부차기 기억 (읽기 전용 사본).</summary>
        public Goalkeeper.ShootoutKeeperMemory Memory(int team) => _mem[team];

        public PenaltyOutcome Kick(MatchContext ctx, int team, int kickerId, int keeperId)
        {
            if (!ctx.Actions.Duels.Enabled) return new PenaltyOutcome { Scored = ctx.FlowRng.Bernoulli(ctx.Params.PenaltyScoreProb) };
            if (ctx.Tick != _tick)
            {
                _tick = ctx.Tick;
                _mem[0] = default; _mem[1] = default;
                _keeperId[0] = int.MinValue; _keeperId[1] = int.MinValue;
            }
            var d = 1 - team;
            if (_keeperId[d] != keeperId) { _keeperId[d] = keeperId; _mem[d].Dives = 0; } // GK 교체: 피로는 새로, 방향 기억은 팀 것
            var ks = SlotOf(ctx, kickerId);
            if (ks < 0) return new PenaltyOutcome { Scored = ctx.FlowRng.Bernoulli(ctx.Params.PenaltyScoreProb) };
            var act = ctx.Actions;
            act.Refresh(ctx);
            var w = act.World;
            var keepers = ctx.Keepers;
            var gp = keepers.Params;
            var goal = ctx.OwnGoal(d);
            var spot = Referee.RestartRules.PenaltySpot(goal, ctx.AttackDir(team));
            var from = new Core.Math.Vec3(spot.X, spot.Y, ctx.BallParams.Radius);
            // 키커 (071 FR-17 + 074 FR-04 압박 배수)
            var speed = Actions.ShotExecution.SpeedFor(act.P, act.P.PenPower);
            var natural = act.Rng.NextFloat() < act.P.PenNaturalP;
            var cell = act.Shots.PenaltyCell(ctx, ks, team, goal, spot, speed, natural, 0f);
            var mulK = Goalkeeper.PenaltyKeeperModel.PressureMul(gp, true, Goalkeeper.PenaltyKeeperModel.BigMatchN(w.BigMatchLevel[ks]), w.Composure[ks]);
            var offY = act.Shots.Aim.OffsetY(cell);
            var intended = Actions.ShotExecution.Intended(from, new Core.Math.Vec2(goal.X, goal.Y + offY), act.Shots.Aim.Height(cell), speed, ctx.BallParams.Gravity);
            var sigma = Actions.ShotExecution.SigmaAimDeg(act.P, w.Finishing[ks], speed, 0f, w.Composure[ks], 1f, false, 1f, mulK);
            var v = Actions.ShotExecution.Apply(intended, sigma, act.Rng, out _, out _);
            // GK (074 FR-01·04)
            var prof = keepers.Profile(ctx, d);
            var gs = d * Squad.SlotsPerTeam;
            var bigG = ctx.IsSlotActive(gs) ? Goalkeeper.PenaltyKeeperModel.BigMatchN(w.BigMatchLevel[gs]) : 0f;
            var mulG = Goalkeeper.PenaltyKeeperModel.PressureMul(gp, true, bigG, prof.ComposureN);
            var model = keepers.PenaltyModel;
            var naturalSign = Goalkeeper.PenaltyKeeperModel.NaturalSign(w.PreferredFoot[ks], ctx.AttackDir(team));
            var s = model.Draw(naturalSign, _mem[d].Balance, mulG, keepers.Rng);
            var tired = model.Fatigued(in prof, _mem[d].Dives);
            var save = keepers.Save;
            var ball = Ball.BallState.InAir(from, v, Core.Math.Vec3.Zero);
            var dirIn = (float)ctx.AttackDir(d);
            var scored = false;
            if (save.Predict(in ball, ctx.BallParams, goal, dirIn))
            {
                var sctx = new Goalkeeper.SaveContext { Goal = goal, DirIn = dirIn, CommitDir = s.Dir, CommitLead = s.Lead };
                var gk = new Goalkeeper.KeeperState { Position = goal };
                var j = save.Evaluate(in gk, in tired, in sctx);
                var o = save.Resolve(in j, in tired, in sctx, keepers.Rng);
                scored = o.Kind == Goalkeeper.SaveOutcomeKind.Miss;
            }
            _mem[d].Record(offY > 0f ? 1 : (offY < 0f ? -1 : 0), s.Dir);
            return new PenaltyOutcome { Scored = scored, KeeperDir = s.Dir, Lead = s.Lead, Lean = s.Lean };
        }

        static int SlotOf(MatchContext ctx, int playerId)
        {
            for (int sl = 0; sl < Squad.Slots; sl++)
                if (ctx.Squad.IsSlotFilled(sl) && ctx.Squad.PlayerAt(sl) == playerId && ctx.IsSlotActive(sl)) return sl;
            return -1;
        }
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
                    e.F0 = outcome.Lead; // 074: GK 조기 시각 · 기울기 (대체 판정은 0 — 해시 불변)
                    e.F1 = outcome.Lean;
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
