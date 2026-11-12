using System;
using RoadToPro.Core.Hashing;
using RoadToPro.Core.Math;
using RoadToPro.Core.Random;
using RoadToPro.Core.Serialization;
using RoadToPro.MatchEngine.Ball;
using RoadToPro.MatchEngine.Match;
using RoadToPro.MatchEngine.Match.Events;
using RoadToPro.MatchEngine.Match.Flow;
using RoadToPro.MatchEngine.Match.Pipeline;
using RoadToPro.MatchEngine.Match.Stages.StandIn;
using RoadToPro.MatchEngine.Tactics;

namespace RoadToPro.MatchEngine.Scenarios
{
    /// <summary>실행 중 시나리오 하나의 상태. 팀마다 하나.</summary>
    public sealed class ScenarioRun
    {
        public const int MaxRoles = 9;
        public const byte Waiting = 0, Running = 1, Done = 2;

        public bool Active;
        public ScenarioDefinition Def;
        public int Team;
        public long StartTick, ZeroTick, StepTimeBase;
        public int Chain;
        public bool Forced, Resumed;
        public int PendingRole = -1;
        public long KickTick = -1, LastKickTick = long.MinValue / 2;
        public IScenarioRoleSource Roles;

        public readonly int[] Slot = new int[MaxRoles], Player = new int[MaxRoles], Step = new int[MaxRoles], TrackSlot = new int[MaxRoles];
        public readonly long[] StepStart = new long[MaxRoles];
        public readonly byte[] State = new byte[MaxRoles];
        public readonly Vec2[] Target = new Vec2[MaxRoles];

        public ScenarioStep CurrentStep(int role) => Def.Cast[role].Steps[Step[role]];
        public bool WaitingForKick => Def.IsSetPiece && !Resumed;
    }

    /// <summary>강제 선택 요청 (FR-53). 검증·미리보기 전용.</summary>
    public sealed class ScenarioForceRequest
    {
        public string Id;
        public int Team;
        /// <summary>이 틱 이후 처음 조건이 맞을 때 시작한다.</summary>
        public long FromTick;
        /// <summary>출시 빌드 경기는 false다. false면 요청을 거부하고 사유를 남긴다.</summary>
        public bool Allowed;
    }

    /// <summary>
    /// 시나리오 런타임 (specs/013). 수명 주기(종료·분기·체이닝)는 단계 2 직전에 <see cref="Advance"/>, 새 선택은 단계 5에서 <see cref="Select"/>를 부른다.
    /// 그래서 끝난 배역은 같은 틱에 형태 목표를 받는다 (FR-33, AC-08). 처리 순서는 팀 0 → 1, 정의 번호 오름차순이다.
    /// </summary>
    public sealed class ScenarioRuntime : IScenarioCastSource, IDecisionOverride, IScenarioRoleSource
    {
        readonly ScenarioLibrary _lib;
        readonly ScenarioParams _p;
        readonly ScenarioInputProviders _in;
        readonly IScenarioDiagnostics _diag;
        readonly SeedRng _rng;
        readonly StepExecutor _exec;
        readonly CastAssigner _assigner = new CastAssigner();
        readonly CastFrame _frame = new CastFrame();
        readonly RarityGate _gate;
        readonly ScenarioAggregator _stats;
        readonly ScenarioForceRequest _force;
        readonly ScenarioRun[] _runs = { new ScenarioRun { Team = 0 }, new ScenarioRun { Team = 1 } };
        readonly int[] _castRun = new int[Squad.Slots];
        readonly bool[] _castOpp = new bool[Squad.Slots];
        readonly int[] _castRole = new int[Squad.Slots];
        readonly long[][] _lastUsed = new long[2][];
        readonly int[] _lastEndOrdinal = { -1, -1 };
        readonly long[] _lastEndTick = { -1, -1 };

        readonly int[] _slotsBuf = new int[ScenarioRun.MaxRoles];
        readonly int[] _candidates;
        readonly float[] _scores;
        readonly float[] _space = new float[SpaceFields.Count];
        readonly int[] _diagSteps = new int[ScenarioRun.MaxRoles], _diagSlots = new int[ScenarioRun.MaxRoles];

        int _prevOwner = -1;
        int _prevDeadBallId = -1;
        long _prevKickSubstep = -1;
        bool _forceDone;

        IScenarioRoleSource _roles;
        /// <summary>마지막으로 알려진 역할. 배역 중이면 형태 슬롯이 비므로 여기서 읽는다 (스냅샷·해시에 포함).</summary>
        readonly PositionMask[] _roleCache = new PositionMask[Squad.Slots];
        public ScenarioLibrary Library => _lib;
        public ScenarioAggregator Stats => _stats;
        public ScenarioRun Run(int team) => _runs[team];
        /// <summary>측정 전용. 결과에 영향을 주지 않는다.</summary>
        public IScenarioTimer Timer;

        public ScenarioRuntime(ScenarioData data, MatchSetup setup, ScenarioInputProviders inputs, IScenarioDiagnostics diagnostics, ScenarioForceRequest force)
        {
            _lib = data.Library;
            _p = new ScenarioParams(data.Tuning);
            _in = inputs ?? new ScenarioInputProviders();
            _diag = diagnostics;
            _force = force;
            _rng = RngStreams.Create(setup.Seed, RngStreamId.ScenarioSelect);
            _exec = new StepExecutor(_lib, _p, _rng);
            _gate = new RarityGate(_lib.TotalCount);
            _stats = new ScenarioAggregator(_lib.TotalCount);
            _candidates = new int[_lib.TotalCount];
            _scores = new float[_lib.TotalCount];
            for (int t = 0; t < 2; t++)
            {
                _lastUsed[t] = new long[_lib.BasePatternCount];
                Array.Fill(_lastUsed[t], -1L);
            }
            Array.Fill(_castRun, -1);
            _roles = _in.Roles;
        }

        /// <summary>대체 역할 소스를 연결한다 (ShapeStage가 만들어진 뒤).</summary>
        public void BindRoles(IScenarioRoleSource roles)
        {
            if (_roles == null) _roles = roles;
            _runs[0].Roles = this;
            _runs[1].Roles = this;
        }

        /// <summary>슬롯의 역할. 소스가 모르면(배역 중) 마지막 값.</summary>
        public PositionMask RoleOf(MatchContext ctx, int slot)
        {
            var r = _roles.RoleOf(ctx, slot);
            if (r != PositionMask.None) _roleCache[slot] = r;
            return _roleCache[slot];
        }

        // ── 009 · 폴백 계약 ──

        public bool IsCast(int slot) => _castRun[slot] >= 0;
        public bool IsLineSyncAdvance(int team) => false;

        /// <summary>배역 선수의 이번 결정 (FR-40). 세트피스 키커는 공을 잡을 때까지 폴백이 움직인다.</summary>
        public bool TryDecide(MatchContext ctx, int slot, ref bool hasAction, out Decision d)
        {
            d = default;
            var team = _castRun[slot];
            if (team < 0) return false;
            var run = _runs[team];
            var role = _castRole[slot];
            var cast = run.Def.Cast[role];
            if (run.State[role] == ScenarioRun.Done) { d = MoveToTarget(ctx, run, role); return true; }

            if (run.WaitingForKick)
            {
                if (cast.IsTaker)
                {
                    var owns = ctx.Ball.Phase == BallPhase.Controlled && ctx.Ball.OwnerId == slot;
                    if (!owns || ctx.Tick < ctx.Flow.ReadyTick) return false;
                }
                else if (run.State[role] == ScenarioRun.Waiting)
                {
                    d = SetupDecision(ctx, run, role, slot);
                    return true;
                }
            }

            if (run.State[role] == ScenarioRun.Waiting) { d = new Decision { Kind = DecisionKind.Stand, TargetSlot = -1 }; return true; }
            d = _exec.Decide(ctx, run, role, ref hasAction);
            return true;
        }

        /// <summary>슬롯의 배역 (010 의도 패킷용, 부수효과 없음). 배역이 아니면 false.</summary>
        public bool TryGetCast(int slot, out ScenarioRun run, out int role)
        {
            var team = _castRun[slot];
            run = team >= 0 ? _runs[team] : null;
            role = team >= 0 ? _castRole[slot] : -1;
            return team >= 0;
        }

        /// <summary>단계 제한 시간 (틱).</summary>
        public long StepLimitTicks => _p.StepLimitTicks;

        /// <summary>
        /// 기회주의 이탈 (010 FR-94): 그 선수의 배역만 해제한다(다른 배역·시나리오는 그대로). 해제 이벤트 + <see cref="SimEventKind.IntentOpportunistic"/>.
        /// 배역이 아니면 false.
        /// </summary>
        public bool ReleaseOpportunistic(MatchContext ctx, int slot)
        {
            var team = _castRun[slot];
            if (team < 0) return false;
            var run = _runs[team];
            var role = _castRole[slot];
            EmitReleased(ctx, run, role);
            Emit(ctx, SimEventKind.IntentOpportunistic, run, role, slot, run.Player[role]);
            ReleaseRole(run, role);
            return true;
        }

        /// <summary>배역 의도 지점 (역할 목표, 부수효과 없음 — 011 FR-94 GK 의도).</summary>
        public bool TryReadIntent(MatchContext ctx, int slot, out Vec2 point)
        {
            point = default;
            var team = _castRun[slot];
            if (team < 0) return false;
            var run = _runs[team];
            point = ctx.FromTeamPerspective(run.Team, run.Target[_castRole[slot]]);
            return true;
        }

        Decision MoveToTarget(MatchContext ctx, ScenarioRun run, int role) =>
            new Decision { Kind = DecisionKind.MoveTo, Point = ctx.FromTeamPerspective(run.Team, run.Target[role]), TargetSlot = -1 };

        /// <summary>재개 전 배역 위치: 첫 단계의 `from` 구역, 없으면 제자리 (FR-46).</summary>
        Decision SetupDecision(MatchContext ctx, ScenarioRun run, int role, int slot)
        {
            var st = run.CurrentStep(role);
            if (st.FromRegion < 0) return new Decision { Kind = DecisionKind.Stand, TargetSlot = -1 };
            var ballP = StepExecutor.Perspective(ctx, run.Team, ctx.Ball.Position.XY);
            var p = _lib.Zones.Center(st.FromRegion, run.Def.Mirror, ballP.Y);
            return new Decision { Kind = DecisionKind.MoveTo, Point = ctx.FromTeamPerspective(run.Team, p), TargetSlot = -1 };
        }

        // ── 수명 주기 (단계 2 직전) ──

        public void Advance(MatchContext ctx)
        {
            Timer?.Begin();
            TrackCounterShots(ctx);
            for (int t = 0; t < 2; t++)
            {
                var run = _runs[t];
                if (run.Active) AdvanceRun(ctx, run);
                if (run.Active && _diag != null) ReportRunning(ctx, run);
            }
            Timer?.End();
        }

        void ReportRunning(MatchContext ctx, ScenarioRun run)
        {
            for (int i = 0; i < run.Def.Cast.Length; i++) { _diagSteps[i] = run.Step[i]; _diagSlots[i] = run.Slot[i]; }
            _diag.OnRunning(run.Team, ctx.Tick, run.Def.Id, run.Forced, run.Def.Cast.Length, _diagSlots, _diagSteps);
        }

        void TrackCounterShots(MatchContext ctx)
        {
            if (ctx.LastKickSubstep == _prevKickSubstep) return;
            _prevKickSubstep = ctx.LastKickSubstep;
            if (ctx.LastKickKind != (byte)BallActionKind.Shot || ctx.LastKickSlot < 0) return;
            var victim = 1 - Squad.TeamOfSlot(ctx.LastKickSlot);
            var o = _lastEndOrdinal[victim];
            if (o >= 0 && ctx.Tick - _lastEndTick[victim] <= _p.CounterWindowTicks)
            {
                _stats.OnCounterAllowed(o);
                _lastEndOrdinal[victim] = -1;
            }
        }

        void AdvanceRun(MatchContext ctx, ScenarioRun run)
        {
            if (CheckBreak(ctx, run)) return;
            if (run.WaitingForKick && ctx.Flow.InPlay)
            {
                run.Resumed = true;
                run.ZeroTick = ctx.Tick;
                run.StartTick = ctx.Tick;
                Emit(ctx, SimEventKind.ScenarioResume, run, 0, 0);
            }
            if (!CheckCast(ctx, run)) return;
            if (CheckSituations(ctx, run)) return;

            var def = run.Def;
            var elapsed = ctx.Tick - run.ZeroTick;
            for (int r = 0; r < def.Cast.Length; r++)
            {
                if (run.Slot[r] < 0 || run.State[r] == ScenarioRun.Done) continue;
                if (run.State[r] == ScenarioRun.Waiting)
                {
                    var st = run.CurrentStep(r);
                    if (!CanStart(run, r, st, elapsed)) continue;
                    run.State[r] = ScenarioRun.Running;
                    run.StepStart[r] = ctx.Tick;
                    _exec.BeginStep(ctx, run, r);
                }
                var outcome = Evaluate(ctx, run, r);
                if (outcome == StepOutcome.None) continue;
                if (FinishStep(ctx, run, r, outcome)) return;
            }

            for (int r = 0; r < def.Cast.Length; r++)
                if (run.Slot[r] >= 0 && run.State[r] != ScenarioRun.Done) return;
            if (run.PendingRole < 0) End(ctx, run, ScenarioEndReason.Completed);
        }

        static bool CanStart(ScenarioRun run, int r, ScenarioStep st, long elapsedTicks)
        {
            if (float.IsNaN(st.T)) return !run.WaitingForKick || run.Def.Cast[r].IsTaker;
            if (run.WaitingForKick && st.T >= 0f) return run.Def.Cast[r].IsTaker;
            return elapsedTicks >= ScenarioParams.Ticks(st.T);
        }

        /// <summary>시나리오가 깨지는 경우 (FR-43). 종료했으면 true.</summary>
        bool CheckBreak(MatchContext ctx, ScenarioRun run)
        {
            var flow = ctx.Flow;
            if (flow.State != FlowState.InPlay)
            {
                if (flow.State == FlowState.DeadBall && run.WaitingForKick) return false;
                End(ctx, run, flow.State == FlowState.DeadBall ? ScenarioEndReason.BallDead : ScenarioEndReason.Whistle);
                return true;
            }
            ref readonly var ball = ref ctx.Ball;
            if (ball.Phase == BallPhase.Controlled && ball.OwnerId >= 0 && run.Def.Side == ScenarioSide.Attack && Squad.TeamOfSlot(ball.OwnerId) != run.Team && !run.WaitingForKick)
            {
                End(ctx, run, run.PendingRole >= 0 ? ScenarioEndReason.Interception : ScenarioEndReason.Tackle);
                return true;
            }
            var limit = run.Def.TimeLimitS > 0f ? ScenarioParams.Ticks(run.Def.TimeLimitS) : _p.ScenarioLimitTicks;
            if (!run.WaitingForKick && ctx.Tick - run.StartTick >= limit)
            {
                End(ctx, run, ScenarioEndReason.ScenarioTimeout);
                return true;
            }
            return false;
        }

        /// <summary>배역 선수 이탈 처리 (FR-43, AC-11). 시나리오가 끝났으면 false.</summary>
        bool CheckCast(MatchContext ctx, ScenarioRun run)
        {
            var cast = run.Def.Cast;
            for (int r = 0; r < cast.Length; r++)
            {
                var s = run.Slot[r];
                if (s < 0) continue;
                if (ctx.IsSlotControllable(s) && ctx.PlayerAtSlot(s) == run.Player[r]) continue; // 058: 걸어 나가면 배역 해제
                if (cast[r].Required)
                {
                    End(ctx, run, ScenarioEndReason.CastLost);
                    return false;
                }
                EmitReleased(ctx, run, r);
                ReleaseRole(run, r);
            }
            return true;
        }

        void EmitReleased(MatchContext ctx, ScenarioRun run, int role) =>
            Emit(ctx, SimEventKind.ScenarioCastReleased, run, role, run.Slot[role], run.Player[role]);

        void ReleaseRole(ScenarioRun run, int role)
        {
            var s = run.Slot[role];
            if (s < 0) return;
            _castRun[s] = -1;
            _castOpp[s] = false;
            run.Slot[role] = -1;
        }

        bool CheckSituations(MatchContext ctx, ScenarioRun run)
        {
            var branches = run.Def.Branches;
            for (int i = 0; i < branches.Length; i++)
            {
                var b = branches[i];
                if (!b.HasSituation || !Condition(ctx, run, b.Situation)) continue;
                if (b.SituationTarget.Kind == BranchTargetKind.JumpStep && !CanJump(ctx, run, b.SituationTarget)) continue;
                if (b.SituationTarget.Kind == BranchTargetKind.JumpStep && run.Step[b.SituationTarget.JumpRole] == b.SituationTarget.JumpStep) continue;
                return Apply(ctx, run, b.SituationTarget, -1, -1, StepOutcome.Success);
            }
            return false;
        }

        /// <summary>끼어드는 단계는 공 동작이면 그 배역이 공을 가졌을 때만 시작한다.</summary>
        static bool CanJump(MatchContext ctx, ScenarioRun run, BranchTarget t)
        {
            var slot = run.Slot[t.JumpRole];
            if (slot < 0) return false;
            var cls = run.Def.Cast[t.JumpRole].Steps[t.JumpStep].Class;
            if (cls != VerbClass.Pass && cls != VerbClass.Shoot) return true;
            return ctx.Ball.Phase == BallPhase.Controlled && ctx.Ball.OwnerId == slot;
        }

        bool Condition(MatchContext ctx, ScenarioRun run, BranchCondition c)
        {
            switch (c)
            {
                case BranchCondition.Always: return true;
                case BranchCondition.Regain:
                    return ctx.Ball.Phase == BallPhase.Controlled && ctx.Ball.OwnerId >= 0 && Squad.TeamOfSlot(ctx.Ball.OwnerId) == run.Team;
                case BranchCondition.ShotLaneOpen: return ShotLaneOpen(ctx, run);
                default: return false; // 007·008·011 입력이 있어야 판정할 수 있다 (대체 입력 동안 거짓)
            }
        }

        bool ShotLaneOpen(MatchContext ctx, ScenarioRun run)
        {
            const float LaneM = 1.5f;
            ref readonly var ball = ref ctx.Ball;
            if (ball.Phase != BallPhase.Controlled || ball.OwnerId < 0 || Squad.TeamOfSlot(ball.OwnerId) != run.Team) return false;
            var from = ctx.SlotState(ball.OwnerId).Position;
            var to = ctx.OpponentGoal(run.Team);
            var d = to - from;
            var len = d.Length();
            if (len > ctx.Params.ShotRange || len < 1f) return false;
            var u = d * (1f / len);
            var opp = 1 - run.Team;
            for (int s = opp * Squad.SlotsPerTeam; s < (opp + 1) * Squad.SlotsPerTeam; s++)
            {
                if (!ctx.IsSlotActive(s)) continue;
                var r = ctx.SlotState(s).Position - from;
                var along = r.X * u.X + r.Y * u.Y;
                if (along <= 0f || along >= len) continue;
                var cross = r.X * u.Y - r.Y * u.X;
                if ((cross < 0f ? -cross : cross) < LaneM) return false;
            }
            return true;
        }

        StepOutcome Evaluate(MatchContext ctx, ScenarioRun run, int r)
        {
            var st = run.CurrentStep(r);
            var slot = run.Slot[r];
            ref readonly var ball = ref ctx.Ball;
            var owns = ball.Phase == BallPhase.Controlled && ball.OwnerId == slot;
            var timedOut = !run.WaitingForKick && ctx.Tick - run.StepStart[r] >= _p.StepLimitTicks;

            switch (st.Class)
            {
                case VerbClass.Move:
                    if (Arrived(ctx, run, r)) return StepOutcome.Success;
                    return timedOut ? StepOutcome.Timeout : StepOutcome.None;
                case VerbClass.Carry:
                case VerbClass.TakeOn:
                    if (owns && Arrived(ctx, run, r)) return StepOutcome.Success;
                    return timedOut ? StepOutcome.Timeout : StepOutcome.None;
                case VerbClass.Pass:
                case VerbClass.Shoot:
                    return EvaluateKick(ctx, run, r, st, timedOut);
                case VerbClass.Defend:
                    if (st.Verb == ScenarioVerb.Engage && ball.Phase == BallPhase.Controlled && ball.OwnerId >= 0
                        && Squad.TeamOfSlot(ball.OwnerId) != Squad.TeamOfSlot(slot)
                        && (ctx.SlotState(ball.OwnerId).Position - ctx.SlotState(slot).Position).LengthSq() <= ctx.Params.TackleRange * ctx.Params.TackleRange)
                        return StepOutcome.Success;
                    return timedOut ? StepOutcome.Success : StepOutcome.None;
                default:
                    if (!ScenarioVerbs.IsReceive(st.Verb)) return StepOutcome.Success;
                    if (owns) return StepOutcome.Success;
                    return timedOut ? StepOutcome.Timeout : StepOutcome.None;
            }
        }

        StepOutcome EvaluateKick(MatchContext ctx, ScenarioRun run, int r, ScenarioStep st, bool timedOut)
        {
            ref readonly var ball = ref ctx.Ball;
            var slot = run.Slot[r];
            if (run.PendingRole != r) return timedOut ? StepOutcome.Timeout : StepOutcome.None;

            var controlled = ball.Phase == BallPhase.Controlled && ball.OwnerId >= 0;
            if (controlled && ball.OwnerId == slot)
            {
                // 물리가 킥을 적용하지 못했다 — 다시 시도한다
                if (ctx.Tick - run.KickTick >= 2) run.PendingRole = -1;
                return timedOut ? StepOutcome.Timeout : StepOutcome.None;
            }
            if (st.Class == VerbClass.Shoot)
            {
                run.PendingRole = -1;
                return StepOutcome.Success;
            }
            if (controlled && Squad.TeamOfSlot(ball.OwnerId) == run.Team)
            {
                run.PendingRole = -1;
                return StepOutcome.Success;
            }
            return timedOut ? StepOutcome.Timeout : StepOutcome.None;
        }

        bool Arrived(MatchContext ctx, ScenarioRun run, int r)
        {
            var target = ctx.FromTeamPerspective(run.Team, run.Target[r]);
            return Vec2.DistanceSq(ctx.SlotState(run.Slot[r]).Position, target) <= _p.ArriveM * _p.ArriveM;
        }

        /// <summary>단계 종료와 분기 (FR-41~42). 시나리오가 끝났거나 바뀌었으면 true.</summary>
        bool FinishStep(MatchContext ctx, ScenarioRun run, int r, StepOutcome outcome)
        {
            var step = run.Step[r];
            var slot = run.Slot[r];
            Emit(ctx, SimEventKind.ScenarioStepEnd, run, r * 100 + step, (int)outcome, ctx.PlayerAtSlot(slot));
            _diag?.OnBranch(run.Team, ctx.Tick, run.Def.Id, r, step, outcome);

            var branch = FindBranch(run.Def, r, step);
            BranchTarget target = null;
            if (branch != null)
            {
                for (int a = 0; a < branch.Alts.Length && target == null; a++)
                    if (Condition(ctx, run, branch.Alts[a].Condition)) target = branch.Alts[a].Target;
                if (target == null) target = outcome == StepOutcome.Success ? branch.Success : branch.Fail;
            }
            if (target != null) return Apply(ctx, run, target, r, step, outcome);

            var ballStep = IsBallStep(run.CurrentStep(r));
            if (outcome == StepOutcome.Success || !ballStep) { NextStep(run, r); return false; }
            End(ctx, run, FailureReason(run, outcome));
            return true;
        }

        static ScenarioEndReason FailureReason(ScenarioRun run, StepOutcome outcome)
        {
            if (outcome != StepOutcome.Timeout) return ScenarioEndReason.BranchFallback;
            return run.PendingRole >= 0 ? ScenarioEndReason.PassFailed : ScenarioEndReason.StepTimeout;
        }

        static bool IsBallStep(ScenarioStep st) =>
            st.Class == VerbClass.Carry || st.Class == VerbClass.TakeOn || st.Class == VerbClass.Pass || st.Class == VerbClass.Shoot || ScenarioVerbs.IsReceive(st.Verb);

        static ScenarioBranch FindBranch(ScenarioDefinition def, int role, int step)
        {
            var bs = def.Branches;
            for (int i = 0; i < bs.Length; i++)
                if (bs[i].Role == role && bs[i].Step == step) return bs[i];
            return null;
        }

        static void NextStep(ScenarioRun run, int r)
        {
            if (run.Step[r] + 1 < run.Def.Cast[r].Steps.Length && !IsJumpOnlyStep(run, r, run.Step[r] + 1))
            {
                run.Step[r]++;
                run.State[r] = ScenarioRun.Waiting;
            }
            else run.State[r] = ScenarioRun.Done;
        }

        /// <summary>분기 `do`로 덧붙인 단계는 순서대로 실행하지 않는다.</summary>
        static bool IsJumpOnlyStep(ScenarioRun run, int r, int step)
        {
            foreach (var b in run.Def.Branches)
            {
                if (IsJump(b.Success, r, step) || IsJump(b.Fail, r, step) || IsJump(b.SituationTarget, r, step)) return true;
                foreach (var a in b.Alts) if (IsJump(a.Target, r, step)) return true;
            }
            return false;
        }

        static bool IsJump(BranchTarget t, int r, int step) => t != null && t.Kind == BranchTargetKind.JumpStep && t.JumpRole == r && t.JumpStep == step;

        bool Apply(MatchContext ctx, ScenarioRun run, BranchTarget t, int role, int step, StepOutcome outcome)
        {
            Emit(ctx, SimEventKind.ScenarioBranch, run, role < 0 ? -1 : role * 100 + step, (int)t.Kind);
            switch (t.Kind)
            {
                case BranchTargetKind.Next:
                    NextStep(run, role);
                    return false;
                case BranchTargetKind.JumpStep:
                    run.Step[t.JumpRole] = t.JumpStep;
                    run.State[t.JumpRole] = ScenarioRun.Waiting;
                    run.PendingRole = run.PendingRole == t.JumpRole ? -1 : run.PendingRole;
                    if (role >= 0 && role != t.JumpRole) NextStep(run, role); // 끝난 배역이 다시 분기하지 않게
                    return false;
                case BranchTargetKind.Chain:
                    return ApplyChain(ctx, run, t, outcome);
                case BranchTargetKind.Loss:
                    End(ctx, run, ScenarioEndReason.PossessionLost);
                    return true;
                default:
                    End(ctx, run, role >= 0 && outcome == StepOutcome.Timeout ? FailureReason(run, outcome) : ScenarioEndReason.BranchFallback);
                    return true;
            }
        }

        bool ApplyChain(MatchContext ctx, ScenarioRun run, BranchTarget t, StepOutcome outcome)
        {
            var i = t.Ordinals.Length == 1 ? 0 : _rng.PickWeighted(t.Weights);
            var ordinal = t.Ordinals[i];
            if (ordinal < 0)
            {
                End(ctx, run, ScenarioEndReason.BranchFallback);
                return true;
            }
            if (run.Chain >= _p.ChainLimit)
            {
                End(ctx, run, ScenarioEndReason.ChainLimit);
                return true;
            }
            var next = _lib[ordinal];
            FillFrame(ctx, run.Team, run);
            if (!_assigner.Assign(next, run.Team, _frame, _slotsBuf))
            {
                End(ctx, run, ScenarioEndReason.BranchFallback);
                return true;
            }
            var chain = run.Chain + 1;
            var forced = run.Forced;
            Emit(ctx, SimEventKind.ScenarioChain, run, ordinal, chain);
            End(ctx, run, ScenarioEndReason.Chained);
            Start(ctx, run.Team, next, _slotsBuf, chain, forced);
            return true;
        }

        void End(MatchContext ctx, ScenarioRun run, ScenarioEndReason reason)
        {
            Emit(ctx, SimEventKind.ScenarioEnd, run, run.Chain, (int)reason);
            _stats.OnEnd(run.Def.Ordinal, reason, ctx.Tick - run.StartTick);
            _diag?.OnEnded(run.Team, ctx.Tick, run.Def.Id, run.Forced, reason);
            if (ScenarioEndReasons.IsFallback(reason) || reason == ScenarioEndReason.Completed)
            {
                _lastEndOrdinal[run.Team] = run.Def.Ordinal;
                _lastEndTick[run.Team] = ctx.Tick;
            }
            for (int r = 0; r < run.Def.Cast.Length; r++) ReleaseRole(run, r);
            run.Active = false;
            run.Def = null;
            run.PendingRole = -1;
        }

        void Emit(MatchContext ctx, SimEventKind kind, ScenarioRun run, int a, int b, int player = -1)
        {
            var e = SimEvent.Make(kind, run.Team, player);
            e.ScenarioId = run.Def.Ordinal;
            e.A = a;
            e.B = b;
            ctx.Emit(e);
        }

        // ── 선택 (단계 5) ──

        public void Select(MatchContext ctx)
        {
            Timer?.Begin();
            SelectCore(ctx);
            Timer?.End();
        }

        void SelectCore(MatchContext ctx)
        {
            var flow = ctx.Flow;
            ref readonly var ball = ref ctx.Ball;
            var owner = ball.Phase == BallPhase.Controlled ? ball.OwnerId : -1;
            var ownerChanged = owner != _prevOwner;
            // 058: 골 데드볼은 킥오프 준비 단계에 들어갈 때 세트피스 사건으로 본다 (세리머니·복귀 중에는 준비 시각이 없다)
            var goalPending = flow.State == FlowState.DeadBall && flow.DeadBall.Pending;
            var deadBallEvent = flow.State == FlowState.DeadBall && !goalPending && flow.DeadBallId != _prevDeadBallId;

            for (int t = 0; t < 2; t++)
            {
                if (_runs[t].Active) continue;
                if (TryForce(ctx, t, owner)) continue;
                if (flow.State != FlowState.InPlay && !(flow.State == FlowState.DeadBall && deadBallEvent)) continue;
                var events = DetectEvents(ctx, t, owner, ownerChanged, deadBallEvent);
                var possessionChanged = ownerChanged && (owner < 0 || _prevOwner < 0 || Squad.TeamOfSlot(owner) != Squad.TeamOfSlot(_prevOwner));
                if (events == TriggerEventMask.None && !possessionChanged && (ctx.Tick + t) % _p.ReevalTicks != 0) continue;
                Choose(ctx, t, owner, events, flow.State == FlowState.DeadBall);
            }

            _prevOwner = owner;
            if (flow.State == FlowState.DeadBall && !goalPending) _prevDeadBallId = flow.DeadBallId;
        }

        bool TryForce(MatchContext ctx, int team, int owner)
        {
            if (_force == null || _forceDone || _force.Team != team || ctx.Tick < _force.FromTick) return false;
            var ordinal = _lib.OrdinalOf(_force.Id);
            if (!_force.Allowed || ordinal < 0)
            {
                _forceDone = true;
                EmitForceRejected(ctx, team, ordinal, _force.Allowed ? 2 : 1);
                return true;
            }
            var def = _lib[ordinal];
            var flow = ctx.Flow;
            var ready = def.IsSetPiece
                ? flow.State == FlowState.DeadBall && !flow.DeadBall.Pending // 058 리뷰 M2: 준비 시각이 정해진 뒤
                : flow.State == FlowState.InPlay && (def.Side == ScenarioSide.Defense ? owner >= 0 && Squad.TeamOfSlot(owner) != team : owner >= 0 && Squad.TeamOfSlot(owner) == team);
            if (!ready) return false;
            _forceDone = true;
            FillFrame(ctx, team, null);
            if (!_assigner.Assign(def, team, _frame, _slotsBuf))
            {
                EmitForceRejected(ctx, team, ordinal, 3);
                return true;
            }
            Start(ctx, team, def, _slotsBuf, 0, true);
            return true;
        }

        void EmitForceRejected(MatchContext ctx, int team, int ordinal, int reason)
        {
            var e = SimEvent.Make(SimEventKind.ScenarioForceRejected, team);
            e.ScenarioId = ordinal;
            e.B = reason;
            ctx.Emit(e);
        }

        TriggerEventMask DetectEvents(MatchContext ctx, int team, int owner, bool ownerChanged, bool deadBallEvent)
        {
            var events = _in.Events != null ? _in.Events.Extra(ctx.Tick, team) : TriggerEventMask.None;
            var flow = ctx.Flow;
            if (deadBallEvent)
            {
                var attacking = flow.RestartTeam == team;
                switch (flow.Restart)
                {
                    case RestartKind.Corner: events |= TriggerEventMask.CornerAward; break;
                    case RestartKind.FreeKick: events |= TriggerEventMask.FreeKickAward; break;
                    case RestartKind.ThrowIn: events |= TriggerEventMask.ThrowInAward; break;
                    case RestartKind.GoalKick: events |= TriggerEventMask.GoalKickAward; break;
                    case RestartKind.Penalty: events |= TriggerEventMask.PenaltyAward; break;
                    case RestartKind.Kickoff: events |= TriggerEventMask.KickoffAward; break;
                }
                return events;
            }
            if (!ownerChanged || owner < 0) return events;

            var ownerTeam = Squad.TeamOfSlot(owner);
            var prevTeam = _prevOwner >= 0 ? Squad.TeamOfSlot(_prevOwner) : -1;
            if (prevTeam != ownerTeam)
            {
                if (ownerTeam == team) events |= TriggerEventMask.Regain;
                else if (prevTeam == team) events |= TriggerEventMask.Loss;
            }
            if (ownerTeam != team) events |= TriggerEventMask.OppReceive;

            var p = StepExecutor.Perspective(ctx, ownerTeam, ctx.SlotState(owner).Position);
            if (p.Y < 0.2f || p.Y > 0.8f) events |= TriggerEventMask.WideReceive;
            if (Squad.IsKeeperSlot(owner)) events |= TriggerEventMask.GkReceive;
            ref readonly var st = ref ctx.SlotState(owner);
            if (st.Facing.X * ctx.AttackDir(ownerTeam) < 0f) events |= TriggerEventMask.BackToGoalReceive;
            if (prevTeam == ownerTeam && _prevOwner >= 0 && (ctx.SlotState(_prevOwner).Position.X - st.Position.X) * ctx.AttackDir(ownerTeam) > 0f)
                events |= TriggerEventMask.BackPass;
            return events;
        }

        void Choose(MatchContext ctx, int team, int owner, TriggerEventMask events, bool deadBall)
        {
            var side = owner >= 0 && Squad.TeamOfSlot(owner) != team ? ScenarioSide.Defense : ScenarioSide.Attack;
            if (!deadBall && owner < 0) return;
            if (deadBall) side = ctx.Flow.RestartTeam == team ? ScenarioSide.Attack : ScenarioSide.Defense;

            var c = FillTrigger(ctx, team, owner, events, deadBall);
            FillFrame(ctx, team, null);
            int n = 0;

            for (int bit = 0; bit < ScenarioLibrary.EventBitCount; bit++)
                if (((uint)events & (1u << bit)) != 0) n = Collect(ctx, team, owner, _lib.ByEventBit(side, bit), in c, n);
            if (!deadBall) n = Collect(ctx, team, owner, _lib.ByCell(side, ZoneMap.CellOf(c.Ball, false)), in c, n);
            if (n == 0) return;

            var slotForJudge = owner >= 0 && Squad.TeamOfSlot(owner) == team ? owner : team * Squad.SlotsPerTeam;
            var pick = ScenarioSelector.Pick(_scores, n, ScenarioSelector.Temperature(_in.Judgement.Decisions(slotForJudge), _p), _rng);
            if (pick < 0) return;
            var def = _lib[_candidates[pick]];
            if (!_assigner.Assign(def, team, _frame, _slotsBuf)) return;
            Start(ctx, team, def, _slotsBuf, 0, false);
        }

        int Collect(MatchContext ctx, int team, int owner, int[] bucket, in TriggerContext c, int n)
        {
            for (int i = 0; i < bucket.Length; i++)
            {
                var d = _lib[bucket[i]];
                if (!TriggerEvaluator.Passes(d, in c, _lib.Zones) || !_gate.Allows(d, ctx.Tick)) continue;
                if (Contains(d.Ordinal, n)) continue;
                if (!_assigner.Assign(d, team, _frame, _slotsBuf)) continue;
                var traitSlot = owner >= 0 && Squad.TeamOfSlot(owner) == team ? owner : team * Squad.SlotsPerTeam;
                var score = ScenarioSelector.Score(d, _p, _in.Traits.Bonus(traitSlot, d),
                    ScenarioSelector.Tactic(d, in ctx.Board.Instructions(team), _p),
                    ScenarioSelector.Modifier(d, c.PhaseBit, _lib, _p),
                    ScenarioSelector.Decay(_lastUsed[team][d.BasePatternIndex], ctx.Tick, _p),
                    _in.Value.Gain(team, c.Ball));
                if (!(score > 0f)) continue;
                _candidates[n] = d.Ordinal;
                _scores[n] = score;
                n++;
            }
            return n;
        }

        bool Contains(int ordinal, int n)
        {
            for (int i = 0; i < n; i++) if (_candidates[i] == ordinal) return true;
            return false;
        }

        TriggerContext FillTrigger(MatchContext ctx, int team, int owner, TriggerEventMask events, bool deadBall)
        {
            var board = ctx.Board;
            var opp = 1 - team;
            ref readonly var ball = ref ctx.Ball;
            _in.Space.Fill(ctx, team, _space);
            var ballP = StepExecutor.Perspective(ctx, team, ball.Position.XY);

            var c = new TriggerContext
            {
                Ball = ballP,
                PhaseBit = PhaseBits.Of(board.Phase(team)),
                PressActive = board.ShapeOutput(team).PressActive,
                BallState = deadBall ? BallStateMask.Dead : StateOf(ball.Phase),
                Events = events,
                Space = _space,
                ScoreDiff = ctx.Flow.Goals(team) - ctx.Flow.Goals(opp),
                Minute = ctx.Tick * (1f / ScenarioParams.TicksPerSecond) / 60f,
                OppMarkingBit = (byte)(1 << (int)board.Instructions(opp).Marking),
            };
            c.OppBlock = BlockOf(board.Phase(opp), board.ShapeOutput(opp).OffsideLineX);
            if (owner >= 0)
            {
                var role = RoleOf(ctx, owner);
                c.CarrierRole = role;
                c.ReceiverRole = role;
                ref readonly var st = ref ctx.SlotState(owner);
                var fy = st.Facing.Y;
                c.ReceiverFacingSideline = (fy < 0f ? -fy : fy) > 0.7f;
            }
            return c;
        }

        OppBlockMask BlockOf(TeamPhase oppPhase, float oppOffsideLineX)
        {
            var m = OppBlockMask.None;
            switch (oppPhase)
            {
                case TeamPhase.DefHigh: m = OppBlockMask.HP; break;
                case TeamPhase.DefMid: m = OppBlockMask.MB; break;
                case TeamPhase.DefLow: m = OppBlockMask.LB; break;
                case TeamPhase.DefTransition: m = OppBlockMask.UNB; break;
            }
            if (oppOffsideLineX >= _p.HighLineX) m |= OppBlockMask.HL;
            return m;
        }

        static BallStateMask StateOf(BallPhase p)
        {
            switch (p)
            {
                case BallPhase.Controlled: return BallStateMask.Control;
                case BallPhase.Air: return BallStateMask.Aerial;
                case BallPhase.Bounce: return BallStateMask.Bouncing;
                default: return BallStateMask.Bouncing;
            }
        }

        void FillFrame(MatchContext ctx, int team, ScenarioRun releasing)
        {
            var f = _frame;
            f.Ball = StepExecutor.Perspective(ctx, team, ctx.Ball.Position.XY);
            f.Owner = ctx.Ball.Phase == BallPhase.Controlled ? ctx.Ball.OwnerId : -1;
            f.Taker = ctx.Flow.RestartTaker;
            f.CastCount[0] = f.CastCount[1] = 0;
            for (int s = 0; s < Squad.Slots; s++)
            {
                f.Active[s] = ctx.IsSlotControllable(s); // 058: 걷는 선수는 배역 후보가 아니다
                if (!f.Active[s]) { f.Busy[s] = CastBusy.Free; continue; }
                f.Position[s] = StepExecutor.Perspective(ctx, team, ctx.SlotState(s).Position);
                f.Role[s] = RoleOf(ctx, s);
                var holder = _castRun[s];
                if (holder < 0 || (releasing != null && holder == releasing.Team)) { f.Busy[s] = CastBusy.Free; continue; }
                f.Busy[s] = _castOpp[s] ? CastBusy.OpponentRole : CastBusy.OwnScenario;
                f.CastCount[Squad.TeamOfSlot(s)]++;
            }
            for (int t = 0; t < 2; t++)
                f.CastCap[t] = _runs[t].Active && _runs[t].Def.Side == ScenarioSide.Attack ? ScenarioValidator.MaxAttackCast : ScenarioValidator.MaxDefenseCast;
        }

        void Start(MatchContext ctx, int team, ScenarioDefinition def, int[] slots, int chain, bool forced)
        {
            var run = _runs[team];
            ReleaseOpponentRolesOn(ctx, team);
            run.Active = true;
            run.Def = def;
            run.StartTick = ctx.Tick;
            run.ZeroTick = def.IsSetPiece && ctx.Flow.State == FlowState.DeadBall ? ctx.Flow.ReadyTick : ctx.Tick;
            run.Chain = chain;
            run.Forced = forced;
            run.Resumed = !(def.IsSetPiece && ctx.Flow.State == FlowState.DeadBall);
            run.PendingRole = -1;
            run.KickTick = -1;
            run.LastKickTick = long.MinValue / 2;

            // 쓰지 않는 배역 칸도 비운다: 이전 런 값이 남으면 스냅샷 복원본(빈 칸)과 상태 해시가 달라진다 (058 T05a, 013 잠복 버그)
            for (int r = 0; r < ScenarioRun.MaxRoles; r++)
            {
                run.Slot[r] = -1;
                run.Player[r] = 0;
                run.Step[r] = 0;
                run.TrackSlot[r] = 0;
                run.StepStart[r] = 0;
                run.State[r] = 0;
                run.Target[r] = default;
            }
            for (int r = 0; r < def.Cast.Length; r++)
            {
                var s = slots[r];
                run.Slot[r] = s;
                run.Step[r] = 0;
                run.State[r] = ScenarioRun.Waiting;
                run.TrackSlot[r] = -1;
                run.StepStart[r] = ctx.Tick;
                run.Target[r] = default;
                if (s < 0) continue;
                run.Player[r] = ctx.PlayerAtSlot(s);
                _castRun[s] = team;
                _castOpp[s] = def.Cast[r].Opponent;
                _castRole[s] = r;
            }

            _gate.OnStart(def, ctx.Tick);
            _stats.OnStart(def.Ordinal);
            _lastUsed[team][def.BasePatternIndex] = ctx.Tick;
            Emit(ctx, SimEventKind.ScenarioStart, run, chain, (forced ? 1 : 0) | (def.Mirror ? 2 : 0));
            for (int r = 0; r < def.Cast.Length; r++)
                if (run.Slot[r] >= 0) Emit(ctx, SimEventKind.ScenarioCast, run, r, run.Slot[r], ctx.PlayerAtSlot(run.Slot[r]));
        }

        /// <summary>
        /// 자기 팀 시나리오가 시작되면 상대가 이 팀 선수에게 준 배역을 푼다 (FR-32·34).
        /// 필수 상대 배역을 잃으면 상대 시나리오는 배역 이탈로 끝난다.
        /// </summary>
        void ReleaseOpponentRolesOn(MatchContext ctx, int team)
        {
            var other = _runs[1 - team];
            if (!other.Active) return;
            for (int r = 0; r < other.Def.Cast.Length; r++)
                if (other.Def.Cast[r].Opponent && other.Def.Cast[r].Required && other.Slot[r] >= 0)
                {
                    End(ctx, other, ScenarioEndReason.CastLost);
                    return;
                }
            for (int r = 0; r < other.Def.Cast.Length; r++)
                if (other.Def.Cast[r].Opponent && other.Slot[r] >= 0)
                {
                    EmitReleased(ctx, other, r);
                    ReleaseRole(other, r);
                }
        }

        // ── 스냅샷 (006 FR-60) ──

        public void WriteState(StateWriter w)
        {
            w.Write(_rng.SaveState());
            w.Write(_prevOwner);
            w.Write(_prevDeadBallId);
            w.Write(_prevKickSubstep);
            w.Write(_forceDone);
            for (int s = 0; s < Squad.Slots; s++) { w.Write(_castRun[s]); w.Write(_castOpp[s]); w.Write(_castRole[s]); w.Write((ushort)_roleCache[s]); }
            for (int t = 0; t < 2; t++)
            {
                WriteRun(w, _runs[t]);
                w.Write(_lastEndOrdinal[t]);
                w.Write(_lastEndTick[t]);
                var used = _lastUsed[t];
                int n = 0;
                for (int i = 0; i < used.Length; i++) if (used[i] >= 0) n++;
                w.Write(n);
                for (int i = 0; i < used.Length; i++) if (used[i] >= 0) { w.Write(i); w.Write(used[i]); }
            }
            _gate.Write(w);
            _stats.Write(w);
        }

        public void ReadState(StateReader r)
        {
            _rng.RestoreState(r.ReadRngState());
            _prevOwner = r.ReadInt();
            _prevDeadBallId = r.ReadInt();
            _prevKickSubstep = r.ReadLong();
            _forceDone = r.ReadBool();
            for (int s = 0; s < Squad.Slots; s++) { _castRun[s] = r.ReadInt(); _castOpp[s] = r.ReadBool(); _castRole[s] = r.ReadInt(); _roleCache[s] = (PositionMask)r.ReadUShort(); }
            for (int t = 0; t < 2; t++)
            {
                ReadRun(r, _runs[t]);
                _lastEndOrdinal[t] = r.ReadInt();
                _lastEndTick[t] = r.ReadLong();
                Array.Fill(_lastUsed[t], -1L);
                var n = r.ReadInt();
                for (int k = 0; k < n; k++) { var i = r.ReadInt(); _lastUsed[t][i] = r.ReadLong(); }
            }
            _gate.Read(r);
            _stats.Read(r);
        }

        static void WriteRun(StateWriter w, ScenarioRun x)
        {
            w.Write(x.Active);
            if (!x.Active) return;
            w.Write(x.Def.Ordinal);
            w.Write(x.StartTick); w.Write(x.ZeroTick); w.Write(x.Chain); w.Write(x.Forced); w.Write(x.Resumed);
            w.Write(x.PendingRole); w.Write(x.KickTick); w.Write(x.LastKickTick);
            for (int r = 0; r < ScenarioRun.MaxRoles; r++)
            {
                w.Write(x.Slot[r]); w.Write(x.Player[r]); w.Write(x.Step[r]); w.Write(x.TrackSlot[r]);
                w.Write(x.StepStart[r]); w.Write(x.State[r]); w.Write(x.Target[r]);
            }
        }

        void ReadRun(StateReader r, ScenarioRun x)
        {
            x.Active = r.ReadBool();
            x.Def = null;
            if (!x.Active) return;
            x.Def = _lib[r.ReadInt()];
            x.StartTick = r.ReadLong(); x.ZeroTick = r.ReadLong(); x.Chain = r.ReadInt(); x.Forced = r.ReadBool(); x.Resumed = r.ReadBool();
            x.PendingRole = r.ReadInt(); x.KickTick = r.ReadLong(); x.LastKickTick = r.ReadLong();
            for (int i = 0; i < ScenarioRun.MaxRoles; i++)
            {
                x.Slot[i] = r.ReadInt(); x.Player[i] = r.ReadInt(); x.Step[i] = r.ReadInt(); x.TrackSlot[i] = r.ReadInt();
                x.StepStart[i] = r.ReadLong(); x.State[i] = r.ReadByte(); x.Target[i] = r.ReadVec2();
            }
        }

        public void AddTo(ref StableHasher h)
        {
            var s = _rng.SaveState();
            h.Add(s.S0); h.Add(s.S1); h.Add(s.S2); h.Add(s.S3);
            h.Add(_prevOwner); h.Add(_prevDeadBallId); h.Add(_prevKickSubstep); h.Add(_forceDone);
            for (int i = 0; i < Squad.Slots; i++) { h.Add(_castRun[i]); h.Add(_castOpp[i]); h.Add(_castRole[i]); h.Add((uint)(ushort)_roleCache[i]); }
            for (int t = 0; t < 2; t++)
            {
                var x = _runs[t];
                h.Add(x.Active);
                if (x.Active)
                {
                    h.Add(x.Def.Ordinal); h.Add(x.StartTick); h.Add(x.ZeroTick); h.Add(x.Chain); h.Add(x.Forced); h.Add(x.Resumed);
                    h.Add(x.PendingRole); h.Add(x.KickTick); h.Add(x.LastKickTick);
                    for (int r = 0; r < ScenarioRun.MaxRoles; r++)
                    {
                        h.Add(x.Slot[r]); h.Add(x.Player[r]); h.Add(x.Step[r]); h.Add(x.TrackSlot[r]);
                        h.Add(x.StepStart[r]); h.Add(x.State[r]); h.Add(x.Target[r].X); h.Add(x.Target[r].Y);
                    }
                }
                h.Add(_lastEndOrdinal[t]); h.Add(_lastEndTick[t]);
                var used = _lastUsed[t];
                for (int i = 0; i < used.Length; i++) if (used[i] >= 0) { h.Add(i); h.Add(used[i]); }
            }
            _gate.AddTo(ref h);
            _stats.AddTo(ref h);
        }
    }
}
