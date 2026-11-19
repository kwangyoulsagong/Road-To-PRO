using RoadToPro.Core.Math;
using RoadToPro.MatchEngine.Ball;
using RoadToPro.MatchEngine.Match.Events;
using RoadToPro.MatchEngine.Match.Flow;
using RoadToPro.MatchEngine.Match.Input;
using RoadToPro.MatchEngine.Match.Pipeline;
using RoadToPro.MatchEngine.Referee;

namespace RoadToPro.MatchEngine.Match.Stages
{
    /// <summary>
    /// 단계 8 공용 처리 (specs/012 T18): 대체 단계(<see cref="StandIn.SimpleRefereeStage"/>)와 실제 심판 단계(<see cref="RefereeStage"/>)가 같이 쓴다.
    /// 예정 사건 스크립트 실행, 골 이벤트, 라인 아웃 재시작(<see cref="RestartRules.Out"/>), 7명 미만, 골대 이벤트. 동작은 대체 단계 그대로 (해시 불변).
    /// </summary>
    internal static class RefereeCommon
    {
        public static void EmitWoodwork(MatchContext ctx, in PhysicsCall c)
        {
            var w = SimEvent.Make(SimEventKind.WoodworkHit, c.LastTouchTeam, c.LastTouchSlot >= 0 ? ctx.PlayerAtSlot(c.LastTouchSlot) : -1);
            w.Substep = c.Substep;
            ctx.SetPosition(ref w, c.Position);
            ctx.Emit(w);
        }

        /// <summary>골 이벤트 + 흐름 (대체 단계와 같은 데이터). 득점 팀을 돌려준다.</summary>
        public static int EmitGoal(MatchContext ctx, in PhysicsCall c)
        {
            var L = ctx.Pitch.LengthM;
            var lastTeam = c.LastTouchTeam < 0 ? 0 : c.LastTouchTeam;
            var lineX = c.Boundary == BallBoundary.GoalLineLow ? 0f : L;
            var defending = (ctx.AttackDir(0) > 0) == (lineX == 0f) ? 0 : 1;
            var attacking = 1 - defending;
            var own = lastTeam != attacking;
            var g = SimEvent.Make(SimEventKind.Goal, attacking, c.LastTouchSlot >= 0 ? ctx.PlayerAtSlot(c.LastTouchSlot) : -1);
            g.Substep = c.Substep;
            g.A = own ? 1 : 0;
            ctx.Keepers.Watcher.ConsumeGoal(defending, out var gkFlags, out var psxg); // 011 FR-80·92: 손끝 닿음·PSxG
            g.B |= gkFlags;
            g.F1 = psxg;
            ctx.SetPosition(ref g, c.Position);
            ctx.Emit(g);
            ctx.Flow.OnGoal(attacking, c.LastTouchSlot);
            return attacking;
        }

        public static bool IsGoal(in PhysicsCall c) =>
            (c.Boundary == BallBoundary.GoalLineLow || c.Boundary == BallBoundary.GoalLineHigh) && c.InGoalMouth;

        /// <summary>라인 아웃 재시작 결정 (득점 아님).</summary>
        public static RestartDecision OutDecision(MatchContext ctx, in PhysicsCall c) =>
            RestartRules.Out(c.Boundary, c.Position.XY, c.LastTouchTeam, ctx.AttackDir(0), ctx.Pitch.LengthM, ctx.Pitch.WidthM);

        public static SimEventKind EventOf(RestartKind k) => k switch
        {
            RestartKind.ThrowIn => SimEventKind.ThrowIn,
            RestartKind.GoalKick => SimEventKind.GoalKick,
            RestartKind.Corner => SimEventKind.Corner,
            RestartKind.FreeKick => SimEventKind.FreeKick,
            RestartKind.Penalty => SimEventKind.Penalty,
            _ => SimEventKind.None,
        };

        /// <summary>재시작 이벤트 + 흐름. 프리킥이면 A = 1 직접 · 2 간접 (대체 단계 이벤트는 A = 0 그대로).</summary>
        public static void Restart(MatchContext ctx, in RestartDecision d, sbyte substep, bool markDirect)
        {
            var kind = EventOf(d.Kind);
            if (kind != SimEventKind.None)
            {
                var e = SimEvent.Make(kind, d.Team);
                e.Substep = substep;
                if (markDirect && d.Kind == RestartKind.FreeKick) e.A = d.Direct ? 1 : 2;
                ctx.SetPosition(ref e, d.Spot);
                ctx.Emit(e);
            }
            ctx.Flow.BeginRestart(d.Kind, d.Team, d.Spot);
        }

        public static void RunScript(MatchContext ctx)
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

        public static int FirstBench(MatchContext ctx, int team)
        {
            var bench = ctx.Setup.Team(team).Bench;
            if (bench == null) return -1;
            foreach (var id in bench)
                if (ctx.Squad.StatusOf(id) == PlayerStatus.Bench) return id;
            return -1;
        }

        /// <summary>FR-91: 7명 미만이면 이벤트 1개(경기당 한 번) + 중단 요청. 이미 보고했으면 그대로 true.</summary>
        public static bool CheckMinimum(MatchContext ctx, bool reported)
        {
            if (reported) return true;
            for (int t = 0; t < 2; t++)
            {
                if (ctx.Squad.OnPitchCount(t) >= 7) continue;
                var e = SimEvent.Make(SimEventKind.PlayersBelowMinimum, t);
                e.Substep = SimEvent.TickEnd; // 물리 사건 뒤 (리뷰 R6)
                e.A = ctx.Squad.OnPitchCount(t);
                ctx.Emit(e);
                reported = true;
            }
            if (reported) ctx.Flow.RequestAbandon();
            return reported;
        }
    }
}
