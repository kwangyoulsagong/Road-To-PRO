using System.Collections.Generic;
using RoadToPro.Core.Hashing;
using RoadToPro.Core.Math;
using RoadToPro.Core.Serialization;
using RoadToPro.Core.Time;
using RoadToPro.MatchEngine.Ball;
using RoadToPro.MatchEngine.Match.Events;
using RoadToPro.MatchEngine.Match.Flow;
using RoadToPro.MatchEngine.Match.Pipeline;
using RoadToPro.MatchEngine.Match.Stages.StandIn;
using RoadToPro.MatchEngine.Scenarios;
using RoadToPro.MatchEngine.Tactics;
using RoadToPro.MatchEngine.UtilityAI;

namespace RoadToPro.MatchEngine.Match.Stages
{
    /// <summary>
    /// 단계 5 — 판단 AI (specs/010). 결정 대상(LOD 스케줄러 + 외부 입력 + 의도 변화 + 키 모먼트)을 선수 id 순으로 처리한다.
    /// ① 인플레이가 아님·외부 입력·공 가진 GK → 대체 경로(<see cref="StandInOnBall"/>, 012·074 자리)
    /// ② 인플레이 공 소유 필드 선수(배역 포함) → 072 온볼 판단(<see cref="OnBallDecider"/>) ③ GK(인플레이·공 미소유) → 011 GK 결정
    /// ④ 그 밖의 필드 선수(배역 포함) → 010 오프볼 판단(<see cref="OffBallDecider"/>).
    /// 배역 비소유 선수는 시나리오가 행동을 정하지 않고 의도 패킷만 준다(FR-50·53). 기회주의 이탈은 같은 틱에 013에 통보(FR-94).
    /// 결정은 보드에 쓰고 결정하지 않은 선수는 직전 결정을 유지한다(FR-43).
    /// </summary>
    public sealed class UtilityDecisionStage : IMatchStage, IDecisionSchedulerProvider
    {
        const int N = Squad.Slots;

        readonly DecisionParams _p;
        readonly StandInOnBall _x;
        readonly ScenarioRuntime _runtime;
        readonly IIntentSource _intents;
        readonly DecisionWorld _w = new DecisionWorld();
        readonly StandInStyleSource _style;
        readonly StandInSpace _space;
        readonly OffBallDecider _decider;
        readonly OnBallDecider _onBall;
        readonly StandInChemistry _chem = new StandInChemistry();
        readonly StandInOnBallEval _eval;
        readonly LodDecisionScheduler _sched;
        readonly KeyMomentOverride _keys = new KeyMomentOverride();
        readonly Dictionary<int, MatchPlayer> _players = new Dictionary<int, MatchPlayer>();

        readonly Decision[] _intent = new Decision[N];
        readonly PlayerDecisionState[] _state = new PlayerDecisionState[N];
        readonly Vec2[] _lastAnchor = new Vec2[N];
        readonly bool[] _hasAnchor = new bool[N];
        readonly int[] _playerAt = new int[N];
        readonly int[] _prevIntentKey = new int[N];
        readonly bool[] _due = new bool[N];
        readonly bool[] _dueNormal = new bool[N];
        readonly bool[] _intentChanged = new bool[N];
        readonly long[] _lossTick = { long.MinValue / 2, long.MinValue / 2 };
        readonly bool[] _assigned = new bool[N];
        int _lastPossTeam = -1;
        int _passTarget = -1;
        long _passTick = long.MinValue / 2;
        long _nextMarkTick;
        int _lastMarkPossTeam = -2;
        // 072 온볼: 이동 결정 유지(010 FR-42 최소 유지)·돌파 성공 후 제친 수비수 태클 차단(070 대체)
        readonly long[] _onBallUntil = new long[N];
        readonly long[] _beatenUntil = new long[N];
        readonly long[] _onBallTick = new long[N];

        public UtilityDecisionStage(MatchSetup setup, MatchTuning tuning, ScenarioRuntime runtime = null)
        {
            _p = DecisionParams.Create(tuning?.Decision);
            _runtime = runtime;
            _x = new StandInOnBall(runtime);
            _intents = runtime != null ? new ScenarioIntentAdapter(runtime, _p) : null;
            _style = new StandInStyleSource(N, _p.TendencyBase);
            _space = new StandInSpace(_p);
            _decider = new OffBallDecider(_p, _space, _style);
            _eval = new StandInOnBallEval(_p);
            _onBall = new OnBallDecider(_p, _style, _eval, _chem);
            _sched = new LodDecisionScheduler(_p) { Cast = _intents };
            AddPlayers(setup.Home);
            AddPlayers(setup.Away);
            for (int s = 0; s < N; s++)
            {
                _state[s] = PlayerDecisionState.Initial;
                _playerAt[s] = int.MinValue;
                _prevIntentKey[s] = -1;
                _w.Intent[s] = IntentPacket.None;
                _onBallUntil[s] = long.MinValue / 2;
                _beatenUntil[s] = long.MinValue / 2;
                _onBallTick[s] = long.MinValue / 2;
            }
        }

        void AddPlayers(TeamSetup t)
        {
            if (t?.Players == null) return;
            foreach (var p in t.Players) _players[p.Id] = p; // 조회 전용 (순회 결과에 의존하지 않음)
        }

        public StageId Id => StageId.ScenarioOrUtility;
        public IDecisionScheduler Scheduler => _sched;
        public LodDecisionScheduler Lod => _sched;
        public DecisionParams Params => _p;
        public DecisionWorld World => _w;
        public StandInStyleSource Style => _style;
        public KeyMomentOverride KeyMoments => _keys;
        public OffBallDecider Decider => _decider;
        public OnBallDecider OnBall => _onBall;
        /// <summary>케미 스냅샷 (033 전 대체 — 경기 시작 전에 채운다).</summary>
        public StandInChemistry Chemistry { get { _chemSet = true; return _chem; } }
        bool _chemSet;
        public ISpaceEvaluator Space => _space;
        public ref readonly PlayerDecisionState State(int slot) => ref _state[slot];
        /// <summary>선수의 현재 결정 (보드에 쓰는 값).</summary>
        public ref readonly Decision DecisionOf(int slot) => ref _intent[slot];

        /// <summary>결정 진단·텔레메트리 (FR-60~62·99·104). 끄면 null — 시뮬 결과·RNG를 바꾸지 않는다.</summary>
        public IDecisionDiagnostics Diagnostics { get; set; }

        public void Execute(MatchContext ctx)
        {
            var board = ctx.Board;
            _x.BeginTick();
            var n = _x.MergeInputs(ctx);
            _x.ComputeChasers(ctx);
            FillWorld(ctx);
            _space.ResetQueries();

            var flow = ctx.Flow;
            var inPlay = flow.State == FlowState.InPlay;
            ref readonly var ball = ref ctx.Ball;
            var owner = ball.Phase == BallPhase.Controlled ? ball.OwnerId : -1;
            System.Array.Clear(_due, 0, N);
            for (int i = 0; i < n; i++) _due[_x.ListAt(i)] = true;
            for (int s = 0; s < N; s++)
            {
                var key = _w.Intent[s].Has ? _w.Intent[s].StepKey : -1;
                _intentChanged[s] = key != _prevIntentKey[s];
                _prevIntentKey[s] = key;
                if (_intentChanged[s]) _due[s] = true; // FR-41·95: 의도 도착·해제 → 같은 틱 재결정
                _dueNormal[s] = _due[s];
                if (!_keys.Has(s)) continue;
                // 키 모먼트는 오프볼 결정만 덮어쓴다 — GK·소유자·데드볼·외부 입력이면 지운다 (결정론 리뷰 M1b)
                if (inPlay && s != owner && !Squad.IsKeeperSlot(s) && _x.InputOf(s) == default && ctx.IsSlotControllable(s)) _due[s] = true;
                else _keys.Clear(s);
            }
            var hasAction = false;
            for (int s = 0; s < N; s++)
            {
                if (!_due[s]) continue;
                if (!ctx.IsSlotControllable(s)) continue; // 058: 걷는 선수는 흐름이 직접 조향
                if (ctx.Keepers.IsBusySlot(s)) // 011 FR-17
                {
                    _intent[s] = StandInOnBall.Stand();
                    Other(s, DecisionSource.Keeper, ctx.Tick);
                    board.MarkDecided(s);
                    continue;
                }
                Decision d;
                if (_x.KeeperDecides(ctx, s))
                {
                    d = _x.DecideKeeper(ctx, s, ref hasAction);
                    Other(s, DecisionSource.Keeper, ctx.Tick);
                }
                else if (inPlay && s == owner && !Squad.IsKeeperSlot(s) && _x.InputOf(s) == default && !flow.DeadBall.Pending)
                {
                    var hadAction = hasAction;
                    d = DecideOnBall(ctx, s, ref hasAction);
                    if (!hadAction && hasAction) RememberPass(ctx, s);
                }
                else if (!inPlay || _x.InputOf(s) != default || s == owner || Squad.IsKeeperSlot(s))
                {
                    var hadAction = hasAction;
                    if (_runtime == null || _x.InputOf(s) != default || flow.DeadBall.Pending || !_runtime.TryDecide(ctx, s, ref hasAction, out d))
                        d = _x.Decide(ctx, s, ref hasAction);
                    if (_p.DeadBallCap > 0f && !inPlay && d.Kind == DecisionKind.HoldShape && d.MaxSpeed <= 0f && _w.Active[s]) d.MaxSpeed = DeadBallShapeSpeed(s); // FR-70: 데드볼 형태 정렬도 강도 상한
                    Other(s, inPlay ? DecisionSource.OnBall : DecisionSource.DeadBall, ctx.Tick);
                    if (!hadAction && hasAction) RememberPass(ctx, s);
                }
                else
                {
                    // FR-52 키 모먼트: 원래 결정할 틱이면 일반 결정(기회주의 통보·태클 굴림 포함)을 끝까지 돌린 뒤 결정만 덮어쓴다 —
                    // RNG 소비와 다른 선수 결정이 키 모먼트가 없을 때와 같다 (AC-13, 결정론 리뷰 M1a). 원래 결정할 틱이 아니면 아무것도 소비하지 않는다.
                    var hasKey = _keys.TryTake(s, out var kk, out var kp);
                    d = default;
                    if (!hasKey || _dueNormal[s])
                    {
                        var trigger = _sched.WasTriggered(s) || _intentChanged[s];
                        var r = _decider.Decide(_w, s, ref _state[s], ctx.DecisionRng, trigger);
                        if (r.ReleaseIntent && _intents != null && _intents.NotifyOpportunistic(ctx, s))
                        {
                            _w.Intent[s] = IntentPacket.None;
                            _prevIntentKey[s] = -1;
                        }
                        d = ToDecision(s, in r);
                        if (r.IgnoredInstruction) EmitIgnored(ctx, s, r.IgnoredKind, r.IgnoredWeight);
                        if (r.Kind == Consideration.Press && r.Target >= 0 && _beatenUntil[s] <= ctx.Tick
                            && _x.TryTackle(ctx, s, r.Target, OffBallScorer.TackleAttemptScale(_p, _w.Mind[s].Temperament), ref hasAction))
                            d = StandInOnBall.Stand();
                        if (!hasKey) Diagnostics?.OnDecision(ctx.Tick, s, in _state[s], _decider, _w);
                    }
                    if (hasKey)
                    {
                        var r = new OffBallResult { Kind = kk, Point = _w.ClampToPitch(kp, 0.5f), Target = -1, Intensity = Intensity.Sprint, Source = DecisionSource.KeyMoment };
                        r.MaxSpeed = _w.Profile[s].MaxSpeed * IntensityPicker.SpeedRatio(_p, r.Intensity);
                        d = ToDecision(s, in r);
                        _state[s].HasAction = true; _state[s].Kind = kk; _state[s].Point = r.Point; _state[s].Target = -1; _state[s].StartTick = ctx.Tick;
                        _state[s].Intensity = r.Intensity; _state[s].Source = DecisionSource.KeyMoment; _state[s].LastDecisionTick = ctx.Tick;
                        Diagnostics?.OnDecision(ctx.Tick, s, in _state[s], null, _w);
                    }
                }
                _intent[s] = d;
                board.MarkDecided(s);
            }
            for (int s = 0; s < N; s++) board.SetDecision(s, in _intent[s]);
            Diagnostics?.OnTick(ctx.Tick, _w, _space.QueryCount);
        }

        /// <summary>
        /// 072 온볼 결정 (인플레이 공 소유 필드 선수). 보유 시간 하한 동안·이동 결정 최소 유지(010 FR-42) 동안은 직전 이동을 이어 간다(RNG 미소비).
        /// 결정이 나면: 패스·스루·크로스 → 008 의도 공 동작, 슈팅·걷어내기 → 대체 공 동작, 돌파 → 070 대체 1:1 판정(결정 RNG 1회), 캐리·홀드 → 이동, 넘어지기 → 012 입력 이벤트.
        /// 지시 무시·기회주의 이탈·의도 일치 킥 통보는 같은 틱.
        /// </summary>
        Decision DecideOnBall(MatchContext ctx, int s, ref bool hasAction)
        {
            var tick = ctx.Tick;
            var team = Squad.TeamOfSlot(s);
            var me = ctx.SlotState(s);
            var p = ctx.Params;
            var carrySpeed = _w.Profile[s].MaxSpeed * p.CarrySpeedRatio;
            var fresh = _onBallTick[s] < ctx.PossessionStartTick; // 이번 소유에서 아직 온볼 결정 안 함 (결정론 리뷰 L)
            var held = tick - ctx.PossessionStartTick;
            var keep = !fresh && tick < _onBallUntil[s] && !_sched.WasTriggered(s) && !_intentChanged[s];
            ref readonly var pend = ref ctx.Actions.Pending;
            if (pend.Active && pend.Action.KickerSlot == s) return StandInOnBall.Stand(); // 킥 준비 중: 다시 판단하지 않는다 (RNG 미소비, 결정론 리뷰 H)
            if (hasAction || held < p.HoldTicks || keep)
            {
                if (!fresh && (_intent[s].Kind == DecisionKind.Carry || _intent[s].Kind == DecisionKind.Stand)) return _intent[s];
                var goal = ctx.OpponentGoal(team);
                var carryTo = new Vec2(goal.X - ctx.AttackDir(team) * 12f, me.Position.Y + (goal.Y - me.Position.Y) * 0.3f);
                return new Decision { Kind = DecisionKind.Carry, Point = carryTo, TargetSlot = -1, MaxSpeed = carrySpeed };
            }

            var act = ctx.Actions;
            act.Refresh(ctx);
            var pressure = act.PressureOn(ctx, s);
            var oneTouch = held <= (long)act.P.OneTouchTicks;
            _onBallTick[s] = tick;
            var r = _onBall.Decide(_w, act.World, act.P, ctx.BallParams, s, ref _state[s], ctx.DecisionRng, pressure, oneTouch, held * _w.Dt);
            Diagnostics?.OnOnBallDecision(tick, s, in r, _onBall, _w);

            if (r.IgnoredInstruction) EmitIgnored(ctx, s, r.IgnoredKind, r.IgnoredWeight);
            if (r.ReleaseIntent && _intents != null && _intents.NotifyOpportunistic(ctx, s))
            {
                _w.Intent[s] = IntentPacket.None;
                _prevIntentKey[s] = -1;
            }

            var from = ctx.Ball.Position.XY;
            switch (r.Item)
            {
                case OnBallItem.ShortPass:
                case OnBallItem.ProgPass:
                case OnBallItem.ThroughBall:
                case OnBallItem.Switch:
                case OnBallItem.Cross:
                {
                    var a = Actions.ActionSystem.IntentAction(s, r.Target, in r.Pass, from, oneTouch);
                    a.Part = PhysicsStage.PartFor(in me, ctx.Ball.Position);
                    ctx.Board.SetBallAction(a);
                    hasAction = true;
                    _x.LastPassTarget = r.Target;
                    _x.LastPassTick = tick;
                    act.Diagnostics?.OnPassChosen(tick, s, r.Target, in r.Pass);
                    if (_w.Intent[s].Has && _w.Intent[s].NextPassTarget == r.Target) _runtime?.NotifyOnBallKick(ctx, s); // 출처가 아니라 실제 킥이 단계와 맞는지 (결정론 리뷰 M)
                    return StandInOnBall.Stand();
                }
                case OnBallItem.Shoot:
                    StandInOnBall.Shoot(ctx, s, team, false, false);
                    hasAction = true;
                    if (_w.Intent[s].Has && (_w.Intent[s].Kind == Consideration.Shot || _w.Intent[s].Kind == Consideration.LongShot)) _runtime?.NotifyOnBallKick(ctx, s);
                    return StandInOnBall.Stand();
                case OnBallItem.Clear:
                    StandInOnBall.ClearForward(ctx, s, team, false);
                    hasAction = true;
                    return StandInOnBall.Stand();
                case OnBallItem.Dive:
                {
                    var e = SimEvent.Make(SimEventKind.DiveAttempt, team, ctx.PlayerAtSlot(s), r.Target >= 0 ? ctx.PlayerAtSlot(r.Target) : -1);
                    e.A = r.ContactBin;
                    e.B = s;
                    e.X = me.Position.X;
                    e.Y = me.Position.Y;
                    e.F0 = r.FoulValue;
                    ctx.Emit(e);
                    _onBallUntil[s] = tick + HoldMinTicks;
                    return new Decision { Kind = DecisionKind.Stand, TargetSlot = -1 };
                }
                case OnBallItem.Dribble when r.Defender >= 0:
                {
                    var ok = ctx.DecisionRng.NextFloat() < r.P; // 070 대체 1:1 판정
                    var e = SimEvent.Make(SimEventKind.Dribble, team, ctx.PlayerAtSlot(s), ctx.PlayerAtSlot(r.Defender));
                    e.A = s;
                    e.B = ok ? EventCodes.DribbleSuccess : 0;
                    e.X = me.Position.X;
                    e.Y = me.Position.Y;
                    e.F0 = r.P;
                    ctx.Emit(e);
                    if (!ok)
                    {
                        act.TransferPossession(ctx, r.Defender, ctx.SlotState(r.Defender).Position); // 이벤트·마지막 터치까지 수신 경로와 같게 (결정론 리뷰 M)
                        hasAction = true;
                        return new Decision { Kind = DecisionKind.Stand, TargetSlot = -1 };
                    }
                    _beatenUntil[r.Defender] = tick + (long)(_p.OnBallBeatenBlock * SimClock.TicksPerSecond + 0.5f);
                    _onBallUntil[s] = tick + HoldMinTicks;
                    return new Decision { Kind = DecisionKind.Carry, Point = r.Point, TargetSlot = -1, MaxSpeed = carrySpeed };
                }
                case OnBallItem.Hold:
                    _onBallUntil[s] = tick + HoldMinTicks;
                    return new Decision { Kind = DecisionKind.Carry, Point = me.Position, TargetSlot = -1, MaxSpeed = _w.Profile[s].MaxSpeed * IntensityPicker.SpeedRatio(_p, Intensity.Walk) };
                default: // 캐리
                    _onBallUntil[s] = tick + HoldMinTicks;
                    return new Decision { Kind = DecisionKind.Carry, Point = r.Point, TargetSlot = -1, MaxSpeed = carrySpeed };
            }
        }

        long HoldMinTicks => (long)(_p.HoldMinTime * SimClock.TicksPerSecond + 0.5f);

        void EmitIgnored(MatchContext ctx, int s, Consideration kind, float weight)
        {
            var e = SimEvent.Make(SimEventKind.InstructionIgnored, Squad.TeamOfSlot(s), ctx.PlayerAtSlot(s));
            e.A = (int)kind;
            e.B = s;
            e.F0 = weight;
            var pos = ctx.SlotState(s).Position;
            e.X = pos.X;
            e.Y = pos.Y;
            ctx.Emit(e);
        }

        /// <summary>이 슬롯이 이번 틱 공 동작을 냈다 — 패스면 받는 슬롯을 기억한다 (대체 온볼 또는 시나리오 패스).</summary>
        void RememberPass(MatchContext ctx, int s)
        {
            var target = -1;
            if (_x.LastPassTick == ctx.Tick) target = _x.LastPassTarget;
            else if (_runtime != null && _runtime.TryGetCast(s, out var run, out var role) && run.KickTick == ctx.Tick)
            {
                var st = run.CurrentStep(role);
                if (st.TargetKind == StepTargetKind.Role && st.TargetRef >= 0) target = run.Slot[st.TargetRef];
            }
            if (target < 0) return;
            _passTarget = target;
            _passTick = ctx.Tick;
        }

        /// <summary>오프볼이 아닌 경로가 정한 결정: 오프볼 상태를 비우고 출처만 남긴다(다음 오프볼 결정은 새로 시작).</summary>
        void Other(int s, DecisionSource src, long tick)
        {
            _state[s].HasAction = false;
            _state[s].Source = src;
            _state[s].LastDecisionTick = tick;
            Diagnostics?.OnDecision(tick, s, in _state[s], null, _w);
        }

        /// <summary>데드볼 형태 정렬 속도 상한 (FR-70·71): 형태 유지 강도 규칙(거리 기준 걷기·조깅·달리기), 질주 없음. 058 골 후 복귀는 자체 속도를 쓴다.</summary>
        float DeadBallShapeSpeed(int s)
        {
            var dist = (_w.Anchor[s] - _w.Pos[s]).Length();
            var i = IntensityPicker.Pick(_p, Consideration.HoldShape, dist, _w.C[s], DecisionAttributes.N(_w.Mind[s].WorkRate), false, Intensity.Run, true);
            return _w.Profile[s].MaxSpeed * IntensityPicker.SpeedRatio(_p, i);
        }

        Decision ToDecision(int s, in OffBallResult r)
        {
            switch (r.Kind)
            {
                case Consideration.HoldShape:
                    // FR-46: 형태 유지도 목표점을 결정 틱당 2 m까지만 옮긴 점(r.Point)으로 간다 — 009 목표가 튀어도 따라 뛰지 않는다. FR-43a 정지 구간이면 멈춘 위치
                    return new Decision { Kind = DecisionKind.MoveTo, Point = r.Point, TargetSlot = -1, MaxSpeed = r.MaxSpeed };
                case Consideration.Press when r.Target >= 0:
                    return new Decision { Kind = DecisionKind.Chase, TargetSlot = r.Target, MaxSpeed = r.MaxSpeed };
                case Consideration.LooseBall:
                    return new Decision { Kind = DecisionKind.Chase, Point = r.Point, TargetSlot = -1, MaxSpeed = r.MaxSpeed };
                default:
                    return new Decision { Kind = DecisionKind.MoveTo, Point = r.Point, TargetSlot = -1, MaxSpeed = r.MaxSpeed };
            }
        }

        void FillWorld(MatchContext ctx)
        {
            var w = _w;
            var board = ctx.Board;
            var flow = ctx.Flow;
            w.Tick = ctx.Tick;
            var left = (flow.NominalTicks - flow.Elapsed) / (float)SimClock.TicksPerMinute; // 072 FR-11 남은 시간 (정규 기준)
            w.MinutesLeft = (left < 0f ? 0f : left) + (flow.Period == 0 ? 45f : (flow.Period == 2 ? 15f : 0f));
            w.Dt = 1f / SimClock.TicksPerSecond;
            w.PitchL = ctx.Pitch.LengthM;
            w.PitchW = ctx.Pitch.WidthM;
            for (int s = 0; s < N; s++)
            {
                var active = ctx.IsSlotControllable(s);
                w.Active[s] = active;
                if (!ctx.IsSlotActive(s)) continue;
                var pid = ctx.PlayerAtSlot(s);
                if (pid != _playerAt[s]) Rebind(s, pid);
                ref readonly var st = ref ctx.SlotState(s);
                w.Pos[s] = st.Position;
                w.Vel[s] = st.Velocity;
                w.W[s] = st.W;
                w.C[s] = st.C;
                w.Profile[s] = ctx.Kinematics.Profile(s);
                w.Kin[s] = ctx.Kinematics.Attributes(s);
                if (board.HasShapeTarget(s))
                {
                    w.Anchor[s] = board.ShapeTarget(s);
                    w.HasShape[s] = true;
                    _lastAnchor[s] = w.Anchor[s];
                    _hasAnchor[s] = true;
                }
                else
                {
                    w.Anchor[s] = _hasAnchor[s] ? _lastAnchor[s] : st.Position; // 배역 중: 마지막 앵커 (FR-96)
                    w.HasShape[s] = false;
                }
            }
            w.RefreshTti(ctx.KinParams);

            ref readonly var ball = ref ctx.Ball;
            w.Ball = ball.Position.XY;
            w.BallVel = ball.Velocity.XY;
            w.BallZ = ball.Position.Z;
            w.Owner = ball.Phase == BallPhase.Controlled ? ball.OwnerId : -1;
            w.OwnerSinceTick = ctx.PossessionStartTick;
            if (w.Owner >= 0 || (ctx.Tick - _passTick) * w.Dt > _p.PassMemory) _passTarget = -1; // 공이 잡혔거나 오래됨
            w.PassTarget = _passTarget >= 0 && w.Active[_passTarget] ? _passTarget : -1;

            var poss = w.PossTeam;
            if (poss >= 0 && poss != _lastPossTeam)
            {
                if (_lastPossTeam >= 0) _lossTick[_lastPossTeam] = ctx.Tick; // 소유 상실 순간 (FR-101 창 시작)
                _lastPossTeam = poss;
            }
            for (int t = 0; t < 2; t++)
            {
                w.AttackDir[t] = ctx.AttackDir(t);
                w.Phase[t] = board.Phase(t);
                w.OffsideLineX[t] = board.OffsideLineM(t);
                w.DefLineX[t] = board.DefLineM(t);
                ref readonly var so = ref board.ShapeOutput(t);
                ref readonly var ins = ref board.Instructions(t);
                w.PressIntensity[t] = ins.PressIntensity;
                w.Tempo[t] = ins.Tempo; w.Directness[t] = ins.Directness; w.Risk[t] = ins.Risk; w.TimeWasting[t] = ins.TimeWasting; // 072 FR-11
                w.GoalDiff[t] = flow.Goals(t) - flow.Goals(1 - t);
                w.Presser[t * 3 + 0] = Slot(t, so.PressActive ? so.Press1 : so.FirstResponder); // 압박 비활성이면 1순위 대응자가 붙는다 (009 FR-51)
                w.Presser[t * 3 + 1] = so.PressActive ? Slot(t, so.Press2) : -1;
                w.Presser[t * 3 + 2] = so.PressActive ? Slot(t, so.Press3) : -1;

                // 게겐프레싱 창 (FR-101): 소유 상실 순간부터 t_gp, 다시 소유하면 즉시 끝
                if (poss == t) _lossTick[t] = long.MinValue / 2;
                var counter = ins.OnLoss == OnLossMode.CounterPress || (ins.OnLoss == OnLossMode.Mixed && w.AttackDir[t] * (w.Ball.X - w.PitchL * 0.5f) > 0f);
                w.GegenActive[t] = counter && poss >= 0 && poss != t && (ctx.Tick - _lossTick[t]) * w.Dt < _p.GpWindow;
            }

            AssignMarks(ctx, poss);
            if (_intents != null) _intents.Fill(ctx, w);
            w.CountNearBall(_p.CrowdRadius);
        }

        static int Slot(int team, int local) => local >= 0 ? team * Squad.SlotsPerTeam + local : -1;

        /// <summary>마크 배정 (FR-19b): 수비 팀만, 0.5 s 주기(또는 소유 변화) 사이에는 바꾸지 않는다. 앵커에서 가장 가까운 상대 필드 선수(20 m 안), 슬롯 순 탐욕.</summary>
        void AssignMarks(MatchContext ctx, int poss)
        {
            var w = _w;
            if (ctx.Tick < _nextMarkTick && poss == _lastMarkPossTeam) return;
            _nextMarkTick = ctx.Tick + (long)(_p.MarkPeriod * SimClock.TicksPerSecond + 0.5f);
            _lastMarkPossTeam = poss;
            System.Array.Clear(_assigned, 0, N);
            for (int s = 0; s < N; s++) w.MarkTarget[s] = -1;
            if (poss < 0) return;
            var def = 1 - poss;
            for (int s = def * Squad.SlotsPerTeam + 1; s < (def + 1) * Squad.SlotsPerTeam; s++)
            {
                if (!w.Active[s]) continue;
                int best = -1;
                float bd = _p.MarkRadius * _p.MarkRadius;
                for (int o = poss * Squad.SlotsPerTeam + 1; o < (poss + 1) * Squad.SlotsPerTeam; o++)
                {
                    if (!w.Active[o] || _assigned[o]) continue;
                    var d = (w.Pos[o] - w.Anchor[s]).LengthSq();
                    if (d < bd) { bd = d; best = o; }
                }
                if (best >= 0) { w.MarkTarget[s] = best; _assigned[best] = true; }
            }
        }

        void Rebind(int s, int playerId)
        {
            _playerAt[s] = playerId;
            if (playerId >= 0 && _players.TryGetValue(playerId, out var p))
            {
                _w.Mind[s] = p.Mind.OrAverage();
                _w.Tech[s] = p.Technical.OrAverage();
                _style.SetStyle(s, p.Style);
            }
            else
            {
                _w.Mind[s] = DecisionAttributes.Average;
                _w.Tech[s] = Actions.TechnicalAttributes.Average;
                _style.SetStyle(s, null);
            }
            _state[s] = PlayerDecisionState.Initial; // 교체·자리 이동: 결정 상태 제거 (경계: 배역 선수 교체)
            _onBallUntil[s] = long.MinValue / 2;
            _beatenUntil[s] = long.MinValue / 2;
            _onBallTick[s] = long.MinValue / 2;
            if (_chemSet) _chem.ClearSlot(s); // 슬롯 키 케미: 교체로 다른 선수가 오면 물려받지 않는다 (033 연결 때 선수 id 키로, 결정론 리뷰 M)
            _hasAnchor[s] = false;
        }

        public void WriteState(StateWriter w)
        {
            w.Write(_x.RestartKickTick);
            _x.WritePass(w);
            for (int s = 0; s < N; s++)
            {
                w.Write((byte)_intent[s].Kind); w.Write(_intent[s].Point); w.Write(_intent[s].TargetSlot); w.Write(_intent[s].MaxSpeed);
                _state[s].Write(w);
                w.Write(_lastAnchor[s]); w.Write(_hasAnchor[s]); w.Write(_playerAt[s]); w.Write(_prevIntentKey[s]); w.Write(_w.MarkTarget[s]);
                w.Write(_onBallUntil[s]); w.Write(_beatenUntil[s]); w.Write(_onBallTick[s]);
            }
            w.Write(_lossTick[0]); w.Write(_lossTick[1]); w.Write(_lastPossTeam); w.Write(_nextMarkTick); w.Write(_lastMarkPossTeam); w.Write(_passTarget); w.Write(_passTick);
            _keys.Write(w);
        }

        public void ReadState(StateReader r)
        {
            _x.RestartKickTick = r.ReadLong();
            _x.ReadPass(r);
            for (int s = 0; s < N; s++)
            {
                _intent[s] = new Decision { Kind = (DecisionKind)r.ReadByte(), Point = r.ReadVec2(), TargetSlot = r.ReadInt(), MaxSpeed = r.ReadFloat() };
                _state[s] = PlayerDecisionState.Read(r);
                _lastAnchor[s] = r.ReadVec2(); _hasAnchor[s] = r.ReadBool();
                var pid = r.ReadInt();
                _prevIntentKey[s] = r.ReadInt(); _w.MarkTarget[s] = r.ReadInt();
                var obu = r.ReadLong(); var btu = r.ReadLong(); var obt = r.ReadLong();
                if (pid != _playerAt[s])
                {
                    var st = _state[s]; var la = _lastAnchor[s]; var ha = _hasAnchor[s];
                    Rebind(s, pid); // 스타일·정신 다시 연결 (상태는 읽은 값으로 되돌림)
                    _state[s] = st; _lastAnchor[s] = la; _hasAnchor[s] = ha;
                }
                _onBallUntil[s] = obu; _beatenUntil[s] = btu; _onBallTick[s] = obt;
            }
            _lossTick[0] = r.ReadLong(); _lossTick[1] = r.ReadLong(); _lastPossTeam = r.ReadInt(); _nextMarkTick = r.ReadLong(); _lastMarkPossTeam = r.ReadInt(); _passTarget = r.ReadInt(); _passTick = r.ReadLong();
            _keys.Read(r);
        }

        public void AddTo(ref StableHasher h)
        {
            h.Add(_x.RestartKickTick);
            _x.AddPassTo(ref h);
            for (int s = 0; s < N; s++)
            {
                h.Add((byte)_intent[s].Kind); h.Add(_intent[s].Point.X); h.Add(_intent[s].Point.Y); h.Add(_intent[s].TargetSlot); h.Add(_intent[s].MaxSpeed);
                _state[s].AddTo(ref h);
                h.Add(_lastAnchor[s].X); h.Add(_lastAnchor[s].Y); h.Add(_hasAnchor[s]); h.Add(_playerAt[s]); h.Add(_prevIntentKey[s]); h.Add(_w.MarkTarget[s]);
                h.Add(_onBallUntil[s]); h.Add(_beatenUntil[s]); h.Add(_onBallTick[s]);
            }
            h.Add(_lossTick[0]); h.Add(_lossTick[1]); h.Add(_lastPossTeam); h.Add(_nextMarkTick); h.Add(_lastMarkPossTeam); h.Add(_passTarget); h.Add(_passTick);
            _keys.AddTo(ref h);
        }
    }
}
