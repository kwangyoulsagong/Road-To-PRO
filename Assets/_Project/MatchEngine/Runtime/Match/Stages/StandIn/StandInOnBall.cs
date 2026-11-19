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
            _receiver = -1;
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
            else if (ball.Phase != BallPhase.Controlled && ctx.Actions.FlightTarget is var ft && ft >= 0
                && ctx.IsSlotControllable(ft) && Squad.TeamOfSlot(ft) == ctx.LastTouchTeam && ft != ctx.LastKickSlot
                && !Squad.IsKeeperSlot(ft) && (_override == null || !_override.IsCast(ft)))
            {
                // 008 T14a: 실행된 동료 패스가 날아가는 중이면 받는 선수가 그 팀 추격자 — 공 경로 위 최근접점으로 (대체·시나리오 패스 모두)
                _receiver = ft;
                _chaser[ctx.LastTouchTeam] = _receiver;
                _receivePoint = Actions.ReceivePoint.OnPath(ball.Position.XY, ball.Velocity.XY, ctx.SlotState(_receiver).Position, ctx.Actions.P.ReceiveAhead);
            }
        }

        int _receiver = -1;
        Vec2 _receivePoint;

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
                    // 058 FR-08: 공이 지점에 놓인 뒤. 086 FR-01 (Q1 ①): 지나가며 잡지 않는다 — 멈춘 뒤(속도 ≤ restartTakeMaxSpeed)에만, 아니면 계속 지점으로
                    if (flow.DeadBall.Relocated && (me.Position - flow.RestartSpot).LengthSq() <= p.ClaimRadius * p.ClaimRadius && SlowEnoughToTake(ctx, me))
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
                    else if (flow.Restart == RestartKind.GoalKick && Squad.IsKeeperSlot(s) && ctx.Actions.Duels.Enabled) // 074 FR-10: GK 골킥 = 짧은·롱 골킥 배급
                    {
                        if (!ctx.Keepers.TryDistribute(ctx, s, Goalkeeper.DistributionSource.GoalKick, out _)) ClearForward(ctx, s, team);
                    }
                    else if (!Pass(ctx, s, team, true, flow.Restart == RestartKind.Kickoff)) ClearForward(ctx, s, team);
                }
                return TakerAtBall(ctx, s); // 086 T03a
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
                var target = s == _receiver ? _receivePoint : ball.Position.XY + ball.Velocity.XY * 0.3f;
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
                    return TakerAtBall(ctx, s); // 086 T03a
                }
                if (db.Relocated && ball.Phase != BallPhase.Controlled && (me.Position - flow.RestartSpot).LengthSq() <= p.ClaimRadius * p.ClaimRadius
                    && SlowEnoughToTake(ctx, me)) // 086 FR-01: 킥오프도 멈춘 뒤 잡는다
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

        /// <summary>
        /// 086 FR-01 (Q1 ①): 키커가 멈췄다 (속도 ≤ restartTakeMaxSpeed). 결정론 리뷰 M: 밀어내기(separation)와 도착이 진동해 끝내 못 멈추면
        /// 데드볼이 끝나지 않으므로, 058 FR-09 대기 상한(RestartWaitMaxTicks)이 지나면 속도 조건 없이 잡는다(공은 지점에 고정 — FR-01 ⓑ).
        /// </summary>
        static bool SlowEnoughToTake(MatchContext ctx, in RoadToPro.MatchEngine.Kinematics.PlayerMotionState me)
        {
            var p = ctx.Params;
            return me.Velocity.LengthSq() <= p.RestartTakeMaxSpeed * p.RestartTakeMaxSpeed
                || ctx.Tick - ctx.Flow.DeadBall.PhaseStartTick >= p.RestartWaitMaxTicks;
        }

        /// <summary>데드볼 키커가 공 곁에 섰다고 보는 캐리 거리 여유 (m, 086 T03a). 킥 도달 거리(act.kick.reach 0.9)보다 충분히 안쪽.</summary>
        public const float TakerNearSlack = 0.05f;

        /// <summary>데드볼 중 재시작 키커가 공을 가졌다 (086 FR-01 ⓑ: 이 동안 공은 재시작 지점에 고정).</summary>
        public static bool HoldsDeadBall(MatchContext ctx, int s) =>
            ctx.Flow.State == FlowState.DeadBall && s == ctx.Flow.RestartTaker && ctx.Ball.Phase == BallPhase.Controlled && ctx.Ball.OwnerId == s;

        /// <summary>
        /// 086 T03a: 공을 가진 데드볼 키커는 공 곁에 있는다 — 캐리 거리(+ 5 cm)보다 멀면 공(재시작 지점)으로 돌아가고, 아니면 선다.
        /// 공이 지점에 고정되므로(FR-01 ⓑ) 준비 뒤 시나리오가 키커를 옮기면 킥 도달 거리(act.kickReach) 밖이 되어 예약 킥이 계속 취소됐다.
        /// 킥은 판단 보드의 공 동작으로 따로 나가므로 이동 결정만 바꾼다.
        /// </summary>
        public static Decision TakerAtBall(MatchContext ctx, int s)
        {
            var spot = ctx.Flow.RestartSpot;
            var near = PhysicsStage.BallCarryOffset + TakerNearSlack;
            return (ctx.SlotState(s).Position - spot).LengthSq() > near * near
                ? new Decision { Kind = DecisionKind.MoveTo, Point = spot, TargetSlot = -1 }
                : Stand();
        }

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
                // 073: 돌진·스마더 = 공 가로채기(첫 접촉점·공), 잡기·펀칭 = 착지점, 세트 = 전진점(정지 뒤엔 자기 위치)
                case Goalkeeper.KeeperActionKind.Rush:
                case Goalkeeper.KeeperActionKind.Smother: return new Decision { Kind = DecisionKind.Chase, Point = point, TargetSlot = -1 };
                case Goalkeeper.KeeperActionKind.Claim:
                case Goalkeeper.KeeperActionKind.Punch: return new Decision { Kind = DecisionKind.MoveTo, Point = point, TargetSlot = -1 };
                case Goalkeeper.KeeperActionKind.Set: return new Decision { Kind = DecisionKind.MoveTo, Point = point, TargetSlot = -1 };
                default: return new Decision { Kind = DecisionKind.HoldShape, TargetSlot = -1 };
            }
        }

        /// <summary>대체 슈팅 공 동작 (072가 재사용 — 조준 오차는 071 전 대체, 결정 RNG 2회).</summary>
        /// <summary>
        /// 074 FR-10~14: 인플레이에서 공을 가진 GK (010 파이프라인). 자기 박스 안 + 손 보유 시계가 돌면 손에 든 공, 아니면 발로 가진 공.
        /// 배급하면 공 동작을 올리고 서 있고, 기다리면 박스 선 안쪽 지점으로 걸어간다.
        /// </summary>
        public static Decision KeeperOnBall(MatchContext ctx, int s, ref bool hasAction)
        {
            if (hasAction || ctx.Actions.Pending.Active) return Stand();
            var team = Squad.TeamOfSlot(s);
            var me = ctx.SlotState(s);
            var held = PhysicsStage.InOwnBox(ctx, team, new Vec3(me.Position.X, me.Position.Y, 0f)) && ctx.Keepers.Hold(team).Active;
            if (ctx.Keepers.TryDistribute(ctx, s, held ? Goalkeeper.DistributionSource.Hands : Goalkeeper.DistributionSource.Feet, out var stand))
            {
                hasAction = true;
                return Stand();
            }
            return new Decision { Kind = DecisionKind.MoveTo, Point = stand, TargetSlot = -1, MaxSpeed = held ? ctx.Keepers.Params.HoldWalkSpeed : 0f };
        }

        public static void Shoot(MatchContext ctx, int s, int team, bool restart, bool penalty)
        {
            var me = ctx.SlotState(s);
            if (ctx.Actions.Duels.Enabled) // 071: 010 파이프라인은 조준 셀·실행 오차 (006 대체 파이프라인은 그대로 — 해시 불변)
            {
                ctx.Board.SetBallAction(ctx.Actions.Shots.Plan(ctx, s, team, -1f, restart, penalty, PhysicsStage.PartFor(in me, ctx.Ball.Position)));
                return;
            }
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

        /// <summary>
        /// 마지막으로 낸 패스(결정)의 받는 슬롯과 틱. 010 수신 우선권이 같은 틱에 읽는다. 날아가는 패스의 받는 선수는 실행 기준인 <c>ActionSystem.FlightTarget</c>을 쓴다(결정론 리뷰).
        /// 값이 다음 틱에 남으므로 해시·스냅샷에 넣어 둔다.
        /// </summary>
        public int LastPassTarget { get; set; } = -1;
        public long LastPassTick { get; set; } = long.MinValue;

        public void WritePass(Core.Serialization.StateWriter w) { w.Write(LastPassTarget); w.Write(LastPassTick); }
        public void ReadPass(Core.Serialization.StateReader r) { LastPassTarget = r.ReadInt(); LastPassTick = r.ReadLong(); }
        public void AddPassTo(ref Core.Hashing.StableHasher h) { h.Add(LastPassTarget); h.Add(LastPassTick); }

        /// <summary>
        /// 대체 패스 선택 (072 자리, specs/008 T14): 동료마다 008 평가(로지스틱 xPass, 속도 후보·스루볼 lead)를 내고
        /// 점수 = xPass × max(floor, 1 + xtK·ΔxT + gainK·전진 거리) + 잡음 U(0, noise) [대체, act.standin.*]가 가장 큰 패스를 의도로 낸다(실행 오차는 접촉 순간 008).
        /// 킥오프는 가까운 동료. 결정 RNG 소비는 후보당 1회(이전과 같은 규칙). 007 FR-21: 킥오프가 아니면 점수 상위 K개를 궤적 질의로 재평가.
        /// </summary>
        bool Pass(MatchContext ctx, int s, int team, bool restart, bool kickoff)
        {
            var me = ctx.SlotState(s);
            var p = ctx.Params;
            var dir = ctx.AttackDir(team);
            var act = ctx.Actions;
            var from = ctx.Ball.Position.XY;
            int best = -1;
            float bestScore = float.MinValue;
            var bestOpt = default(Actions.PassOption);
            var press = act.PressureOn(ctx, s); // 패서 압박은 후보와 무관 — 1회
            var n = 0;
            for (int o = team * Squad.SlotsPerTeam; o < (team + 1) * Squad.SlotsPerTeam; o++)
            {
                if (o == s || !ctx.IsSlotControllable(o)) continue;
                var op = ctx.SlotState(o).Position;
                var dist = (op - me.Position).Length();
                if (dist < 4f || dist > p.PassRange) continue;
                var noise = ctx.DecisionRng.Range(0f, act.P.StandInNoise);
                var opt = act.EvaluatePass(ctx, s, o, op, press, out var dxt);
                if (!kickoff)
                {
                    var th = act.EvaluateThrough(ctx, s, o, press, out var dxt2);
                    if (th.Valid && th.XPass * DMath.Max(act.P.StandInFloor, 1f + act.P.StandInXtK * dxt2) > opt.XPass * DMath.Max(act.P.StandInFloor, 1f + act.P.StandInXtK * dxt)) { opt = th; dxt = dxt2; }
                }
                if (!opt.Valid) continue;
                _cTarget[n] = o; _cOpt[n] = opt; _cDxt[n] = dxt; _cNoise[n] = noise; _cDist[n] = dist; _cRefined[n] = false;
                _cScore[n] = StandInScore(act, kickoff, from, dir, in opt, dxt, dist, noise);
                n++;
            }
            // 007 FR-21: 점수 상위 K개(킥오프 제외)는 궤적 질의로 xPass를 다시 정하고 다시 점수, 뒤이어 최고가 미질의면 그것도 (최대 refineExtra회)
            var pc = act.PcParams;
            if (!kickoff && !pc.LegacyOn && pc.RefineCount > 0)
            {
                for (int r = 0; r < pc.RefineCount; r++)
                {
                    var b = BestStandIn(n, false);
                    if (b < 0) break;
                    RefineStandIn(act, ctx.BallParams, from, s, b, kickoff, dir);
                }
                for (int e = 0; e < pc.RefineExtraCount; e++)
                {
                    var b = BestStandIn(n, true);
                    if (b < 0 || _cRefined[b]) break;
                    RefineStandIn(act, ctx.BallParams, from, s, b, kickoff, dir);
                }
            }
            for (int i = 0; i < n; i++)
            {
                act.Diagnostics?.OnPassCandidate(ctx.Tick, s, _cTarget[i], in _cOpt[i], _cDxt[i], press, _cScore[i]);
                if (_cScore[i] > bestScore) { bestScore = _cScore[i]; best = _cTarget[i]; bestOpt = _cOpt[i]; }
            }
            if (best < 0) return false;
            LastPassTarget = best;
            LastPassTick = ctx.Tick;
            act.Diagnostics?.OnPassChosen(ctx.Tick, s, best, in bestOpt);
            var a = Actions.ActionSystem.IntentAction(s, best, in bestOpt, from, ctx.Tick - ctx.PossessionStartTick <= (long)act.P.OneTouchTicks);
            a.Part = PhysicsStage.PartFor(in me, ctx.Ball.Position);
            a.IsRestart = restart;
            a.IsKickoff = kickoff;
            ctx.Board.SetBallAction(a);
            return true;
        }

        // 대체 패스 후보 버퍼 (동료 최대 10명, 재사용)
        readonly int[] _cTarget = new int[Squad.SlotsPerTeam];
        readonly Actions.PassOption[] _cOpt = new Actions.PassOption[Squad.SlotsPerTeam];
        readonly float[] _cDxt = new float[Squad.SlotsPerTeam], _cNoise = new float[Squad.SlotsPerTeam], _cDist = new float[Squad.SlotsPerTeam], _cScore = new float[Squad.SlotsPerTeam];
        readonly bool[] _cRefined = new bool[Squad.SlotsPerTeam];

        static float StandInScore(Actions.ActionSystem act, bool kickoff, Vec2 from, int dir, in Actions.PassOption opt, float dxt, float dist, float noise)
        {
            var gain = (opt.Aim.X - from.X) * dir;
            return kickoff ? -dist + noise : opt.XPass * DMath.Max(act.P.StandInFloor, 1f + act.P.StandInXtK * dxt + act.P.StandInGainK * gain) + noise;
        }

        int BestStandIn(int n, bool includeRefined)
        {
            var best = -1;
            for (int i = 0; i < n; i++)
            {
                if (!includeRefined && _cRefined[i]) continue;
                if (best < 0 || _cScore[i] > _cScore[best]) best = i;
            }
            return best;
        }

        void RefineStandIn(Actions.ActionSystem act, BallParams bp, Vec2 from, int s, int i, bool kickoff, int dir)
        {
            _cOpt[i].XPass = Actions.PassModel.Refine(act.P, bp, act.World, s, _cTarget[i], from, in _cOpt[i]);
            _cScore[i] = StandInScore(act, kickoff, from, dir, in _cOpt[i], _cDxt[i], _cDist[i], _cNoise[i]);
            _cRefined[i] = true;
        }

        static void ClearForward(MatchContext ctx, int s, int team) => ClearForward(ctx, s, team, true);

        /// <summary>앞으로 걷어내기 (072 인플레이 걷어내기는 restart = false).</summary>
        public static void ClearForward(MatchContext ctx, int s, int team, bool restart)
        {
            var me = ctx.SlotState(s);
            var dir = ctx.AttackDir(team);
            ctx.Board.SetBallAction(new BallAction
            {
                Kind = BallActionKind.Clearance, KickerSlot = s, TargetSlot = -1, Velocity = new Vec3(dir * 18f, 0f, 8f),
                Part = PhysicsStage.PartFor(in me, ctx.Ball.Position), IsRestart = restart,
            });
        }

    }
}
