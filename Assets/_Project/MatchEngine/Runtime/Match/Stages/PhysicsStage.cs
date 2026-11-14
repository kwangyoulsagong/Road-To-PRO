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
    /// 단계 5가 정한 공 동작은 서브스텝 0 전에 008 예약 킥으로 넘기고, 킥 준비 시간이 지나 공이 발 도달 거리 안인 서브스텝에 실행한다(008 FR-65).
    /// 매 서브스텝 공 사건(라인 통과·골대)을 서브스텝 시각과 함께 보드에 남겨 심판 단계로 넘긴다. 느슨한 공은 008 수신·인터셉트 판정(FR-45·67).
    /// </summary>
    public sealed class PhysicsStage : StatelessStage
    {
        public const float BallCarryOffset = 0.45f;
        /// <summary>다이빙 중 GK가 잡은 공이 손 위치로 따라가는 서브스텝당 최대 이동 (m, 15 m/s).</summary>
        public const float KeeperHoldStep = 0.15f;

        public override StageId Id => StageId.Physics;

        /// <summary>테스트용 서브스텝 카운터 (AC-02: 틱당 정확히 10회).</summary>
        public long SubstepsRun { get; private set; }

        static bool HasBoundary(MatchContext ctx)
        {
            for (int i = 0; i < ctx.BallEvents.Count; i++) if (ctx.BallEvents[i].Kind == BallEventKind.BoundaryCrossed) return true;
            return false;
        }

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
                if (sub == 0 && action.Kind != BallActionKind.None) ctx.Actions.Schedule(ctx, in action, abs);
                ctx.Actions.OnSubstep(ctx, abs, sub); // 008 FR-65: 예약 킥 접촉 서브스텝
                ctx.Actions.Duels.OnTackles(ctx, abs, sub); // 070 FR-10·11: 예약 태클 접촉 서브스텝
                k.Step();
                ctx.Actions.Duels.OnDribble(ctx, abs, sub); // 070 FR-02·03: 드리블 터치·이격·탈취
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
                    else if (ctx.Actions.Duels.ControlledBallPosition(ctx, abs, target, out var dp)) target = dp; // 070: 이격 중 굴림 위치·되돌아가기
                    BallCommands.SetControlledPosition(ref ball, target);
                }
                ctx.BallEvents.Clear();
                BallStepper.Step(ref ball, ctx.BallParams, ctx.BallEvents, out _);
                if (!crossed && ctx.Actions.Shots.InFlight && !HasBoundary(ctx)) ctx.Actions.Shots.OnSubstep(ctx, abs, sub); // 071 FR-15: 슛 블록 (공 적분 뒤, 라인 앞 — 라인을 넘은 서브스텝은 판정 안 함, 결정론 리뷰 M1)
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
                if (!crossed) ctx.Actions.Aerials.OnSubstep(ctx, abs, sub); // 071 FR-03·05: 공중볼 경합·헤더 (GK·수신 앞)
                else if (ctx.Actions.Aerials.IsArmed || ctx.Actions.Aerials.IsDecided) ctx.Actions.Aerials.Cancel();
                if (!crossed) ctx.Keepers.OnSubstep(ctx, abs, sub); // 011 FR-15·17: 다이빙 진행·슛 감시·선방 적용
                if (!crossed) ctx.Actions.Resolve(ctx, abs, sub); // 008 FR-45·67: 수신·인터셉트·첫 터치
                if (!crossed) ctx.Actions.Duels.OnShield(ctx, abs, sub); // 070 FR-20: 몸싸움
                rec.AfterSubstep(abs);
                SubstepsRun++;
            }
            rec.EndTick();
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
        static bool HasBoundary(MatchContext ctx)
        {
            for (int i = 0; i < ctx.BallEvents.Count; i++) if (ctx.BallEvents[i].Kind == BallEventKind.BoundaryCrossed) return true;
            return false;
        }

        public override void Execute(MatchContext ctx)
        {
            var from = ctx.Log.Count;
            ctx.Publisher.Flush(ctx.Log, ctx.View);
            ctx.Actions.AfterPublish(ctx.Log, from); // 008 FR-72: 상세 기록 ↔ 이벤트 Seq
        }
    }
}
