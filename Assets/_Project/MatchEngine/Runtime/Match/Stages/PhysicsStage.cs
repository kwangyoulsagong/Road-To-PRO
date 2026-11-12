using RoadToPro.Core.Math;
using RoadToPro.MatchEngine.Ball;
using RoadToPro.MatchEngine.Kinematics;
using RoadToPro.MatchEngine.Match.Events;
using RoadToPro.MatchEngine.Match.Pipeline;
using RoadToPro.MatchEngine.Match.Replay;

namespace RoadToPro.MatchEngine.Match.Stages
{
    /// <summary>
    /// 단계 7 — 물리 서브스텝 100 Hz × 10 (specs/006 FR-01 ⑧, FR-03). 004 공·005 선수.
    /// 서브스텝 0 시작에 단계 5가 정한 공 동작(킥)을 적용하고, 매 서브스텝 공 사건(라인 통과·골대)을 서브스텝 시각과 함께
    /// 보드에 남겨 심판 단계로 넘긴다. 느슨한 공 잡기는 대체 규칙(008 자리)으로 서브스텝마다 판정한다.
    /// </summary>
    public sealed class PhysicsStage : StatelessStage
    {
        public const float BallCarryOffset = 0.45f;
        /// <summary>다이빙 중 GK가 잡은 공이 손 위치로 따라가는 서브스텝당 최대 이동 (m, 15 m/s).</summary>
        public const float KeeperHoldStep = 0.15f;

        public override StageId Id => StageId.Physics;

        /// <summary>테스트용 서브스텝 카운터 (AC-02: 틱당 정확히 10회).</summary>
        public long SubstepsRun { get; private set; }

        public override void Execute(MatchContext ctx)
        {
            var k = ctx.Kinematics;
            var board = ctx.Board;
            ref readonly var action = ref board.GetBallAction();
            var rec = ctx.Recorder;
            var crossed = false;
            for (int sub = 0; sub < Core.Time.SimClock.SubstepsPerDecision; sub++)
            {
                var abs = k.Substep;
                rec.CapturePre(abs);
                if (sub == 0 && action.Kind != BallActionKind.None) ApplyAction(ctx, in action, abs);
                k.Step();
                ctx.Officials.Step(ctx.KinParams.Dt);
                ref var ball = ref ctx.Ball;
                if (ball.Phase == BallPhase.Controlled && ball.OwnerId >= 0 && ball.OwnerId < Squad.Slots)
                {
                    ref readonly var o = ref k.State(ball.OwnerId);
                    var bp = o.Position + o.Facing * BallCarryOffset;
                    var target = new Vec3(bp.X, bp.Y, ctx.BallParams.Radius);
                    if (ctx.Keepers.IsBusySlot(ball.OwnerId))
                    {
                        // 011: 다이빙 중 잡은 공은 손 위치로 서브스텝당 최대 KeeperHoldStep만 옮긴다 (순간이동 금지)
                        var d = target - ball.Position;
                        var len = d.Length();
                        if (len > KeeperHoldStep) target = ball.Position + d * (KeeperHoldStep / len);
                    }
                    BallCommands.SetControlledPosition(ref ball, target);
                }
                ctx.BallEvents.Clear();
                BallStepper.Step(ref ball, ctx.BallParams, ctx.BallEvents, out _);
                for (int i = 0; i < ctx.BallEvents.Count; i++)
                {
                    ref readonly var be = ref ctx.BallEvents[i];
                    if (be.Kind == BallEventKind.NumericSanitized) throw new NumericException($"공 상태 NaN/무한대 보정 발생 (틱 {ctx.Tick}, 서브스텝 {sub})");
                    if (be.Kind == BallEventKind.BoundaryCrossed)
                    {
                        crossed = true;
                        board.AddPhysicsCall(new PhysicsCall
                        {
                            Kind = PhysicsCallKind.BoundaryCrossed, Substep = (sbyte)sub, Slot = ctx.LastTouchSlot, Boundary = be.Boundary,
                            InGoalMouth = be.InGoalMouth, Position = be.Position, LastTouchTeam = ctx.LastTouchTeam, LastTouchSlot = ctx.LastTouchSlot,
                        });
                    }
                    else if (be.Kind == BallEventKind.GoalFrameHit)
                    {
                        board.AddPhysicsCall(new PhysicsCall
                        {
                            Kind = PhysicsCallKind.WoodworkHit, Substep = (sbyte)sub, Slot = ctx.LastTouchSlot, Position = be.Position,
                            LastTouchTeam = ctx.LastTouchTeam, LastTouchSlot = ctx.LastTouchSlot,
                        });
                    }
                }
                if (!crossed && ctx.Flow.InPlay && OutsidePitch(ctx, out var boundary, out var mouth))
                {
                    // 004는 날아가는·구르는 공의 라인 통과만 낸다. 몰고 나간 공(Controlled)도 같은 사건으로 넘긴다.
                    crossed = true;
                    board.AddPhysicsCall(new PhysicsCall
                    {
                        Kind = PhysicsCallKind.BoundaryCrossed, Substep = (sbyte)sub, Slot = ctx.LastTouchSlot, Boundary = boundary,
                        InGoalMouth = mouth, Position = ball.Position, LastTouchTeam = ctx.LastTouchTeam, LastTouchSlot = ctx.LastTouchSlot,
                    });
                }
                if (!crossed) ctx.Keepers.OnSubstep(ctx, abs, sub); // 011 FR-15·17: 다이빙 진행·슛 감시·선방 적용
                if (!crossed) TryClaim(ctx, abs, sub);
                rec.AfterSubstep(abs);
                SubstepsRun++;
            }
            rec.EndTick();
        }

        static void ApplyAction(MatchContext ctx, in BallAction a, long abs)
        {
            ref var ball = ref ctx.Ball;
            if (ball.Phase != BallPhase.Controlled) return; // 그 사이 공을 잃음
            var tackle = a.Kind == BallActionKind.Clearance && ball.OwnerId != a.KickerSlot;
            if (!tackle && ball.OwnerId != a.KickerSlot) return;
            var victim = ball.OwnerId;
            var team = Squad.TeamOfSlot(a.KickerSlot);
            BallCommands.Kick(ref ball, a.Velocity, a.Spin, ctx.BallParams, ctx.BallEvents);
            ctx.Keepers.NotifyFlight(); // 011: 골문을 향하는지 다음 감시에서 예측
            if (a.IsKickoff)
            {
                var ko = SimEvent.Make(SimEventKind.Kickoff, team, ctx.PlayerAtSlot(a.KickerSlot));
                ko.Substep = 0;
                ctx.SetPosition(ref ko, ball.Position);
                ctx.Emit(ko);
            }
            var kind = tackle ? SimEventKind.Tackle : a.Kind == BallActionKind.Shot ? SimEventKind.Shot : a.Kind == BallActionKind.Pass ? SimEventKind.Pass : SimEventKind.Clearance;
            var e = SimEvent.Make(kind, team, ctx.PlayerAtSlot(a.KickerSlot),
                tackle ? ctx.PlayerAtSlot(victim) : (a.TargetSlot >= 0 ? ctx.PlayerAtSlot(a.TargetSlot) : -1));
            e.Substep = 0;
            e.Part = a.Part == BodyPart.None ? BodyPart.RightFoot : a.Part;
            e.BallPhase = ball.Phase;
            e.F0 = a.Velocity.Length();
            e.A = a.IsPenalty ? 1 : 0;
            ctx.SetPosition(ref e, ball.Position);
            ctx.Emit(e);
            ctx.LastKickSlot = a.KickerSlot;
            ctx.LastKickSubstep = abs;
            ctx.LastTackleVictim = tackle ? victim : -1;
            ctx.LastKickKind = (byte)a.Kind;
            ctx.LastTouchTeam = team;
            ctx.LastTouchSlot = a.KickerSlot;
            ctx.Recorder.MarkAction(a.KickerSlot, tackle ? MotionHint.Tackle : MotionHint.Kick);
            if (tackle)
            {
                ctx.Kinematics.ApplyContact(a.KickerSlot);
                if (victim >= 0 && victim < Squad.Slots && ctx.IsSlotActive(victim)) ctx.Kinematics.ApplyContact(victim);
            }
            if (a.IsRestart) ctx.Flow.OnRestartTaken();
        }

        /// <summary>공 전체가 라인 밖인가 (중심이 라인에서 반지름 넘게). 004 라인 통과 판정과 같은 기준.</summary>
        static bool OutsidePitch(MatchContext ctx, out BallBoundary boundary, out bool mouth)
        {
            var pos = ctx.Ball.Position;
            var r = ctx.BallParams.Radius;
            var L = ctx.Pitch.LengthM;
            var W = ctx.Pitch.WidthM;
            mouth = false;
            if (pos.X < -r) boundary = BallBoundary.GoalLineLow;
            else if (pos.X > L + r) boundary = BallBoundary.GoalLineHigh;
            else if (pos.Y < -r) boundary = BallBoundary.TouchlineLow;
            else if (pos.Y > W + r) boundary = BallBoundary.TouchlineHigh;
            else { boundary = BallBoundary.None; return false; }
            if (boundary == BallBoundary.GoalLineLow || boundary == BallBoundary.GoalLineHigh)
                mouth = DMath.Abs(pos.Y - W * 0.5f) < BallParams.GoalWidth * 0.5f && pos.Z < BallParams.GoalHeight;
            return true;
        }

        /// <summary>대체 컨트롤 규칙 (008 자리): 공 가까이(반경·높이·속도 한도) 가장 가까운 선수가 잡는다. 같은 거리면 슬롯 순.</summary>
        static void TryClaim(MatchContext ctx, long abs, int sub)
        {
            ref var ball = ref ctx.Ball;
            if (!ctx.Flow.InPlay || ball.Phase == BallPhase.Controlled) return;
            var p = ctx.Params;
            var pos = ball.Position;
            if (pos.Z > p.ClaimMaxZ) return;
            // 공 전체가 라인 밖일 때만 잡기 금지 (라인 위 공은 인플레이 — 011 발견: 골라인 위에 멈춘 공을 아무도 못 잡아 경기가 멈춤)
            var rb = ctx.BallParams.Radius;
            if (pos.X < -rb || pos.X > ctx.Pitch.LengthM + rb || pos.Y < -rb || pos.Y > ctx.Pitch.WidthM + rb) return;
            var v2 = ball.Velocity.LengthSq();
            var fieldOk = v2 <= p.ClaimMaxSpeed * p.ClaimMaxSpeed;
            // 골키퍼 (011 FR-16): 자기 페널티 박스 안에서만 손으로 더 넓게. 골문을 향하는 슛은 선방 모델만 판정(추적 중이면 제외)
            var keeperR = p.ClaimRadius * 1.8f;
            int best = -1;
            float bestScore = float.MaxValue;
            var k = ctx.Kinematics;
            for (int s = 0; s < Squad.Slots; s++)
            {
                if (!ctx.IsSlotControllable(s)) continue;
                if ((s == ctx.LastKickSlot || s == ctx.LastTackleVictim) && abs - ctx.LastKickSubstep < 25) continue;
                var keeper = Squad.IsKeeperSlot(s) && InOwnBox(ctx, Squad.TeamOfSlot(s), pos);
                if (Squad.IsKeeperSlot(s) && ctx.Keepers.ExcludedFromClaim(s)) continue;
                if (!fieldOk) continue;
                var r = keeper ? keeperR : p.ClaimRadius;
                var d = k.State(s).Position;
                var dx = d.X - pos.X;
                var dy = d.Y - pos.Y;
                var dsq = dx * dx + dy * dy;
                if (dsq > r * r) continue;
                var score = dsq / (r * r); // 반경 대비 거리 — 같으면 슬롯 순
                if (score < bestScore) { bestScore = score; best = s; }
            }
            if (best < 0) return;
            var speed = ball.Velocity.Length();
            var team = Squad.TeamOfSlot(best);
            var prevTeam = ctx.LastTouchTeam;
            var prevKicker = ctx.LastKickSlot;
            var kickKind = (BallActionKind)ctx.LastKickKind;
            var z = pos.Z;
            BallCommands.TakeControl(ref ball, best, pos, ctx.BallEvents);
            SimEventKind kind;
            if (team == prevTeam && kickKind == BallActionKind.Pass && prevKicker != best) kind = SimEventKind.PassReceived;
            else if (team != prevTeam && (kickKind == BallActionKind.Pass || kickKind == BallActionKind.Shot) && speed > 3f) kind = SimEventKind.Interception;
            else kind = SimEventKind.BallRecovered;
            var e = SimEvent.Make(kind, team, ctx.PlayerAtSlot(best), prevKicker >= 0 ? ctx.PlayerAtSlot(prevKicker) : -1);
            e.Substep = (sbyte)sub;
            e.Part = PartFor(k.State(best), pos);
            e.BallPhase = BallPhase.Controlled;
            e.F0 = speed;
            ctx.SetPosition(ref e, pos);
            ctx.Emit(e);
            if (team != prevTeam)
            {
                var pc = SimEvent.Make(SimEventKind.PossessionChange, team, ctx.PlayerAtSlot(best));
                pc.Substep = (sbyte)sub;
                ctx.SetPosition(ref pc, pos);
                ctx.Emit(pc);
            }
            ctx.LastTouchTeam = team;
            ctx.LastTouchSlot = best;
            ctx.LastKickKind = 0;
            ctx.PossessionStartTick = ctx.Tick;
            if (ctx.Flow.PenaltyKickTick >= 0) ctx.Flow.ClearPenalty();
        }

        /// <summary>
        /// 공이 team의 자기 페널티 박스 안(손 사용 가능)인가 (011 FR-16). 공 일부라도 선 위면 허용 — 중심이 선에서 반지름 안까지
        /// (스펙 경계 '[미정: 규칙 확인]', 012에서 재확인).
        /// </summary>
        public static bool InOwnBox(MatchContext ctx, int team, Vec3 ball)
        {
            var goal = ctx.OwnGoal(team);
            var r = ctx.BallParams.Radius;
            var depth = (ball.X - goal.X) * ctx.AttackDir(team);
            return depth >= -r && depth <= BoxDepth + r && DMath.Abs(ball.Y - goal.Y) <= BoxHalfWidth + r;
        }

        public const float BoxDepth = 16.5f, BoxHalfWidth = 20.16f;

        /// <summary>FR-82 사용 부위 (대체): 높이로 머리·가슴·허벅지, 그 아래는 몸 방향 기준 공이 있는 쪽 발.</summary>
        public static BodyPart PartFor(in PlayerMotionState s, Vec3 ball)
        {
            if (ball.Z >= 1.5f) return BodyPart.Head;
            if (ball.Z >= 1.0f) return BodyPart.Chest;
            if (ball.Z >= 0.5f) return BodyPart.Thigh;
            var cross = s.Facing.X * (ball.Y - s.Position.Y) - s.Facing.Y * (ball.X - s.Position.X);
            return cross > 0f ? BodyPart.LeftFoot : BodyPart.RightFoot;
        }
    }

    /// <summary>단계 9 — 발행: 이번 틱 이벤트를 정렬해 로그·구독자로 (FR-01 ⑩).</summary>
    public sealed class PublishStage : StatelessStage
    {
        public override StageId Id => StageId.Publish;
        public override void Execute(MatchContext ctx) => ctx.Publisher.Flush(ctx.Log, ctx.View);
    }
}
