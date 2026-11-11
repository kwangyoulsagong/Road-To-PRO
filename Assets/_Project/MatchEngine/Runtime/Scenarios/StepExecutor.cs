using RoadToPro.Core.Math;
using RoadToPro.Core.Pitch;
using RoadToPro.Core.Random;
using RoadToPro.MatchEngine.Ball;
using RoadToPro.MatchEngine.Match;
using RoadToPro.MatchEngine.Match.Flow;
using RoadToPro.MatchEngine.Match.Pipeline;
using RoadToPro.MatchEngine.Match.Stages;

namespace RoadToPro.MatchEngine.Scenarios
{
    /// <summary>
    /// 단계 동사 → "이동 목표 + 행동 의도" (FR-40). 시나리오는 결과를 정하지 않는다 — 공 동작을 물리에 넘기고 결과는 런타임이 관찰한다.
    /// 공 동작의 성공 판정은 008·011 자리이고, 지금은 대체 구현(004 역산 + 물리)이다.
    /// </summary>
    public sealed class StepExecutor
    {
        readonly ScenarioLibrary _lib;
        readonly ScenarioParams _p;
        readonly SeedRng _rng;
        readonly int _nearPost, _farPost, _penaltySpot, _box;

        public StepExecutor(ScenarioLibrary lib, ScenarioParams p, SeedRng rng)
        {
            _lib = lib;
            _p = p;
            _rng = rng;
            _nearPost = lib.Zones.IndexOf("near_post_6yd");
            _farPost = lib.Zones.IndexOf("far_post_6yd");
            _penaltySpot = lib.Zones.IndexOf("penalty_spot");
            _box = lib.Zones.IndexOf("BOX");
        }

        public static Vec2 Perspective(MatchContext ctx, int team, Vec2 meters) =>
            PitchCoords.ToPerspective(PitchCoords.ToNormalized(meters, ctx.Pitch), team == 0 ? TeamSide.Home : TeamSide.Away, ctx.Flow.Period);

        static float Clamp01(float v) => v < 0.01f ? 0.01f : v > 0.99f ? 0.99f : v;

        Vec2 RegionCenter(int region, ScenarioRun run, Vec2 ball, Vec2 fallback) =>
            region >= 0 ? _lib.Zones.Center(region, run.Def.Mirror, ball.Y) : fallback;

        /// <summary>단계 시작 때 목표를 한 번 정한다 (틱마다 다시 계산하면 떨린다). 좌표는 시나리오 팀 관점 정규화.</summary>
        public void BeginStep(MatchContext ctx, ScenarioRun run, int role)
        {
            var st = run.CurrentStep(role);
            var slot = run.Slot[role];
            var me = Perspective(ctx, run.Team, ctx.SlotState(slot).Position);
            var ball = Perspective(ctx, run.Team, ctx.Ball.Position.XY);
            run.TrackSlot[role] = ResolveTrackSlot(ctx, run, st, ball);
            run.Target[role] = TargetOf(ctx, run, st, me, ball, role);
        }

        int ResolveTrackSlot(MatchContext ctx, ScenarioRun run, ScenarioStep st, Vec2 ball)
        {
            switch (st.TargetKind)
            {
                case StepTargetKind.Role: return run.Slot[st.TargetRef];
                case StepTargetKind.Receiver: return ctx.Ball.Phase == BallPhase.Controlled ? ctx.Ball.OwnerId : -1;
                case StepTargetKind.OppPosition: return NearestOpponent(ctx, run, st.OppPositions, ball);
                default: return -1;
            }
        }

        int NearestOpponent(MatchContext ctx, ScenarioRun run, PositionMask mask, Vec2 ball)
        {
            var opp = 1 - run.Team;
            int best = -1;
            float bestD = float.MaxValue;
            for (int s = opp * Squad.SlotsPerTeam; s < (opp + 1) * Squad.SlotsPerTeam; s++)
            {
                if (!ctx.IsSlotActive(s) || (run.Roles.RoleOf(ctx, s) & mask) == 0) continue;
                var d = Vec2.DistanceSq(Perspective(ctx, run.Team, ctx.SlotState(s).Position), ball);
                if (d < bestD) { bestD = d; best = s; }
            }
            return best;
        }

        Vec2 TargetOf(MatchContext ctx, ScenarioRun run, ScenarioStep st, Vec2 me, Vec2 ball, int role)
        {
            var l = ctx.Pitch.LengthM;
            var w = ctx.Pitch.WidthM;
            switch (st.TargetKind)
            {
                case StepTargetKind.Region:
                    return _lib.Zones.Center(st.TargetRef, run.Def.Mirror, ball.Y);
                case StepTargetKind.Role:
                case StepTargetKind.Receiver:
                case StepTargetKind.OppPosition:
                    var track = run.TrackSlot[role];
                    if (track >= 0) return Perspective(ctx, run.Team, ctx.SlotState(track).Position);
                    break;
            }

            var ahead = _p.MoveAheadM / l;
            var wideSign = ball.Y < 0.5f ? -1f : 1f;
            var off = _p.LaneOffsetM / w;
            switch (st.Class)
            {
                case VerbClass.Carry:
                case VerbClass.TakeOn:
                    return CarryTarget(run, st, me, ball, l, w);
            }
            switch (st.Verb)
            {
                case ScenarioVerb.CheckIn: return Toward(me, ball, 8f / l);
                case ScenarioVerb.CheckOut: return Toward(me, ball, -8f / l);
                case ScenarioVerb.Drop: return Clamp(new Vec2(me.X - 10f / l, me.Y));
                case ScenarioVerb.Spin:
                case ScenarioVerb.RunInBehind:
                case ScenarioVerb.BlindSideRun:
                    return Clamp(new Vec2(ball.X + ahead, me.Y));
                case ScenarioVerb.ThirdManRun: return Clamp(new Vec2(ball.X + ahead, ball.Y));
                case ScenarioVerb.DiagonalRun: return Clamp(new Vec2(ball.X + ahead, me.Y + (0.5f - me.Y) * 0.5f));
                case ScenarioVerb.Overlap: return Clamp(new Vec2(ball.X + ahead, ball.Y + wideSign * off));
                case ScenarioVerb.Underlap:
                case ScenarioVerb.UnderlapSupport:
                    return Clamp(new Vec2(ball.X + ahead, ball.Y - wideSign * off));
                case ScenarioVerb.DecoyPullCb: return Clamp(new Vec2(ball.X + ahead, 0.5f));
                case ScenarioVerb.NearPostRun: return RegionCenter(_nearPost, run, ball, me);
                case ScenarioVerb.FarPostRun: return RegionCenter(_farPost, run, ball, me);
                case ScenarioVerb.ArriveLate: return RegionCenter(_penaltySpot, run, ball, me);
                case ScenarioVerb.CrashBox: return RegionCenter(_box, run, ball, me);
                default:
                    return st.LaneRegion >= 0 ? new Vec2(me.X, _lib.Zones.Center(st.LaneRegion, run.Def.Mirror, ball.Y).Y) : me;
            }
        }

        Vec2 CarryTarget(ScenarioRun run, ScenarioStep st, Vec2 me, Vec2 ball, float l, float w)
        {
            var dist = st.DistanceM > 0f ? st.DistanceM : st.Class == VerbClass.TakeOn ? _p.TakeOnAheadM : _p.MoveAheadM;
            var y = me.Y;
            if (st.LaneRegion >= 0) y = _lib.Zones.Center(st.LaneRegion, run.Def.Mirror, ball.Y).Y;
            else if (st.Direction != 0)
            {
                var inward = me.Y < 0.5f ? 1f : -1f;
                y = me.Y - st.Direction * inward * _p.LaneOffsetM / w;
            }
            return Clamp(new Vec2(me.X + dist / l, y));
        }

        static Vec2 Toward(Vec2 from, Vec2 to, float amount)
        {
            var d = to - from;
            var len = d.Length();
            return len < 1e-5f ? from : Clamp(from + d * (amount / len));
        }

        static Vec2 Clamp(Vec2 v) => new Vec2(Clamp01(v.X), Clamp01(v.Y));

        /// <summary>실행 중 단계의 이번 결정. 공 동작이 있으면 보드에 쓴다.</summary>
        public Decision Decide(MatchContext ctx, ScenarioRun run, int role, ref bool hasAction)
        {
            var slot = run.Slot[role];
            var st = run.CurrentStep(role);
            ref readonly var me = ref ctx.SlotState(slot);
            var owns = ctx.Ball.Phase == BallPhase.Controlled && ctx.Ball.OwnerId == slot;
            var targetM = ctx.FromTeamPerspective(run.Team, run.Target[role]);
            var stand = new Decision { Kind = DecisionKind.Stand, TargetSlot = -1 };
            var move = new Decision { Kind = DecisionKind.MoveTo, Point = targetM, TargetSlot = -1 };

            switch (st.Class)
            {
                case VerbClass.Move:
                    return move;
                case VerbClass.Carry:
                case VerbClass.TakeOn:
                    if (!owns) return stand;
                    return new Decision { Kind = DecisionKind.Carry, Point = targetM, TargetSlot = -1, MaxSpeed = ctx.Kinematics.Profile(slot).MaxSpeed * ctx.Params.CarrySpeedRatio };
                case VerbClass.Pass:
                    if (owns) TryKick(ctx, run, role, st, ref hasAction);
                    return stand;
                case VerbClass.Shoot:
                    if (owns) TryShoot(ctx, run, role, ref hasAction);
                    return stand;
                case VerbClass.Defend:
                    return DecideDefend(ctx, run, role, st, ref hasAction);
                default:
                    if (ScenarioVerbs.IsReceive(st.Verb) && ctx.Ball.Phase != BallPhase.Controlled)
                        return new Decision { Kind = DecisionKind.Chase, Point = ctx.Ball.Position.XY + ctx.Ball.Velocity.XY * 0.3f, TargetSlot = -1 };
                    return move;
            }
        }

        Decision DecideDefend(MatchContext ctx, ScenarioRun run, int role, ScenarioStep st, ref bool hasAction)
        {
            var slot = run.Slot[role];
            var track = run.TrackSlot[role];
            ref var ball = ref ctx.Ball;
            if (st.Verb == ScenarioVerb.Engage || st.Verb == ScenarioVerb.CurveRun)
            {
                var owner = ball.Phase == BallPhase.Controlled ? ball.OwnerId : -1;
                if (owner < 0 || Squad.TeamOfSlot(owner) == Squad.TeamOfSlot(slot))
                    return new Decision { Kind = DecisionKind.Stand, TargetSlot = -1 };
                if (st.Verb == ScenarioVerb.Engage && !hasAction && TryTackle(ctx, slot, owner))
                {
                    hasAction = true;
                    return new Decision { Kind = DecisionKind.Stand, TargetSlot = -1 };
                }
                return new Decision { Kind = DecisionKind.Chase, TargetSlot = owner };
            }
            var pointM = track >= 0 && ctx.IsSlotControllable(track)
                ? ctx.SlotState(track).Position
                : ctx.FromTeamPerspective(run.Team, run.Target[role]);
            return new Decision { Kind = DecisionKind.MoveTo, Point = pointM, TargetSlot = -1 };
        }

        bool TryTackle(MatchContext ctx, int slot, int owner)
        {
            ref readonly var me = ref ctx.SlotState(slot);
            var away = ctx.SlotState(owner).Position - me.Position;
            if (away.LengthSq() > ctx.Params.TackleRange * ctx.Params.TackleRange || !_rng.Bernoulli(ctx.Params.TackleProb)) return false;
            var dir = away.Normalized();
            if (dir == Vec2.Zero) dir = new Vec2(ctx.AttackDir(Squad.TeamOfSlot(slot)), 0f);
            ctx.Board.SetBallAction(new BallAction
            {
                Kind = BallActionKind.Clearance, KickerSlot = slot, TargetSlot = -1,
                Velocity = new Vec3(dir.X * 6f, dir.Y * 6f, 0f), Part = PhysicsStage.PartFor(in me, ctx.Ball.Position),
            });
            return true;
        }

        bool ReadyToKick(MatchContext ctx, ScenarioRun run, int role, ref bool hasAction, out bool restart)
        {
            restart = ctx.Flow.State == FlowState.DeadBall;
            if (hasAction || run.PendingRole == role) return false;
            if (restart) return ctx.Tick >= ctx.Flow.ReadyTick && ctx.Tick - run.LastKickTick >= 10;
            return ctx.Tick - ctx.PossessionStartTick >= ctx.Params.HoldTicks;
        }

        void TryKick(MatchContext ctx, ScenarioRun run, int role, ScenarioStep st, ref bool hasAction)
        {
            if (!ReadyToKick(ctx, run, role, ref hasAction, out var restart)) return;
            var slot = run.Slot[role];
            var targetSlot = st.TargetKind == StepTargetKind.Role ? run.Slot[st.TargetRef] : -1;
            Vec2 aim;
            if (targetSlot >= 0 && ctx.IsSlotControllable(targetSlot)) aim = ctx.SlotState(targetSlot).Position;
            else if (st.TargetKind == StepTargetKind.Region) aim = ctx.FromTeamPerspective(run.Team, run.Target[role]);
            else
            {
                if (st.HasAlt && ScenarioVerbs.ClassOf(st.Alt) == VerbClass.Shoot) { TryShoot(ctx, run, role, ref hasAction); return; }
                return;
            }

            ref readonly var me = ref ctx.SlotState(slot);
            var from = ctx.Ball.Position.XY;
            var d = aim - from;
            var dist = d.Length();
            if (dist < 1f) return;
            var u = d.Normalized();
            Vec3 velocity;
            if (ScenarioVerbs.IsAir(st.Verb))
            {
                var flight = dist / _p.AerialSpeed;
                velocity = new Vec3(u.X * _p.AerialSpeed, u.Y * _p.AerialSpeed, DMath.Abs(ctx.BallParams.Gravity) * flight * 0.5f);
            }
            else
            {
                var inv = BallInverse.GroundSpeedFor(ctx.BallParams, dist, ctx.Params.PassExtraSpeed);
                var speed = inv.Status == BallInverseStatus.Ok ? inv.Speed : DMath.Min(dist * 0.9f + 5f, 25f);
                velocity = new Vec3(u.X * speed, u.Y * speed, 0f);
            }
            hasAction = true;
            run.PendingRole = role;
            run.KickTick = ctx.Tick;
            run.LastKickTick = ctx.Tick;
            ctx.Board.SetBallAction(new BallAction
            {
                Kind = BallActionKind.Pass, KickerSlot = slot, TargetSlot = targetSlot, Velocity = velocity,
                Part = PhysicsStage.PartFor(in me, ctx.Ball.Position), IsRestart = restart,
            });
        }

        void TryShoot(MatchContext ctx, ScenarioRun run, int role, ref bool hasAction)
        {
            if (!ReadyToKick(ctx, run, role, ref hasAction, out var restart)) return;
            var slot = run.Slot[role];
            ref readonly var me = ref ctx.SlotState(slot);
            var goal = ctx.OpponentGoal(run.Team);
            var half = BallParams.GoalWidth * 0.5f - 0.4f;
            var dir = (new Vec2(goal.X, goal.Y + _rng.Range(-half, half)) - ctx.Ball.Position.XY).Normalized();
            var speed = ctx.Params.ShotSpeed;
            hasAction = true;
            run.PendingRole = role;
            run.KickTick = ctx.Tick;
            run.LastKickTick = ctx.Tick;
            ctx.Board.SetBallAction(new BallAction
            {
                Kind = BallActionKind.Shot, KickerSlot = slot, TargetSlot = -1,
                Velocity = new Vec3(dir.X * speed, dir.Y * speed, _rng.Range(0.3f, 3f)),
                Part = PhysicsStage.PartFor(in me, ctx.Ball.Position), IsRestart = restart,
            });
        }
    }
}
