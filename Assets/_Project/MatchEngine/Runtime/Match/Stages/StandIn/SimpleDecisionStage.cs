using RoadToPro.Core.Hashing;
using RoadToPro.Core.Math;
using RoadToPro.Core.Serialization;
using RoadToPro.MatchEngine.Ball;
using RoadToPro.MatchEngine.Match.Events;
using RoadToPro.MatchEngine.Match.Flow;
using RoadToPro.MatchEngine.Match.Input;
using RoadToPro.MatchEngine.Match.Pipeline;

namespace RoadToPro.MatchEngine.Match.Stages.StandIn
{
    /// <summary>시나리오 배역 선수의 결정을 대신 낸다 (specs/013). 없으면 폴백 판단 그대로다.</summary>
    public interface IDecisionOverride
    {
        bool IsCast(int slot);
        bool TryDecide(MatchContext ctx, int slot, ref bool hasAction, out Decision decision);
    }

    /// <summary>
    /// 단계 5 대체 (013 시나리오 · 010 Utility 자리, FR-04): "22명 위치 + 공 + 단순 패스/슈팅"(기획서 § 9 M0).
    /// 결정 대상(<see cref="MatchContext.DecisionList"/>, 오름차순)만 새로 정하고 나머지는 직전 의도를 유지한다 (FR-05).
    /// 공 소유자는 보유 시간이 지나면 사거리 안에서 확률로 슈팅, 아니면 앞쪽 동료에게 땅볼 패스(004 역산), 없으면 드리블.
    /// 소유 팀 상대의 가장 가까운 선수는 압박·태클, 공이 느슨하면 팀마다 가장 가까운 선수가 쫓는다. 나머지는 대형.
    /// 외부 입력(FR-62)은 그 틱에 대상 선수를 결정 대상으로 넣어 적용한다. 공 동작은 틱당 하나(먼저 정한 것).
    /// </summary>
    public sealed class SimpleDecisionStage : IMatchStage
    {
        public StageId Id => StageId.ScenarioOrUtility;

        readonly IDecisionOverride _override;

        public SimpleDecisionStage(IDecisionOverride decisionOverride = null) => _override = decisionOverride;

        readonly Decision[] _intent = new Decision[Squad.Slots];
        readonly int[] _list = new int[Squad.Slots];
        readonly InputKind[] _input = new InputKind[Squad.Slots];
        readonly int[] _chaser = new int[2];
        int _presser;
        long _restartKickTick = long.MinValue / 2;
        const long RestartRetryTicks = 10;

        public void Execute(MatchContext ctx)
        {
            var board = ctx.Board;
            System.Array.Clear(_input, 0, _input.Length);
            var n = MergeInputs(ctx);
            ComputeChasers(ctx);
            var hasAction = false;
            for (int i = 0; i < n; i++)
            {
                var s = _list[i];
                if (!ctx.IsSlotActive(s)) continue;
                Decision d;
                if (_override == null || _input[s] != default || !_override.TryDecide(ctx, s, ref hasAction, out d))
                    d = Decide(ctx, s, ref hasAction);
                _intent[s] = d;
                board.MarkDecided(s);
            }
            for (int s = 0; s < Squad.Slots; s++) board.SetDecision(s, in _intent[s]);
        }

        int MergeInputs(MatchContext ctx)
        {
            var n = ctx.DecisionCount;
            for (int i = 0; i < n; i++) _list[i] = ctx.DecisionList[i];
            var inputs = ctx.Inputs;
            for (int i = inputs.FirstAtOrAfter(ctx.Tick); i < inputs.Count && inputs[i].Tick == ctx.Tick; i++)
            {
                var inp = inputs[i];
                if (inp.Kind == InputKind.Substitute || inp.Kind == InputKind.SubstituteConcussion) continue; // MatchSim이 틱 시작에 처리
                if (inp.Kind == InputKind.TeamInstruction || inp.Kind == InputKind.TeamFormation) continue; // PlayerId가 선수가 아니다 (009 TacticsStage)
                if (!ctx.Squad.Has(inp.PlayerId)) continue;
                var slot = ctx.Squad.SlotOf(inp.PlayerId);
                if (slot < 0 || !ctx.IsSlotActive(slot)) continue;
                _input[slot] = inp.Kind;
                var e = SimEvent.Make(SimEventKind.InputApplied, Squad.TeamOfSlot(slot), inp.PlayerId);
                e.A = (int)inp.Kind;
                e.F0 = inp.Value;
                ctx.Emit(e);
                // 오름차순 삽입 (중복 없음)
                int j = 0;
                while (j < n && _list[j] < slot) j++;
                if (j < n && _list[j] == slot) continue;
                for (int m = n; m > j; m--) _list[m] = _list[m - 1];
                _list[j] = slot;
                n++;
            }
            return n;
        }

        void ComputeChasers(MatchContext ctx)
        {
            ref readonly var ball = ref ctx.Ball;
            var target = ball.Position.XY + ball.Velocity.XY * 0.3f;
            _presser = -1;
            for (int t = 0; t < 2; t++)
            {
                _chaser[t] = -1;
                float bd = float.MaxValue;
                var nearOwnGoal = (target - ctx.OwnGoal(t)).LengthSq() < 18f * 18f;
                for (int s = t * Squad.SlotsPerTeam; s < (t + 1) * Squad.SlotsPerTeam; s++)
                {
                    if (!ctx.IsSlotActive(s)) continue;
                    if (_override != null && _override.IsCast(s)) continue;
                    if (Squad.IsKeeperSlot(s) && !nearOwnGoal) continue;
                    var d = (ctx.SlotState(s).Position - target).LengthSq();
                    if (d < bd) { bd = d; _chaser[t] = s; }
                }
            }
            if (ball.Phase == BallPhase.Controlled && ball.OwnerId >= 0)
                _presser = _chaser[1 - Squad.TeamOfSlot(ball.OwnerId)];
        }

        Decision Decide(MatchContext ctx, int s, ref bool hasAction)
        {
            var flow = ctx.Flow;
            ref var ball = ref ctx.Ball;
            var p = ctx.Params;
            var team = Squad.TeamOfSlot(s);
            var me = ctx.SlotState(s);

            if (_input[s] == InputKind.HoldPosition) return new Decision { Kind = DecisionKind.Stand, TargetSlot = -1 };

            if (flow.State == FlowState.DeadBall)
            {
                if (s != flow.RestartTaker)
                    return flow.Restart == RestartKind.Kickoff
                        ? new Decision { Kind = DecisionKind.Stand, TargetSlot = -1 }
                        : new Decision { Kind = DecisionKind.HoldShape, TargetSlot = -1 };
                if (!(ball.Phase == BallPhase.Controlled && ball.OwnerId == s))
                {
                    if ((me.Position - flow.RestartSpot).LengthSq() <= p.ClaimRadius * p.ClaimRadius)
                    {
                        BallCommands.TakeControl(ref ball, s, new Vec3(flow.RestartSpot.X, flow.RestartSpot.Y, ctx.BallParams.Radius), ctx.BallEvents);
                        ctx.PossessionStartTick = ctx.Tick;
                        return new Decision { Kind = DecisionKind.Stand, TargetSlot = -1 };
                    }
                    return new Decision { Kind = DecisionKind.MoveTo, Point = flow.RestartSpot, TargetSlot = -1 };
                }
                if (ctx.Tick >= flow.ReadyTick && !hasAction && ctx.Tick - _restartKickTick >= RestartRetryTicks)
                {
                    hasAction = true;
                    _restartKickTick = ctx.Tick; // 물리 단계가 다음 서브스텝 0에 적용 — 적용 못 됐을 때만 1초 뒤 다시
                    if (flow.Restart == RestartKind.Penalty) Shoot(ctx, s, team, true, true);
                    else if (!Pass(ctx, s, team, true, flow.Restart == RestartKind.Kickoff)) ClearForward(ctx, s, team);
                }
                return new Decision { Kind = DecisionKind.Stand, TargetSlot = -1 };
            }
            if (flow.State != FlowState.InPlay) return new Decision { Kind = DecisionKind.Stand, TargetSlot = -1 };

            // 공 소유자
            if (ball.Phase == BallPhase.Controlled && ball.OwnerId == s)
            {
                var goal = ctx.OpponentGoal(team);
                if (!hasAction)
                {
                    if (_input[s] == InputKind.ShootNow) { hasAction = true; Shoot(ctx, s, team, false, false); return Stand(); }
                    if (ctx.Tick - ctx.PossessionStartTick >= p.HoldTicks)
                    {
                        var dist = (goal - me.Position).Length();
                        if (dist < p.ShotRange && ctx.DecisionRng.Bernoulli(p.ShotProb)) { hasAction = true; Shoot(ctx, s, team, false, false); return Stand(); }
                        if (Pass(ctx, s, team, false, false)) { hasAction = true; return Stand(); }
                    }
                }
                var carryTo = new Vec2(goal.X - ctx.AttackDir(team) * 12f, me.Position.Y + (goal.Y - me.Position.Y) * 0.3f); // 박스 앞에서 멈춘다
                return new Decision { Kind = DecisionKind.Carry, Point = carryTo, TargetSlot = -1, MaxSpeed = ctx.Kinematics.Profile(s).MaxSpeed * p.CarrySpeedRatio };
            }

            if (_input[s] == InputKind.RunInBehind)
            {
                var g = ctx.OpponentGoal(team);
                var dir = ctx.AttackDir(team);
                var x = me.Position.X + dir * 20f;
                if (x < 2f) x = 2f;
                if (x > ctx.Pitch.LengthM - 2f) x = ctx.Pitch.LengthM - 2f;
                return new Decision { Kind = DecisionKind.MoveTo, Point = new Vec2(x, me.Position.Y + (g.Y - me.Position.Y) * 0.2f), TargetSlot = -1 };
            }

            // 압박·태클
            if (s == _presser)
            {
                var owner = ball.OwnerId;
                var od = (ctx.SlotState(owner).Position - me.Position).LengthSq();
                if (!hasAction && od <= p.TackleRange * p.TackleRange && ctx.DecisionRng.Bernoulli(p.TackleProb))
                {
                    hasAction = true;
                    var away = ctx.SlotState(owner).Position - me.Position;
                    var dir = away.Normalized();
                    if (dir == Vec2.Zero) dir = new Vec2(ctx.AttackDir(team), 0f);
                    ctx.Board.SetBallAction(new BallAction
                    {
                        Kind = BallActionKind.Clearance, KickerSlot = s, TargetSlot = -1,
                        Velocity = new Vec3(dir.X * 6f, dir.Y * 6f, 0f), Part = PhysicsStage.PartFor(in me, ball.Position),
                    });
                    return Stand();
                }
                return new Decision { Kind = DecisionKind.Chase, TargetSlot = owner };
            }

            // 느슨한 공
            if (ball.Phase != BallPhase.Controlled && s == _chaser[team])
            {
                var target = ball.Position.XY + ball.Velocity.XY * 0.3f;
                return new Decision { Kind = DecisionKind.Chase, Point = target, TargetSlot = -1 };
            }
            return new Decision { Kind = DecisionKind.HoldShape, TargetSlot = -1 };
        }

        static Decision Stand() => new Decision { Kind = DecisionKind.Stand, TargetSlot = -1 };

        static void Shoot(MatchContext ctx, int s, int team, bool restart, bool penalty)
        {
            var me = ctx.SlotState(s);
            var goal = ctx.OpponentGoal(team);
            var half = BallParams.GoalWidth * 0.5f - 0.4f;
            var ty = goal.Y + ctx.DecisionRng.Range(-half, half);
            var d = new Vec2(goal.X, ty) - ctx.Ball.Position.XY;
            var dir = d.Normalized();
            var speed = ctx.Params.ShotSpeed;
            var vz = ctx.DecisionRng.Range(0.3f, 3.0f);
            ctx.Board.SetBallAction(new BallAction
            {
                Kind = BallActionKind.Shot, KickerSlot = s, TargetSlot = -1, Velocity = new Vec3(dir.X * speed, dir.Y * speed, vz),
                Part = PhysicsStage.PartFor(in me, ctx.Ball.Position), IsRestart = restart, IsPenalty = penalty,
            });
        }

        /// <summary>앞쪽(진행 방향) 이득 − 거리 비용 + 잡음이 가장 큰 동료에게 땅볼 패스. 킥오프는 아무 방향이나 가까운 동료.</summary>
        static bool Pass(MatchContext ctx, int s, int team, bool restart, bool kickoff)
        {
            var me = ctx.SlotState(s);
            var p = ctx.Params;
            var dir = ctx.AttackDir(team);
            int best = -1;
            float bestScore = float.MinValue, bestDist = 0f;
            for (int o = team * Squad.SlotsPerTeam; o < (team + 1) * Squad.SlotsPerTeam; o++)
            {
                if (o == s || !ctx.IsSlotActive(o)) continue;
                var op = ctx.SlotState(o).Position;
                var dist = (op - me.Position).Length();
                if (dist < 4f || dist > p.PassRange) continue;
                var gain = (op.X - me.Position.X) * dir;
                var score = (kickoff ? -dist : gain - 0.2f * dist) + ctx.DecisionRng.Range(0f, 8f);
                if (score > bestScore) { bestScore = score; best = o; bestDist = dist; }
            }
            if (best < 0) return false;
            var tp = ctx.SlotState(best).Position;
            var inv = BallInverse.GroundSpeedFor(ctx.BallParams, bestDist, p.PassExtraSpeed);
            var speed = inv.Status == BallInverseStatus.Ok ? inv.Speed : DMath.Min(bestDist * 0.9f + 5f, 25f);
            var u = (tp - ctx.Ball.Position.XY).Normalized();
            ctx.Board.SetBallAction(new BallAction
            {
                Kind = BallActionKind.Pass, KickerSlot = s, TargetSlot = best, Velocity = new Vec3(u.X * speed, u.Y * speed, 0f),
                Part = PhysicsStage.PartFor(in me, ctx.Ball.Position), IsRestart = restart, IsKickoff = kickoff,
            });
            return true;
        }

        static void ClearForward(MatchContext ctx, int s, int team)
        {
            var me = ctx.SlotState(s);
            var dir = ctx.AttackDir(team);
            ctx.Board.SetBallAction(new BallAction
            {
                Kind = BallActionKind.Clearance, KickerSlot = s, TargetSlot = -1, Velocity = new Vec3(dir * 18f, 0f, 8f),
                Part = PhysicsStage.PartFor(in me, ctx.Ball.Position), IsRestart = true,
            });
        }

        public void WriteState(StateWriter w)
        {
            w.Write(_restartKickTick);
            for (int s = 0; s < Squad.Slots; s++)
            {
                w.Write((byte)_intent[s].Kind); w.Write(_intent[s].Point); w.Write(_intent[s].TargetSlot); w.Write(_intent[s].MaxSpeed);
            }
        }

        public void ReadState(StateReader r)
        {
            _restartKickTick = r.ReadLong();
            for (int s = 0; s < Squad.Slots; s++)
                _intent[s] = new Decision { Kind = (DecisionKind)r.ReadByte(), Point = r.ReadVec2(), TargetSlot = r.ReadInt(), MaxSpeed = r.ReadFloat() };
        }

        public void AddTo(ref StableHasher h)
        {
            h.Add(_restartKickTick);
            for (int s = 0; s < Squad.Slots; s++)
            {
                h.Add((byte)_intent[s].Kind); h.Add(_intent[s].Point.X); h.Add(_intent[s].Point.Y); h.Add(_intent[s].TargetSlot); h.Add(_intent[s].MaxSpeed);
            }
        }
    }
}
