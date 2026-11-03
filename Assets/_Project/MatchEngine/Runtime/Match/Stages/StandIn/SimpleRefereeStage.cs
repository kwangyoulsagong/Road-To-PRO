using RoadToPro.Core.Math;
using RoadToPro.MatchEngine.Ball;
using RoadToPro.MatchEngine.Match.Events;
using RoadToPro.MatchEngine.Match.Flow;
using RoadToPro.MatchEngine.Match.Input;
using RoadToPro.MatchEngine.Match.Pipeline;

namespace RoadToPro.MatchEngine.Match.Stages.StandIn
{
    /// <summary>
    /// 단계 8 대체 (012 자리, FR-04): 물리 사건(FR-03, 서브스텝 순)으로 골·아웃·재시작을 판정하고, 예정 심판 사건
    /// (<see cref="RefereeScript"/>: 퇴장·부상·페널티·정지 시간·골 취소 — 012·027이 생기기 전 검증용 주입 훅)을 실행한다.
    /// 팀 인원이 7명 미만이 되면 012 FR-91 이벤트를 내고 경기 중단을 요청한다. 심판 3명의 목표 위치도 정한다.
    /// </summary>
    public sealed class SimpleRefereeStage : StatelessStage
    {
        public override StageId Id => StageId.Referee;

        bool _belowReported;

        public override void Execute(MatchContext ctx)
        {
            RunScript(ctx);
            var board = ctx.Board;
            var n = board.PhysicsCallCount;
            for (int i = 0; i < n; i++)
            {
                ref readonly var c = ref board.PhysicsCallAt(i);
                if (c.Kind == PhysicsCallKind.WoodworkHit)
                {
                    var w = SimEvent.Make(SimEventKind.WoodworkHit, c.LastTouchTeam, c.LastTouchSlot >= 0 ? ctx.PlayerAtSlot(c.LastTouchSlot) : -1);
                    w.Substep = c.Substep;
                    ctx.SetPosition(ref w, c.Position);
                    ctx.Emit(w);
                    continue;
                }
                if (!ctx.Flow.InPlay) continue;
                Boundary(ctx, in c);
                break; // 첫 라인 통과 뒤로는 데드볼
            }
            CheckMinimum(ctx);
            SetOfficials(ctx);
        }

        static void Boundary(MatchContext ctx, in PhysicsCall c)
        {
            var flow = ctx.Flow;
            var L = ctx.Pitch.LengthM;
            var W = ctx.Pitch.WidthM;
            var lastTeam = c.LastTouchTeam < 0 ? 0 : c.LastTouchTeam;
            if (flow.PenaltyKickTick >= 0) flow.ClearPenalty();
            if (c.Boundary == BallBoundary.GoalLineLow || c.Boundary == BallBoundary.GoalLineHigh)
            {
                var lineX = c.Boundary == BallBoundary.GoalLineLow ? 0f : L;
                var defending = (ctx.AttackDir(0) > 0) == (lineX == 0f) ? 0 : 1;
                var attacking = 1 - defending;
                if (c.InGoalMouth)
                {
                    var own = lastTeam != attacking;
                    var g = SimEvent.Make(SimEventKind.Goal, attacking, c.LastTouchSlot >= 0 ? ctx.PlayerAtSlot(c.LastTouchSlot) : -1);
                    g.Substep = c.Substep;
                    g.A = own ? 1 : 0;
                    ctx.SetPosition(ref g, c.Position);
                    ctx.Emit(g);
                    flow.OnGoal(attacking);
                    return;
                }
                var inward = lineX == 0f ? 1f : -1f;
                if (lastTeam == defending)
                {
                    var spot = new Vec2(lineX + inward * 0.5f, c.Position.Y < W * 0.5f ? 0.5f : W - 0.5f);
                    Restart(ctx, SimEventKind.Corner, RestartKind.Corner, attacking, spot, c.Substep);
                }
                else
                {
                    var spot = new Vec2(lineX + inward * 5.5f, W * 0.5f);
                    Restart(ctx, SimEventKind.GoalKick, RestartKind.GoalKick, defending, spot, c.Substep);
                }
                return;
            }
            var y = c.Boundary == BallBoundary.TouchlineLow ? 0f : W;
            var x = c.Position.X < 0.5f ? 0.5f : (c.Position.X > L - 0.5f ? L - 0.5f : c.Position.X);
            Restart(ctx, SimEventKind.ThrowIn, RestartKind.ThrowIn, 1 - lastTeam, new Vec2(x, y), c.Substep);
        }

        static void Restart(MatchContext ctx, SimEventKind kind, RestartKind rk, int team, Vec2 spot, sbyte substep)
        {
            var e = SimEvent.Make(kind, team);
            e.Substep = substep;
            ctx.SetPosition(ref e, spot);
            ctx.Emit(e);
            ctx.Flow.BeginRestart(rk, team, spot);
        }

        static void RunScript(MatchContext ctx)
        {
            var script = ctx.RefereeScript;
            for (int i = script.FirstAt(ctx.Tick); i < script.Count && script[i].Tick == ctx.Tick; i++)
            {
                var a = script[i];
                var flow = ctx.Flow;
                var sq = ctx.Squad;
                switch (a.Kind)
                {
                    case RefereeActionKind.SendOff:
                    case RefereeActionKind.Booking:
                    {
                        if (!sq.Has(a.Target) || sq.StatusOf(a.Target) != PlayerStatus.OnPitch) break;
                        var slot = sq.SlotOf(a.Target);
                        var e = SimEvent.Make(SimEventKind.Card, sq.TeamOf(a.Target), a.Target);
                        e.Substep = SimEvent.TickEnd; // 물리 사건 뒤 (리뷰 R6)
                        e.A = a.Kind == RefereeActionKind.SendOff ? EventCodes.CardRed : EventCodes.CardYellow;
                        ctx.SetPosition(ref e, ctx.SlotState(slot).Position);
                        ctx.Emit(e);
                        if (a.Kind == RefereeActionKind.SendOff) flow.RemoveFromPitch(slot, PlayerStatus.SentOff);
                        break;
                    }
                    case RefereeActionKind.Injury:
                    {
                        if (!sq.Has(a.Target) || sq.StatusOf(a.Target) != PlayerStatus.OnPitch) break;
                        var team = sq.TeamOf(a.Target);
                        var slot = sq.SlotOf(a.Target);
                        var e = SimEvent.Make(SimEventKind.Injury, team, a.Target);
                        e.Substep = SimEvent.TickEnd; // 물리 사건 뒤 (리뷰 R6)
                        e.A = a.Code;
                        e.B = (int)a.Ticks;
                        ctx.SetPosition(ref e, ctx.SlotState(slot).Position);
                        ctx.Emit(e);
                        var possession = ctx.LastTouchTeam < 0 ? team : ctx.LastTouchTeam;
                        flow.BeginRestart(RestartKind.DropBall, possession, ctx.Ball.Position.XY);
                        flow.AddStoppage(EventCodes.StopInjury, a.Ticks);
                        var bench = FirstBench(ctx, team);
                        if (bench >= 0)
                            flow.RequestSubstitution(new SubstitutionRequest { Team = team, OutId = a.Target, InId = bench, Concussion = a.Code == 1, InjuryReplacement = true });
                        else flow.RemoveFromPitch(slot, PlayerStatus.InjuredOff);
                        break;
                    }
                    case RefereeActionKind.AwardPenalty:
                    {
                        var team = a.Target;
                        var g = ctx.OpponentGoal(team);
                        var spot = new Vec2(g.X - ctx.AttackDir(team) * 11f, g.Y);
                        var e = SimEvent.Make(SimEventKind.Penalty, team);
                        e.Substep = SimEvent.TickEnd; // 물리 사건 뒤 (리뷰 R6)
                        ctx.SetPosition(ref e, spot);
                        ctx.Emit(e);
                        flow.BeginRestart(RestartKind.Penalty, team, spot);
                        break;
                    }
                    case RefereeActionKind.Stoppage:
                    {
                        var e = SimEvent.Make(SimEventKind.Stoppage);
                        e.Substep = SimEvent.TickEnd; // 물리 사건 뒤 (리뷰 R6)
                        e.A = a.Code;
                        e.B = (int)a.Ticks;
                        ctx.Emit(e);
                        flow.AddStoppage(a.Code, a.Ticks);
                        break;
                    }
                    case RefereeActionKind.DisallowLastGoal:
                    {
                        var team = a.Target;
                        var e = SimEvent.Make(SimEventKind.VarOverturn, team);
                        e.Substep = SimEvent.TickEnd; // 물리 사건 뒤 (리뷰 R6)
                        ctx.Emit(e);
                        flow.DisallowGoal(team);
                        var defending = 1 - team;
                        var own = ctx.OwnGoal(defending);
                        flow.BeginRestart(RestartKind.FreeKick, defending, new Vec2(own.X + ctx.AttackDir(defending) * 16f, own.Y));
                        break;
                    }
                }
            }
        }

        static int FirstBench(MatchContext ctx, int team)
        {
            var bench = ctx.Setup.Team(team).Bench;
            if (bench == null) return -1;
            foreach (var id in bench)
                if (ctx.Squad.StatusOf(id) == PlayerStatus.Bench) return id;
            return -1;
        }

        void CheckMinimum(MatchContext ctx)
        {
            if (_belowReported) return;
            for (int t = 0; t < 2; t++)
            {
                if (ctx.Squad.OnPitchCount(t) >= 7) continue;
                var e = SimEvent.Make(SimEventKind.PlayersBelowMinimum, t);
                        e.Substep = SimEvent.TickEnd; // 물리 사건 뒤 (리뷰 R6)
                e.A = ctx.Squad.OnPitchCount(t);
                ctx.Emit(e);
                _belowReported = true;
            }
            if (_belowReported) ctx.Flow.RequestAbandon();
        }

        static void SetOfficials(MatchContext ctx)
        {
            var b = ctx.Ball.Position.XY;
            var L = ctx.Pitch.LengthM;
            var W = ctx.Pitch.WidthM;
            var rx = b.X - 8f; if (rx < 2f) rx = b.X + 8f;
            var ry = b.Y - 6f; if (ry < 2f) ry = b.Y + 6f;
            ctx.Officials.SetTarget(0, new Vec2(rx, ry));
            ctx.Officials.SetTarget(1, new Vec2(b.X < L * 0.5f ? L * 0.5f : (b.X > L ? L : b.X), -1.5f));
            ctx.Officials.SetTarget(2, new Vec2(b.X > L * 0.5f ? L * 0.5f : (b.X < 0f ? 0f : b.X), W + 1.5f));
        }

        public override void WriteState(Core.Serialization.StateWriter w) => w.Write(_belowReported);
        public override void ReadState(Core.Serialization.StateReader r) => _belowReported = r.ReadBool();
        public override void AddTo(ref Core.Hashing.StableHasher h) => h.Add(_belowReported);
    }
}
