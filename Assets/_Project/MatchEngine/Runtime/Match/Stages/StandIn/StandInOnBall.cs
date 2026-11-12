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
    /// <summary>
    /// 대체 결정 경로 (specs/006 대체 단계 5에서 추출, specs/010 T11): 외부 입력 병합, 데드볼·골 후·재시작, 공 소유자(보유·슈팅·패스·드리블 — 072 자리),
    /// 압박·태클(070 자리), 느슨한 공 추격, GK 결정 위임(011). <see cref="SimpleDecisionStage"/>(006 대체 파이프라인)와 UtilityDecisionStage(010)가 같이 쓴다.
    /// 동작은 추출 전과 같다(같은 RNG 순서·같은 해시).
    /// </summary>
    public sealed class StandInOnBall
    {
        readonly IDecisionOverride _override;
        readonly int[] _list = new int[Squad.Slots];
        readonly InputKind[] _input = new InputKind[Squad.Slots];
        readonly int[] _chaser = new int[2];
        int _presser;
        long _restartKickTick = long.MinValue / 2;
        const long RestartRetryTicks = 10;

        public StandInOnBall(IDecisionOverride decisionOverride) => _override = decisionOverride;

        /// <summary>병합된 결정 대상 (MergeInputs 결과).</summary>
        public int ListAt(int i) => _list[i];
        /// <summary>이번 틱 외부 입력 (없으면 기본값).</summary>
        public InputKind InputOf(int s) => _input[s];
        public int Presser => _presser;
        public int Chaser(int team) => _chaser[team];
        public long RestartKickTick { get => _restartKickTick; set => _restartKickTick = value; }

        /// <summary>
        /// 대체 태클 (070 자리): 압박 중인 선수가 태클 거리 안이면 시도 확률 × scale(다혈질, 010 FR-83)로 걷어내기. 동작했으면 true.
        /// RNG는 거리 안일 때만 1회(006 대체 압박과 같은 규칙).
        /// </summary>
        public bool TryTackle(MatchContext ctx, int s, int owner, float scale, ref bool hasAction)
        {
            if (hasAction || owner < 0 || Squad.TeamOfSlot(owner) == Squad.TeamOfSlot(s)) return false; // 동료 공은 걷어내지 않는다 (결정론 리뷰 M3)
            var p = ctx.Params;
            var me = ctx.SlotState(s);
            var od = (ctx.SlotState(owner).Position - me.Position).LengthSq();
            if (od > p.TackleRange * p.TackleRange) return false;
            var prob = p.TackleProb * scale;
            if (!ctx.DecisionRng.Bernoulli(prob < 0f ? 0f : (prob > 1f ? 1f : prob))) return false;
            hasAction = true;
            var away = ctx.SlotState(owner).Position - me.Position;
            var dir = away.Normalized();
            if (dir == Vec2.Zero) dir = new Vec2(ctx.AttackDir(Squad.TeamOfSlot(s)), 0f);
            ctx.Board.SetBallAction(new BallAction
            {
                Kind = BallActionKind.Clearance, KickerSlot = s, TargetSlot = -1,
                Velocity = new Vec3(dir.X * 6f, dir.Y * 6f, 0f), Part = PhysicsStage.PartFor(in me, ctx.Ball.Position),
            });
            return true;
        }

        /// <summary>틱 시작: 외부 입력 초기화.</summary>
        public void BeginTick() => System.Array.Clear(_input, 0, _input.Length);

        public int MergeInputs(MatchContext ctx)
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

        public void ComputeChasers(MatchContext ctx)
        {
            ref readonly var ball = ref ctx.Ball;
            var target = ball.Position.XY + ball.Velocity.XY * 0.3f;
            _presser = -1;
            for (int t = 0; t < 2; t++)
            {
                _chaser[t] = -1;
                float bd = float.MaxValue;
                for (int s = t * Squad.SlotsPerTeam; s < (t + 1) * Squad.SlotsPerTeam; s++)
                {
                    if (!ctx.IsSlotControllable(s)) continue;
                    if (_override != null && _override.IsCast(s)) continue;
                    if (Squad.IsKeeperSlot(s)) continue; // 011 FR-16: GK 경합은 GK 결정(KeeperChooser)이 정한다
                    var d = (ctx.SlotState(s).Position - target).LengthSq();
                    if (d < bd) { bd = d; _chaser[t] = s; }
                }
            }
            if (ball.Phase == BallPhase.Controlled && ball.OwnerId >= 0)
                _presser = _chaser[1 - Squad.TeamOfSlot(ball.OwnerId)];
        }

        public Decision Decide(MatchContext ctx, int s, ref bool hasAction)
        {
            var flow = ctx.Flow;
            ref var ball = ref ctx.Ball;
            var p = ctx.Params;
            var team = Squad.TeamOfSlot(s);
            var me = ctx.SlotState(s);

            if (_input[s] == InputKind.HoldPosition) return new Decision { Kind = DecisionKind.Stand, TargetSlot = -1 };

            if (flow.State == FlowState.DeadBall && flow.DeadBall.IsGoal) return DecideGoalDeadBall(ctx, s, ref hasAction);
            if (flow.State == FlowState.DeadBall)
            {
                if (s != flow.RestartTaker)
                    return flow.Restart == RestartKind.Kickoff
                        ? new Decision { Kind = DecisionKind.Stand, TargetSlot = -1 }
                        : new Decision { Kind = DecisionKind.HoldShape, TargetSlot = -1 };
                if (!(ball.Phase == BallPhase.Controlled && ball.OwnerId == s))
                {
                    if (flow.DeadBall.Relocated && (me.Position - flow.RestartSpot).LengthSq() <= p.ClaimRadius * p.ClaimRadius) // 058 FR-08: 공이 지점에 놓인 뒤
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

        /// <summary>
        /// 골 후 데드볼 (specs/058 FR-02): 세리머니 단계는 득점자만 코너 쪽 조깅, 나머지는 감속. 복귀·준비 단계는 킥오프 지점으로
        /// 걷거나 조깅(복귀 속도). 키커는 공이 센터 스폿에 놓인 뒤(재배치) 잡고, 준비 지연이 지나면 킥오프한다.
        /// </summary>
        Decision DecideGoalDeadBall(MatchContext ctx, int s, ref bool hasAction)
        {
            var flow = ctx.Flow;
            var db = flow.DeadBall;
            ref var ball = ref ctx.Ball;
            var me = ctx.SlotState(s);
            if (db.Phase == DeadBallPhase.Celebration)
            {
                if (s != db.Scorer) return Stand();
                var jog = ctx.Kinematics.Profile(s).MaxSpeed * ctx.Params.ReturnSpeedMax;
                return new Decision { Kind = DecisionKind.MoveTo, Point = db.CelebrationTarget(s), TargetSlot = -1, MaxSpeed = jog };
            }
            if (s == flow.RestartTaker)
            {
                var p = ctx.Params;
                if (ball.Phase == BallPhase.Controlled && ball.OwnerId == s)
                {
                    if (db.Phase == DeadBallPhase.Ready && ctx.Tick >= flow.ReadyTick && !hasAction && ctx.Tick - _restartKickTick >= RestartRetryTicks)
                    {
                        hasAction = true;
                        _restartKickTick = ctx.Tick;
                        if (!Pass(ctx, s, Squad.TeamOfSlot(s), true, true)) ClearForward(ctx, s, Squad.TeamOfSlot(s));
                    }
                    return Stand();
                }
                if (db.Relocated && ball.Phase != BallPhase.Controlled && (me.Position - flow.RestartSpot).LengthSq() <= p.ClaimRadius * p.ClaimRadius)
                {
                    BallCommands.TakeControl(ref ball, s, new Vec3(flow.RestartSpot.X, flow.RestartSpot.Y, ctx.BallParams.Radius), ctx.BallEvents);
                    ctx.PossessionStartTick = ctx.Tick;
                    ctx.LastTouchTeam = Squad.TeamOfSlot(s);
                    ctx.LastTouchSlot = s;
                    return Stand();
                }
            }
            return new Decision { Kind = DecisionKind.MoveTo, Point = db.KickoffTarget(s), TargetSlot = -1, MaxSpeed = db.ReturnSpeed(s) };
        }

        public static Decision Stand() => new Decision { Kind = DecisionKind.Stand, TargetSlot = -1 };

        /// <summary>인플레이·공을 안 가진 GK·외부 입력 없음 → GK 고유 결정 (011 FR-16·94).</summary>
        public bool KeeperDecides(MatchContext ctx, int s)
        {
            if (!Squad.IsKeeperSlot(s) || _input[s] != default || ctx.Flow.State != FlowState.InPlay) return false;
            ref readonly var ball = ref ctx.Ball;
            return !(ball.Phase == BallPhase.Controlled && ball.OwnerId == s);
        }

        /// <summary>
        /// GK 결정 (011 FR-94): 시나리오 배역이면 시나리오 결정을 의도(지점 이동)로만 넘기고, 최종 선택은 GK 기대효용 + 010 선택 규칙.
        /// </summary>
        public Decision DecideKeeper(MatchContext ctx, int s, ref bool hasAction)
        {
            var intent = default(Goalkeeper.KeeperIntent);
            // 의도 지점만 부수효과 없이 읽는다 — 시나리오가 GK 공 동작·RNG를 건드리지 못하게 (결정론 리뷰 M1)
            if (_override != null && _override.IsCast(s) && _override.TryReadIntent(ctx, s, out var ip))
                intent = new Goalkeeper.KeeperIntent { Has = true, Kind = Goalkeeper.KeeperActionKind.MoveTo, Point = ip, InWindow = true };
            var kind = ctx.Keepers.Decide(ctx, Squad.TeamOfSlot(s), in intent, out var point);
            switch (kind)
            {
                case Goalkeeper.KeeperActionKind.Contest: return new Decision { Kind = DecisionKind.Chase, Point = point, TargetSlot = -1 };
                case Goalkeeper.KeeperActionKind.MoveTo: return new Decision { Kind = DecisionKind.MoveTo, Point = point, TargetSlot = -1 };
                default: return new Decision { Kind = DecisionKind.HoldShape, TargetSlot = -1 };
            }
        }

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
        /// <summary>마지막으로 낸 패스의 받는 슬롯과 틱 (010 수신 우선권, 상태 아님 — 같은 틱에 읽는다).</summary>
        public int LastPassTarget { get; private set; } = -1;
        public long LastPassTick { get; private set; } = long.MinValue;

        bool Pass(MatchContext ctx, int s, int team, bool restart, bool kickoff)
        {
            var me = ctx.SlotState(s);
            var p = ctx.Params;
            var dir = ctx.AttackDir(team);
            int best = -1;
            float bestScore = float.MinValue, bestDist = 0f;
            for (int o = team * Squad.SlotsPerTeam; o < (team + 1) * Squad.SlotsPerTeam; o++)
            {
                if (o == s || !ctx.IsSlotControllable(o)) continue;
                var op = ctx.SlotState(o).Position;
                var dist = (op - me.Position).Length();
                if (dist < 4f || dist > p.PassRange) continue;
                var gain = (op.X - me.Position.X) * dir;
                var score = (kickoff ? -dist : gain - 0.2f * dist) + ctx.DecisionRng.Range(0f, 8f);
                if (score > bestScore) { bestScore = score; best = o; bestDist = dist; }
            }
            if (best < 0) return false;
            LastPassTarget = best;
            LastPassTick = ctx.Tick;
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

    }
}
