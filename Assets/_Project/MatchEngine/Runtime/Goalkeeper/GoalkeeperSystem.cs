using RoadToPro.Core.Hashing;
using RoadToPro.Core.Math;
using RoadToPro.Core.Random;
using RoadToPro.Core.Serialization;
using RoadToPro.Core.Time;
using RoadToPro.MatchEngine.Ball;
using RoadToPro.MatchEngine.Match;
using RoadToPro.MatchEngine.Match.Events;

namespace RoadToPro.MatchEngine.Goalkeeper
{
    /// <summary>
    /// 경기당 GK 시스템 (specs/011): 두 GK의 매핑 결과·목표 위치(FR-01~07)·다이빙 상태(FR-17)와 슛 감시자(FR-10~15)를 묶는다.
    /// <see cref="MatchContext.Keepers"/>. 호출 순서: 단계 2(형태) <see cref="UpdateTarget"/> 팀 0 → 1, 단계 7(물리) 서브스텝마다 <see cref="OnSubstep"/>.
    /// RNG는 전용 스트림 <see cref="RngStreamId.Goalkeeper"/> 하나 — 위치 오차 재추첨과 선방 결과만 쓴다.
    /// </summary>
    public sealed class GoalkeeperSystem
    {
        readonly GoalkeeperParams _p;
        readonly KeeperPositioner _pos;
        readonly SaveModel _save;
        readonly ShotWatcher _watch;
        readonly SeedRng _rng;
        readonly KeeperProfile _league;
        readonly KeeperProfile _field;

        readonly KeeperProfile[] _prof = new KeeperProfile[2];
        readonly int[] _profId = { int.MinValue, int.MinValue };
        readonly Vec2[] _target = new Vec2[2];
        readonly bool[] _hasTarget = new bool[2];
        readonly Vec2[] _noise = new Vec2[2];
        readonly long[] _lastEval = new long[2];
        readonly bool[] _inRange = new bool[2];
        readonly int[] _possSeen = { -2, -2 };
        readonly DiveMotion[] _dive = new DiveMotion[2];
        readonly long _lowRateTicks, _landSteps, _recoverSteps;
        readonly KeeperChooser _chooser;
        readonly KeeperCandidate[] _cands = new KeeperCandidate[KeeperChooser.MaxCandidates];
        readonly KeeperActionKind[] _lastChoice = new KeeperActionKind[2];
        readonly KeeperPlan[] _plan = { KeeperPlan.Empty, KeeperPlan.Empty };
        readonly PenaltyPlan[] _pk = new PenaltyPlan[2];
        readonly HoldClock[] _hold = new HoldClock[2];
        int _flightSeq;
        readonly SweeperModel _sweeper;
        readonly CrossClaimModel _cross;
        readonly OneOnOneModel _oneOnOne;
        readonly BallPrediction _pred = new BallPrediction(160);
        readonly PenaltyKeeperModel _pkModel;
        readonly DistributionModel _dist;

        /// <summary>역할 (FR-71). 003 연결 전에는 Standard. 경기 시작 전에 설정.</summary>
        public readonly KeeperRole[] Roles = { KeeperRole.Standard, KeeperRole.Standard };

        /// <summary>진단 싱크 (FR-82). null이면 호출 0.</summary>
        public IKeeperDiagnostics Diagnostics { get; set; }

        public GoalkeeperSystem(GoalkeeperParams p, BallParams bp, ulong seed)
        {
            _p = p;
            var grid = new GoalGrid(p);
            _pos = new KeeperPositioner(p, grid);
            _save = new SaveModel(p, _pos);
            _save.Bind(bp);
            _watch = new ShotWatcher(p, _save);
            _chooser = new KeeperChooser(p);
            _sweeper = new SweeperModel(p);
            _cross = new CrossClaimModel(p);
            _oneOnOne = new OneOnOneModel(p);
            _pkModel = new PenaltyKeeperModel(p);
            _dist = new DistributionModel(p);
            _rng = RngStreams.Create(seed, RngStreamId.Goalkeeper);
            _league = KeeperProfile.From(p, KeeperAttributes.Uniform(p.LeagueDisplay));
            _field = KeeperProfile.From(p, KeeperAttributes.Uniform(p.FieldDisplay));
            _lowRateTicks = (long)DMath.Floor(p.PosLowRate * SimClock.TicksPerSecond + 0.5f);
            _landSteps = (long)DMath.Floor(p.DiveLand / bp.Dt + 0.5f);
            _recoverSteps = (long)DMath.Floor(p.DiveRecover / bp.Dt + 0.5f);
        }

        public GoalkeeperParams Params => _p;
        public KeeperPositioner Positioner => _pos;
        public SaveModel Save => _save;
        public ShotWatcher Watcher => _watch;
        internal SeedRng Rng => _rng;
        public KeeperProfile LeagueAverage => _league;

        public ref readonly DiveMotion Dive(int team) => ref _dive[team];
        public KeeperChooser Chooser => _chooser;
        public SweeperModel Sweeper => _sweeper;
        public CrossClaimModel Cross => _cross;
        public OneOnOneModel OneOnOne => _oneOnOne;
        public PenaltyKeeperModel PenaltyModel => _pkModel;
        public DistributionModel Distribution => _dist;
        /// <summary>다이빙·일어나기 중 — 결정·잡기에서 빠진다(FR-17).</summary>
        public bool IsBusy(int team) => _dive[team].Busy;
        public bool IsBusySlot(int slot) => Squad.IsKeeperSlot(slot) && _dive[Squad.TeamOfSlot(slot)].Busy;
        public bool HasTarget(int team) => _hasTarget[team];
        public Vec2 Target(int team) => _target[team];

        /// <summary>
        /// GK 슬롯 선수의 매핑 결과. 슬롯 선수가 바뀌면 다시 계산: 능력치 지정 → 그 값, 미지정 선발 GK → 리그 평균(표시 10.5),
        /// 미지정 다른 선수(필드 선수가 GK를 맡음) → 표시 2 대체값 (경계 상황, AC-15).
        /// </summary>
        public KeeperProfile Profile(MatchContext ctx, int team)
        {
            var slot = team * Squad.SlotsPerTeam;
            var id = ctx.Squad.IsSlotFilled(slot) ? ctx.Squad.PlayerAt(slot) : -1;
            if (id == _profId[team]) return _prof[team];
            _profId[team] = id;
            var setup = ctx.Setup.Team(team);
            if (id >= 0 && setup.TryGetPlayer(id, out var mp) && mp.Keeper.IsSet) _prof[team] = KeeperProfile.From(_p, mp.Keeper);
            else if (id >= 0 && setup.Starters.Length > 0 && setup.Starters[0] == id) _prof[team] = _league;
            else _prof[team] = _field;
            return _prof[team];
        }

        /// <summary>
        /// GK 목표 위치 갱신 (FR-01~07, 단계 2). lineHeight = 자기 골라인에서 009 수비 라인까지(m, 모르면 음수), highBlock = DefHigh 국면.
        /// 슈팅 가능 거리 밖에서는 0.3 s마다만 다시 계산(FR-04). 위치 오차는 공 소유 변경·슈팅 가능 거리 진입 때만 재추첨(FR-06).
        /// </summary>
        public Vec2 UpdateTarget(MatchContext ctx, int team, float lineHeight, bool highBlock)
        {
            var slot = team * Squad.SlotsPerTeam;
            if (!ctx.IsSlotActive(slot)) { _hasTarget[team] = false; return _target[team]; }
            var prof = Profile(ctx, team);
            var goal = ctx.OwnGoal(team);
            var dirIn = (float)ctx.AttackDir(team);
            ref readonly var ball = ref ctx.Ball;
            var bxy = new Vec2(ball.Position.X, ball.Position.Y);
            var inRange = (bxy - goal).LengthSq() <= _p.PosShootRange * _p.PosShootRange;
            var poss = ball.Phase == BallPhase.Controlled && ball.OwnerId >= 0 ? Squad.TeamOfSlot(ball.OwnerId) : _possSeen[team];
            if (poss != _possSeen[team] || (inRange && !_inRange[team]))
            {
                var sigma = prof.PositionSigma;
                _noise[team] = sigma > 0f ? new Vec2(_rng.Normal(0f, sigma), _rng.Normal(0f, sigma)) : Vec2.Zero;
                _possSeen[team] = poss;
            }
            _inRange[team] = inRange;
            if (!inRange && _hasTarget[team] && ctx.Tick - _lastEval[team] < _lowRateTicks) return _target[team];
            var inp = new PositionInput
            {
                Goal = goal, DirIn = dirIn, Ball = bxy, LineHeight = lineHeight, HighBlock = highBlock, Role = Roles[team],
                Noise = _noise[team], HasPrev = _hasTarget[team], Prev = _target[team],
            };
            var t = _pos.Target(in inp, in prof, team, Diagnostics);
            // 피치 안 (운동 경계와 같은 기준)
            t = new Vec2(Interp.Clamp(t.X, 0f, ctx.Pitch.LengthM), Interp.Clamp(t.Y, 0f, ctx.Pitch.WidthM));
            _target[team] = t;
            _hasTarget[team] = true;
            _lastEval[team] = ctx.Tick;
            return t;
        }

        /// <summary>
        /// GK 결정 (011 FR-16·94 + 073 FR-01~31, 단계 5): 후보 {위치 선정, 박스 안 느슨한 공 경합, 돌진, 크로스 잡기·펀칭, 1v1 스마더·세트, 의도 지점 이동}의
        /// 기대효용을 010 선택 규칙으로 고른다. 공을 가진 GK·데드볼은 호출하지 않는다(대체 판단이 처리). 시나리오 의도는 효용 보너스로만 들어온다(FR-31).
        /// 073 후보는 010 파이프라인(<c>Duels.Enabled</c>)에서만 — 006 대체 파이프라인은 011 그대로.
        /// 스위퍼·크로스는 비행 하나에 한 번 확정(후보가 있었을 때), 1v1은 결정 틱마다 다시 보되 스마더·세트 정지 시각은 캐리어 기준으로 유지.
        /// 호출 순서(후보·RNG): 위치 → 경합 → 크로스(잡기·펀칭) 또는 돌진 → 1v1 → 의도 이동 → 직전 선택 보너스 → softmax 1회 → (잡기·펀칭) 결단 노이즈 1회 → (새 세트) 정지 오차 1회.
        /// </summary>
        public KeeperActionKind Decide(MatchContext ctx, int team, in KeeperIntent intent, out Vec2 point)
        {
            // 결정론 리뷰 L1 (073 T19): GK 판단이 액션 월드를 이 틱에 처음 채웠으면 끝나고 되돌린다 — 같은 틱 물리 단계(071 경합·070 결투)가
            // 073 전과 같은 시점에 월드를 채우게 (안 그러면 결정 단계 위치로 고정돼 경합 참여자가 바뀐다, AerialChaseTests)
            var borrowed = ctx.Actions.Duels.Enabled && !ctx.Actions.WorldFresh(ctx);
            var kind = DecideCore(ctx, team, in intent, out point);
            if (borrowed && ctx.Actions.WorldFresh(ctx)) ctx.Actions.InvalidateWorld();
            return kind;
        }

        KeeperActionKind DecideCore(MatchContext ctx, int team, in KeeperIntent intent, out Vec2 point)
        {
            var slot = team * Squad.SlotsPerTeam;
            var prof = Profile(ctx, team);
            ref readonly var me = ref ctx.SlotState(slot);
            var pos = _hasTarget[team] ? _target[team] : me.Position;
            ref readonly var ball = ref ctx.Ball;
            ref var plan = ref _plan[team];
            var on = ctx.Actions.Duels.Enabled;
            var loose = ball.Phase != BallPhase.Controlled;
            var opp = 1 - team;
            if (on)
            {
                plan.Seen = _flightSeq;
                if (plan.Flight >= 0 && plan.Flight != _flightSeq) { plan.ClearFlight(); plan.Flight = -1; }
                if (plan.RushOwned && !(ball.Phase == BallPhase.Controlled && ball.OwnerId == slot)) plan.RushOwned = false;
                MaintainOneOnOne(ctx, team, ref plan, in prof);
                // 확정된 비행 계획 유지 (매 틱 뒤집힘 방지)
                if (plan.Flight == _flightSeq && loose)
                {
                    if (plan.Rush) { point = plan.RushPoint; return Remember(team, KeeperActionKind.Rush); }
                    if (plan.Cross == CrossMode.Claim || plan.Cross == CrossMode.Punch)
                    {
                        if (ctx.Tick < plan.CrossReadyTick) { point = pos; return Remember(team, KeeperActionKind.Position); } // FR-12 결단 지연
                        point = plan.Landing;
                        return Remember(team, plan.Cross == CrossMode.Claim ? KeeperActionKind.Claim : KeeperActionKind.Punch);
                    }
                }
                if (plan.OneOnOne == OneOnOneMode.Smother) { point = new Vec2(ball.Position.X, ball.Position.Y); return Remember(team, KeeperActionKind.Smother); }
            }
            int n = 0;
            _cands[n++] = new KeeperCandidate { Kind = KeeperActionKind.Position, Point = pos, Utility = _p.ChoicePositionUtility };
            if (loose)
            {
                var target = new Vec2(ball.Position.X + ball.Velocity.X * _p.ChoiceContestLookahead, ball.Position.Y + ball.Velocity.Y * _p.ChoiceContestLookahead);
                if (Match.Stages.PhysicsStage.InOwnBox(ctx, team, new Vec3(target.X, target.Y, 0f)))
                {
                    var vmax = ctx.Kinematics.Profile(slot).MaxSpeed;
                    var tGk = (target - me.Position).Length() / DMath.Max(vmax, 0.1f) + prof.ReactTime;
                    var tOpp = float.MaxValue;
                    for (int s = opp * Squad.SlotsPerTeam; s < (opp + 1) * Squad.SlotsPerTeam; s++)
                    {
                        if (!ctx.IsSlotControllable(s)) continue;
                        var t = (target - ctx.SlotState(s).Position).Length() / DMath.Max(ctx.Kinematics.Profile(s).MaxSpeed, 0.1f);
                        if (t < tOpp) tOpp = t;
                    }
                    var pWin = tOpp == float.MaxValue ? 1f : KeeperPositioner.Sigmoid((tOpp - tGk) / _p.ChoiceContestScale);
                    _cands[n++] = new KeeperCandidate { Kind = KeeperActionKind.Contest, Point = target, Utility = pWin * _p.ChoiceContestWin - (1f - pWin) * _p.ChoiceContestFail };
                }
            }
            var flightCands = false;
            var gate = default(SweepGate);
            var cross = default(CrossEval);
            var pRush = 0f;
            var oo = default(OneOnOneEval);
            if (on)
            {
                var goal = ctx.OwnGoal(team);
                var dirIn = (float)ctx.AttackDir(team);
                var act = ctx.Actions;
                if (loose && plan.Flight != _flightSeq && ctx.LastTouchTeam == opp && !_watch.Tracks(slot))
                {
                    act.Refresh(ctx);
                    var w = act.World;
                    if (act.Aerials.TryLanding(ctx, out var landing, out var tLand, out var kickerTeam) && kickerTeam == opp)
                    {
                        // FR-10·11 크로스: 손 도달 높이는 필드 선수 컨트롤 상한(act.ctrlMaxZ) 위를 잡을 수 있는지로 본다
                        cross = _cross.Evaluate(w, team, in prof, w.HeightCm[slot] / 100f, goal, dirIn, landing, tLand, act.P.CtrlMaxZ, act.P.AerialTtiSlack);
                        if (cross.Has)
                        {
                            flightCands = true;
                            _cands[n++] = new KeeperCandidate { Kind = KeeperActionKind.Claim, Point = landing, Utility = cross.UClaim };
                            _cands[n++] = new KeeperCandidate { Kind = KeeperActionKind.Punch, Point = landing, Utility = cross.UPunch };
                        }
                    }
                    else if (ball.Velocity.X * dirIn < 0f && (new Vec2(ball.Position.X, ball.Position.Y) - goal).LengthSq() <= _p.SweepRange * _p.SweepRange)
                    {
                        // FR-01~03 스위퍼: 자기 골 쪽으로 오는 상대 킥만 (경로 예측은 이때 1회)
                        BallPredictor.Predict(in ball, ctx.BallParams, SweepSampleInterval(ctx.BallParams.Dt), SweepHorizon(ctx.BallParams.Dt), _pred);
                        gate = _sweeper.Gate(w, team, in prof, Roles[team], goal, dirIn, _pred.Times, _pred.Positions, _pred.Count, 0f, act.P.CtrlMaxZ);
                        if (gate.Has) flightCands = true;
                        if (gate.Pass)
                        {
                            pRush = SweeperModel.PWin(w.PitchControl, w.ControlPlayersView, team, gate.Point, gate.BallTime, gate.OutsideBox);
                            var xg = act.Shots.Xg(ctx, gate.Attacker, gate.Point, 0f);
                            _cands[n++] = new KeeperCandidate { Kind = KeeperActionKind.Rush, Point = gate.Point, Utility = _sweeper.Utility(pRush, xg, _sweeper.EmptyXg(gate.Point, goal)) };
                        }
                    }
                }
                // FR-20 1v1
                var carrier = -1;
                var ballLoose = false;
                if (!loose && ball.OwnerId >= 0 && Squad.TeamOfSlot(ball.OwnerId) == opp) carrier = ball.OwnerId;
                else if (loose && ctx.LastTouchSlot >= 0 && Squad.TeamOfSlot(ctx.LastTouchSlot) == opp && !_watch.Tracks(slot) && ball.Position.Z <= act.P.CtrlMaxZ
                    && ctx.IsSlotControllable(ctx.LastTouchSlot) && (ctx.SlotState(ctx.LastTouchSlot).Position - new Vec2(ball.Position.X, ball.Position.Y)).LengthSq() <= _p.OneOnOneCarrierGap * _p.OneOnOneCarrierGap)
                {
                    carrier = ctx.LastTouchSlot;
                    ballLoose = true;
                }
                if (carrier >= 0)
                {
                    act.Refresh(ctx);
                    oo = _oneOnOne.Evaluate(act.World, team, in prof, goal, dirIn, carrier, new Vec2(ball.Position.X, ball.Position.Y), ballLoose);
                    if (oo.Has)
                    {
                        if (oo.Smother) _cands[n++] = new KeeperCandidate { Kind = KeeperActionKind.Smother, Point = oo.Ball, Utility = oo.USmother };
                        else
                        {
                            // FR-20 세트: 이등분선 위 전진 = 011 위치 선정 목표(이등분선·깊이 0.5~6 m·위험 최소). 정지 여부는 선방 판정에서 정지 예정 틱으로(<see cref="SaveVelocity"/>)
                            // — 자리에 얼어붙으면 캐리어가 옆으로 움직일 때 각을 잃는다 (073 T17 측정: 고정 정지 선방률 61.9 → 50.6 %)
                            _cands[n++] = new KeeperCandidate { Kind = KeeperActionKind.Set, Point = pos, Utility = oo.USet };
                        }
                    }
                }
            }
            if (intent.Has && intent.Kind == KeeperActionKind.MoveTo)
                _cands[n++] = new KeeperCandidate { Kind = KeeperActionKind.MoveTo, Point = intent.Point, Utility = _p.ChoicePositionUtility - _p.ChoiceMoveCost * (intent.Point - pos).Length() };
            for (int i = 0; i < n; i++)
                if (_cands[i].Kind == _lastChoice[team] && _cands[i].Utility >= 0f) _cands[i].Utility += _p.ChoiceStickiness;
            var pick = _chooser.Choose(_cands, n, in intent, prof.DecisionsN, _rng);
            var kind = _cands[pick].Kind;
            point = _cands[pick].Point;
            if (on)
            {
                if (flightCands)
                {
                    // 비행 하나에 한 번 확정 (FR-02 "EU > 0이면 실행" — 음수 후보는 선택기가 뺐다)
                    plan.ClearFlight();
                    plan.Flight = _flightSeq;
                    if (kind == KeeperActionKind.Rush)
                    {
                        plan.Rush = true;
                        plan.RushPoint = gate.Point;
                        var e = SimEvent.Make(SimEventKind.KeeperRush, team, ctx.PlayerAtSlot(slot));
                        e.F0 = gate.Delta;
                        e.F1 = pRush;
                        e.B = gate.OutsideBox ? 1 : 0;
                        ctx.SetPosition(ref e, new Vec3(gate.Point.X, gate.Point.Y, 0f));
                        ctx.Emit(e);
                    }
                    else if (kind == KeeperActionKind.Claim || kind == KeeperActionKind.Punch)
                    {
                        plan.Cross = kind == KeeperActionKind.Claim ? CrossMode.Claim : CrossMode.Punch;
                        plan.Landing = cross.Landing;
                        plan.CrossP = kind == KeeperActionKind.Claim ? cross.PClaim : cross.PPunch;
                        var delay = _cross.Delay(in prof, _rng);
                        plan.CrossReadyTick = ctx.Tick + (long)DMath.Floor(delay * SimClock.TicksPerSecond + 0.5f);
                        if (ctx.Tick < plan.CrossReadyTick) { kind = KeeperActionKind.Position; point = pos; }
                    }
                    else if (cross.Has) plan.Cross = CrossMode.Abandon;
                }
                if (kind == KeeperActionKind.Smother)
                {
                    plan.OneOnOne = OneOnOneMode.Smother;
                    plan.Carrier = oo.Carrier;
                    plan.OneOnOneFlight = _flightSeq;
                }
                else if (kind == KeeperActionKind.Set)
                {
                    if (plan.OneOnOne != OneOnOneMode.Set || plan.Carrier != oo.Carrier)
                    {
                        var err = OneOnOneModel.StopError(in prof, _rng);
                        var stop = (long)DMath.Floor((oo.TimeToShot + err) * SimClock.TicksPerSecond + 0.5f);
                        plan.StopTick = ctx.Tick + (stop > 0 ? stop : 0);
                    }
                    plan.OneOnOne = OneOnOneMode.Set;
                    plan.Carrier = oo.Carrier;
                    plan.SetPoint = pos;
                }
                else if (!oo.Has) plan.ClearOneOnOne();
            }
            return Remember(team, kind);
        }

        /// <summary>경로 샘플 간격: 004 예측기 범위 [0.04, 0.1] s 안·서브스텝 정수배로 맞춘다 (튜닝 값이 어긋나도 예외 없음 — 결정론 리뷰 M2).</summary>
        float SweepSampleInterval(float dt)
        {
            var v = _p.SweepSampleDt < BallPredictor.MinSampleInterval ? BallPredictor.MinSampleInterval : (_p.SweepSampleDt > BallPredictor.MaxSampleInterval ? BallPredictor.MaxSampleInterval : _p.SweepSampleDt);
            var steps = (int)DMath.Floor(v / dt + 0.5f);
            var lo = (int)DMath.Floor(BallPredictor.MinSampleInterval / dt + 0.999f); // 올림
            var hi = (int)DMath.Floor(BallPredictor.MaxSampleInterval / dt + 1e-3f);
            if (steps < lo) steps = lo;
            if (steps > hi) steps = hi;
            return steps * dt;
        }

        /// <summary>경로 길이: (0, 10] s 안·샘플 간격 정수배·버퍼 용량 안.</summary>
        float SweepHorizon(float dt)
        {
            var si = SweepSampleInterval(dt);
            var n = (int)DMath.Floor(_p.SweepHorizon / si + 0.5f);
            if (n < 1) n = 1;
            var max = (int)DMath.Floor(BallPredictor.MaxHorizonSeconds / si + 1e-3f);
            if (n > max) n = max;
            if (n > _pred.Capacity) n = _pred.Capacity;
            return n * si;
        }

        KeeperActionKind Remember(int team, KeeperActionKind kind)
        {
            _lastChoice[team] = kind;
            return kind;
        }

        /// <summary>1v1 계획 유효성: 캐리어가 빠지거나 다른 선수가 공을 만졌으면 끝. 스마더 중 캐리어가 공을 다시 찼으면 취소 불가 구간(공까지 commitTime) 밖일 때 끝.</summary>
        void MaintainOneOnOne(MatchContext ctx, int team, ref KeeperPlan plan, in KeeperProfile prof)
        {
            if (plan.OneOnOne == OneOnOneMode.None) return;
            ref readonly var ball = ref ctx.Ball;
            var c = plan.Carrier;
            var owned = ball.Phase == BallPhase.Controlled && ball.OwnerId == c;
            var stillCarrier = c >= 0 && ctx.IsSlotControllable(c) && (owned || (ball.Phase != BallPhase.Controlled && ctx.LastTouchSlot == c));
            if (!stillCarrier || (plan.OneOnOne == OneOnOneMode.Smother && owned)) { plan.ClearOneOnOne(); return; } // 캐리어가 다시 공을 붙이면 덮칠 공이 없다
            if (plan.OneOnOne == OneOnOneMode.Smother && ball.Phase != BallPhase.Controlled && plan.OneOnOneFlight != _flightSeq)
            {
                var slot = team * Squad.SlotsPerTeam;
                ctx.Actions.Refresh(ctx);
                var t = Kinematics.TimeToIntercept.EstimateNoReaction(in ctx.Actions.World.Tti[slot], ball.Position.X, ball.Position.Y);
                if (!(t <= _p.OneOnOneCommitTime)) plan.ClearOneOnOne(); // 경계: 패스 전환 — 취소 불가 구간 밖이면 위치 선정으로
                else plan.OneOnOneFlight = _flightSeq;
            }
        }

        /// <summary>073: 새 상대 비행이 아직 평가되지 않았으면 결정 주기와 무관하게 이번 틱 결정 (킥 직후 1회), 결단 지연이 끝나는 틱에도 1회.</summary>
        public bool WantsDecision(MatchContext ctx, int team)
        {
            if (!ctx.Actions.Duels.Enabled || !ctx.Flow.InPlay) return false;
            ref readonly var plan = ref _plan[team];
            if (MustClear(ctx, team * Squad.SlotsPerTeam) && ctx.Ball.Phase == BallPhase.Controlled && ctx.Ball.OwnerId == team * Squad.SlotsPerTeam) return true; // FR-04 바로 걷어내기
            if (plan.Flight == _flightSeq && (plan.Cross == CrossMode.Claim || plan.Cross == CrossMode.Punch) && ctx.Tick == plan.CrossReadyTick) return true;
            return ctx.Ball.Phase != BallPhase.Controlled && plan.Flight != _flightSeq && plan.Seen != _flightSeq && ctx.LastTouchTeam == 1 - team;
        }

        /// <summary>
        /// 073 FR-20·22: 선방 판정에 넘길 GK 속도. 1v1 세트 중이면 '슛 직전 정지했는가'를 정지 예정 틱(예상 슈팅 시각 + N(0, σ_t))으로 정한다 —
        /// 지났으면 정지(속도 0), 아니면 움직이는 중(정지 자세 속도를 넘는 값 → 011 FR-09 reach 패널티). 세트가 아니면 실제 속도(011 그대로).
        /// (073 T17: 정지 뒤 자리 고정·속도 상한은 각을 잃어 선방률 61.9 → 50.6 / 59.7 % — 위치는 011 목표를 그대로 따라가고 정지 판정만 시각으로)
        /// </summary>
        public Vec2 SaveVelocity(MatchContext ctx, int team, Vec2 actual)
        {
            ref readonly var plan = ref _plan[team];
            if (!ctx.Actions.Duels.Enabled || plan.OneOnOne != OneOnOneMode.Set) return actual;
            if (ctx.Tick >= plan.StopTick) return Vec2.Zero;
            var v = actual.Length();
            var min = _p.StillSpeed * 1.01f + 1e-3f;
            if (v >= min) return actual;
            return v > 1e-4f ? actual * (min / v) : new Vec2(min, 0f);
        }

        /// <summary>073: 지금 비행의 크로스 결정을 확정했다 (잡기·펀칭·포기) — 010 뜬 공 착지점 추격(081 FR-24)에서 GK를 뺀다.</summary>
        public bool HandlesAerial(int team) => _plan[team].Flight == _flightSeq && _plan[team].Cross != CrossMode.None;

        /// <summary>073: 지금 비행에 확정된 크로스 모드 (없으면 None).</summary>
        public CrossMode AerialMode(int team) => _plan[team].Flight == _flightSeq ? _plan[team].Cross : CrossMode.None;

        /// <summary>073 FR-11·12: GK가 071 공중볼 경합에 들어가지 않는다 — 포기, 결단 지연 중, 이미 잡기 실패.</summary>
        public bool SkipsAerial(MatchContext ctx, int slot)
        {
            if (!Squad.IsKeeperSlot(slot) || !ctx.Actions.Duels.Enabled) return false;
            ref readonly var plan = ref _plan[Squad.TeamOfSlot(slot)];
            if (plan.Flight != _flightSeq) return false;
            if (plan.ClaimFailed || plan.Cross == CrossMode.Abandon) return true;
            return (plan.Cross == CrossMode.Claim || plan.Cross == CrossMode.Punch) && ctx.Tick < plan.CrossReadyTick;
        }

        /// <summary>
        /// 073 FR-11: 071 경합에서 잡기 모드 GK가 이겼다 → p_claim으로 성공 판정 (GK RNG 1회). 실패면 ClaimFail 이벤트를 내고 이 비행에서 GK 손 잡기를 뺀다.
        /// </summary>
        public bool RollClaim(MatchContext ctx, int team, int sub, Vec3 at)
        {
            ref var plan = ref _plan[team];
            if (_rng.NextFloat() < plan.CrossP) return true;
            plan.ClaimFailed = true;
            var slot = team * Squad.SlotsPerTeam;
            var e = SimEvent.Make(SimEventKind.ClaimFail, team, ctx.PlayerAtSlot(slot));
            e.Substep = (sbyte)sub;
            e.Part = BodyPart.Hand;
            e.A = (int)KeeperMotionKind.ClaimJump;
            e.F0 = plan.CrossP;
            ctx.SetPosition(ref e, at);
            ctx.Emit(e);
            return false;
        }

        /// <summary>073 FR-21: 1v1 중 슈터가 스프레드 거리 안 — 슛 감시 시작 때 한 번 판정해 그 슛 동안 고정한다(<see cref="ShotWatcher.Spread"/>).</summary>
        public bool SpreadActive(MatchContext ctx, int team)
        {
            if (!ctx.Actions.Duels.Enabled || _plan[team].OneOnOne == OneOnOneMode.None) return false;
            var shooter = ctx.LastKickSlot;
            if (shooter < 0 || Squad.TeamOfSlot(shooter) == team || !ctx.IsSlotActive(shooter)) return false;
            var gk = ctx.SlotState(team * Squad.SlotsPerTeam).Position;
            return (ctx.SlotState(shooter).Position - gk).LengthSq() <= _p.OneOnOneSpreadDist * _p.OneOnOneSpreadDist;
        }

        /// <summary>073: 선방 판정·다이빙용 프로필 — 스프레드면 r0 + Δr0·v_dive 감소(FR-21), 아니면 매핑 그대로(011과 같음).</summary>
        public KeeperProfile ProfileFor(MatchContext ctx, int team, bool spread)
        {
            var prof = Profile(ctx, team);
            return spread ? _oneOnOne.Spread(in prof) : prof;
        }

        /// <summary>
        /// 073: GK가 느슨한 공을 잡았다 (008 수신 판정). 스마더 중 손으로 잡으면 Smother(306) 이벤트, 돌진 중 박스 밖에서 발로 잡으면 다음 공 동작은 클리어만(FR-04).
        /// </summary>
        public void OnKeeperClaim(MatchContext ctx, int slot, int sub, Vec3 at, bool hands, long abs)
        {
            if (!Squad.IsKeeperSlot(slot) || !ctx.Actions.Duels.Enabled) return;
            var team = Squad.TeamOfSlot(slot);
            ref var plan = ref _plan[team];
            var rushing = plan.Flight == _flightSeq && plan.Rush;
            plan.RushOwned = false;
            if (plan.OneOnOne == OneOnOneMode.Smother && hands)
            {
                var e = SimEvent.Make(SimEventKind.Smother, team, ctx.PlayerAtSlot(slot), plan.Carrier >= 0 ? ctx.PlayerAtSlot(plan.Carrier) : -1);
                e.Substep = (sbyte)sub;
                e.Part = BodyPart.Hand;
                e.A = (int)KeeperMotionKind.Smother;
                ctx.SetPosition(ref e, at);
                ctx.Emit(e);
            }
            plan.ClearOneOnOne();
            if (rushing && !hands) plan.RushOwned = true;
            plan.Rush = false;
            if (hands) StartHold(ctx, team, abs); // 074 FR-14: 손으로 잡은 순간부터 (캐치 경로와 같은 서브스텝 기준 — 결정론 리뷰 L)
        }

        // ── 074 손 보유 시간 · 배급 ──

        /// <summary>
        /// 074 FR-14: 손 보유 시계 시작 (캐치·손 잡기). 마감 = 목표 + |N(0, σ_hold·(1 − n_Decisions))| (GK RNG 1회), 빠른 스로 조건 = 캐치 순간 상대 미정렬
        /// (상대 필드 선수 중 자기 진영(라인 뒤)에 있는 수 &lt; unsetCount, AC-02). 010 파이프라인만.
        /// </summary>
        public void StartHold(MatchContext ctx, int team, long abs)
        {
            if (!ctx.Actions.Duels.Enabled) return;
            ref var h = ref _hold[team];
            var prof = Profile(ctx, team);
            var deadline = DistributionModel.DeadlineSeconds(_p, prof.DecisionsN, _rng);
            h.Active = true;
            h.StartAbs = abs;
            h.DeadlineAbs = abs + (long)DMath.Floor(deadline / ctx.BallParams.Dt + 0.5f);
            h.Quick = OpponentsBack(ctx, team) < (int)_p.DistUnsetCount;
        }

        /// <summary>상대 필드 선수 중 자기 진영(하프라인 자기 골 쪽)에 있는 수.</summary>
        static int OpponentsBack(MatchContext ctx, int team)
        {
            var opp = 1 - team;
            var half = ctx.Pitch.LengthM * 0.5f;
            var dir = ctx.AttackDir(opp);
            var n = 0;
            for (int s = opp * Squad.SlotsPerTeam + 1; s < (opp + 1) * Squad.SlotsPerTeam; s++)
                if (ctx.IsSlotControllable(s) && (ctx.SlotState(s).Position.X - half) * dir < 0f) n++;
            return n;
        }

        /// <summary>
        /// 074 FR-14: 손 보유 서브스텝 수 (100 Hz) — 지금 손에 들고 있지 않으면 −1. 012 8초 판정이 읽는다(위반 = 보유 &gt; 800).
        /// </summary>
        public long HoldSubsteps(MatchContext ctx, int team, long abs)
        {
            ref readonly var h = ref _hold[team];
            if (!h.Active) return -1;
            ref readonly var ball = ref ctx.Ball;
            if (ball.Phase != BallPhase.Controlled || ball.OwnerId != team * Squad.SlotsPerTeam) return -1;
            return abs - h.StartAbs;
        }

        /// <summary>012 8초 위반 판정 뒤 시계 초기화 (이중 판정 방지).</summary>
        public void ClearHold(int team) => _hold[team] = default;

        void EndLostHolds(MatchContext ctx)
        {
            ref readonly var ball = ref ctx.Ball;
            for (int t = 0; t < 2; t++)
            {
                if (!_hold[t].Active) continue;
                var gk = t * Squad.SlotsPerTeam;
                if (ball.Phase != BallPhase.Controlled || ball.OwnerId != gk || !ctx.Flow.InPlay
                    || !Match.Stages.PhysicsStage.InOwnBox(ctx, t, new Vec3(ctx.SlotState(gk).Position.X, ctx.SlotState(gk).Position.Y, 0f))) _hold[t] = default; // 놓쳤거나 경기 중단·박스 밖(발로 가진 공) — 시계 끝
            }
        }

        /// <summary>
        /// 074 FR-10~14: 공을 가진 GK의 배급 결정 (단계 5). source = 손(캐치 뒤)·골킥·발. 손이면 최소 대기(빠른 스로 조건이면 0) 전에는 박스 선 안쪽으로 걸어가고,
        /// 그 뒤 최선 EU ≥ goEu 또는 마감이 되면 배급한다. 발·골킥은 바로. 배급하면 공 동작을 판에 올리고 true, 기다리면 false(standPoint로 이동).
        /// RNG: softmax 1회(배급할 때만).
        /// </summary>
        public bool TryDistribute(MatchContext ctx, int slot, DistributionSource source, out Vec2 standPoint)
        {
            var team = Squad.TeamOfSlot(slot);
            standPoint = ctx.SlotState(slot).Position;
            var act = ctx.Actions;
            var borrowed = !act.WorldFresh(ctx); // 073 결정론 리뷰 L1과 같은 처리
            try
            {
                act.Refresh(ctx);
                var abs = ctx.Kinematics.Substep;
                ref var h = ref _hold[team];
                var held = source == DistributionSource.Hands;
                if (held)
                {
                    if (!h.Active) StartHold(ctx, team, abs);
                    standPoint = BoxInset(ctx, team, standPoint);
                    if ((abs - h.StartAbs) * ctx.BallParams.Dt < (h.Quick ? 0f : _p.HoldSettle)) return false;
                }
                var prof = Profile(ctx, team);
                var from = new Vec2(ctx.Ball.Position.X, ctx.Ball.Position.Y);
                var pressure = act.PressureOn(ctx, slot);
                var c = new DistributionContext
                {
                    Source = source, Quick = held && h.Quick, HighPress = pressure >= _p.DistPressHigh,
                    Directness = ctx.Board.Instructions(team).Directness, Role = Roles[team], DecisionsN = prof.DecisionsN,
                };
                var n = _dist.Evaluate(act.World, act.P, ctx.BallParams, slot, from, pressure, in c);
                DistributionCandidate pick;
                if (n == 0) pick = _dist.Forced(act.World, ctx.BallParams, slot, from, source);
                else
                {
                    var dt = ctx.BallParams.Dt;
                    if (held && !DistributionModel.ReleaseNow(_p, (abs - h.StartAbs) * dt, (h.DeadlineAbs - h.StartAbs) * dt, h.Quick, true, _dist.Candidate(_dist.Best()).Eu)) return false;
                    pick = _dist.Candidate(_dist.Choose(in c, _rng));
                }
                ctx.Board.SetBallAction(DistributionAction(slot, in pick, from, source == DistributionSource.GoalKick));
                return true;
            }
            finally
            {
                if (borrowed && act.WorldFresh(ctx)) act.InvalidateWorld();
            }
        }

        /// <summary>손에 든 GK의 이동 목표: 박스 선에서 boxInset 안쪽(공격 방향), 좌우는 박스 안으로 자름.</summary>
        Vec2 BoxInset(MatchContext ctx, int team, Vec2 at)
        {
            var goal = ctx.OwnGoal(team);
            var dir = ctx.AttackDir(team);
            var depth = Referee.RestartRules.BoxDepth - _p.HoldBoxInset;
            var half = Referee.RestartRules.BoxHalfWidth - _p.HoldBoxInset;
            var x = goal.X + dir * depth;
            var y = Interp.Clamp(at.Y, goal.Y - half, goal.Y + half);
            return new Vec2(x, y);
        }

        static Match.Pipeline.BallAction DistributionAction(int slot, in DistributionCandidate c, Vec2 from, bool restart)
        {
            var d = c.Aim - from;
            var len = d.Length();
            var u = len > 1e-5f ? d / len : new Vec2(1f, 0f);
            var cos = DMath.Cos(c.Elevation);
            return new Match.Pipeline.BallAction
            {
                Kind = Match.Pipeline.BallActionKind.Pass, KickerSlot = slot, TargetSlot = c.Target,
                Velocity = new Vec3(u.X * c.Speed * cos, u.Y * c.Speed * cos, c.Speed * DMath.Sin(c.Elevation)),
                HasIntent = true, Aim = c.Aim, Speed = c.Speed, Elevation = c.Elevation, XPass = c.P,
                Part = DistributionModel.IsHand(c.Kind) ? BodyPart.Hand : BodyPart.None, IsRestart = restart, DistKind = (byte)(c.Kind + 1),
            };
        }

        /// <summary>074: 배급 킥 실행 직후(008 Execute, Pass 이벤트 뒤) — Distribution(308) 이벤트, 손 보유 시계 끝.</summary>
        public void OnDistributed(MatchContext ctx, in Match.Pipeline.BallAction a, long abs, int sub)
        {
            var team = Squad.TeamOfSlot(a.KickerSlot);
            ref var h = ref _hold[team];
            var held = h.Active && a.KickerSlot == team * Squad.SlotsPerTeam;
            var e = SimEvent.Make(SimEventKind.Distribution, team, ctx.PlayerAtSlot(a.KickerSlot), a.TargetSlot >= 0 ? ctx.PlayerAtSlot(a.TargetSlot) : -1);
            e.Substep = (sbyte)sub;
            e.A = a.DistKind - 1;
            e.B = held ? (int)(abs - h.StartAbs) : -1;
            e.F0 = held ? (abs - h.StartAbs) * ctx.BallParams.Dt : 0f;
            e.F1 = a.XPass;
            e.Part = a.Part;
            ctx.SetPosition(ref e, ctx.Ball.Position);
            ctx.Emit(e);
            h = default;
        }

        /// <summary>073 FR-04: 돌진으로 박스 밖에서 공을 얻은 GK — 공 동작은 클리어만.</summary>
        public bool MustClear(MatchContext ctx, int slot) =>
            Squad.IsKeeperSlot(slot) && _plan[Squad.TeamOfSlot(slot)].RushOwned && !Match.Stages.PhysicsStage.InOwnBox(ctx, Squad.TeamOfSlot(slot), new Vec3(ctx.SlotState(slot).Position.X, ctx.SlotState(slot).Position.Y, 0f));

        /// <summary>물리 단계 서브스텝 훅 (공 적분 뒤·잡기 판정 전): 다이빙 진행 → 슛 감시.</summary>
        public void OnSubstep(MatchContext ctx, long abs, int sub)
        {
            UpdatePenalty(ctx, abs, sub); // 074 FR-01: PK 예약 킥 → GK 전략 · 조기 다이빙 (다이빙 진행 전)
            EndLostHolds(ctx); // 074 FR-14: 손 보유 시계 — 공을 놓쳤으면 끝
            for (int t = 0; t < 2; t++) StepDive(ctx, t, abs, sub);
            _watch.Step(ctx, this, abs, sub);
        }

        /// <summary>공의 비행이 바뀜 (킥·굴절). 물리 단계가 킥 적용 때 부른다.</summary>
        public void NotifyFlight()
        {
            _flightSeq++; // 073: 스위퍼·크로스 결정은 비행 하나에 한 번
            _plan[0].RushOwned = false; // 073: 돌진 소유 뒤 첫 킥(클리어)로 끝
            _plan[1].RushOwned = false;
            _watch.NotifyFlight();
        }

        /// <summary>073: 지금 공 비행 번호 (킥·굴절마다 +1).</summary>
        public int FlightSeq => _flightSeq;

        /// <summary>073: 팀 GK의 골문 이탈 계획 (읽기 전용 사본).</summary>
        public KeeperPlan Plan(int team) => _plan[team];

        /// <summary>074: 팀 GK의 PK 전략 (읽기 전용 사본).</summary>
        public PenaltyPlan Penalty(int team) => _pk[team];

        /// <summary>074: 팀 GK의 손 보유 시계 (읽기 전용 사본).</summary>
        public HoldClock Hold(int team) => _hold[team];

        /// <summary>추적 중인 슛의 GK이거나 다이빙 중인 GK면 물리 단계 잡기에서 뺀다.</summary>
        public bool ExcludedFromClaim(int slot) => _watch.Tracks(slot) || IsBusySlot(slot) || (Squad.IsKeeperSlot(slot) && _plan[Squad.TeamOfSlot(slot)].ClaimFailed && _plan[Squad.TeamOfSlot(slot)].Flight == _flightSeq); // 073: 잡기 실패한 비행

        /// <summary>
        /// 074 FR-01·03: 상대 PK 예약 킥(008 <c>PendingKick</c>)이 보이면 그 예약에 한 번 GK 전략을 뽑는다(GK RNG: 기울기 → t_e → 라인 이탈).
        /// 다이빙 시작 = 킥 실행 가능 서브스텝 − t_e(지났으면 지금). 그 서브스텝에 <see cref="SimEventKind.PenaltyKeeper"/>를 내고, 방향이 있으면 조기 다이빙을 시작한다.
        /// 010 파이프라인(<c>Duels.Enabled</c>)만 — 006 대체 파이프라인은 011 반응형 그대로.
        /// </summary>
        void UpdatePenalty(MatchContext ctx, long abs, int sub)
        {
            if (!ctx.Actions.Duels.Enabled) return;
            ref readonly var pk = ref ctx.Actions.Pending;
            if (pk.Active && pk.Action.IsPenalty && pk.Victim < 0)
            {
                var kicker = pk.Action.KickerSlot;
                var d = 1 - Squad.TeamOfSlot(kicker);
                ref var plan = ref _pk[d];
                if (!plan.Active || plan.KickDue != pk.Due)
                {
                    ctx.Actions.Refresh(ctx);
                    var natural = PenaltyKeeperModel.NaturalSign(ctx.Actions.World.PreferredFoot[kicker], ctx.AttackDir(1 - d));
                    var s = _pkModel.Draw(natural, 0, 1f, _rng);
                    var steps = (long)DMath.Floor(s.Lead / ctx.BallParams.Dt + 0.5f);
                    var start = pk.Due - steps;
                    plan = new PenaltyPlan { Active = true, KickDue = pk.Due, Dir = s.Dir, Lean = s.Lean, Lead = s.Lead, DiveAbs = start > abs ? start : abs, OffLine = s.OffLine };
                }
            }
            for (int t = 0; t < 2; t++)
            {
                ref var plan = ref _pk[t];
                if (plan.Active && !plan.Started && !plan.Kicked && abs >= plan.DiveAbs) StartCommit(ctx, t, abs, sub);
                if (!plan.Active) continue;
                // 결정론 리뷰 H1·M1: 계획 수명 — 판정 끝(Settled), 킥 뒤 경기 중단(정산 터치 없이 멈춤), 예약 킥 취소(실행 없이 Due 지남)면 지운다
                var cancelled = !plan.Kicked && abs > plan.KickDue && !(pk.Active && pk.Action.IsPenalty && pk.Due == plan.KickDue);
                if (plan.Settled || (plan.Kicked && !ctx.Flow.InPlay) || cancelled) plan = default;
            }
        }

        void StartCommit(MatchContext ctx, int team, long abs, int sub)
        {
            ref var plan = ref _pk[team];
            plan.Started = true;
            var slot = team * Squad.SlotsPerTeam;
            if (!ctx.IsSlotActive(slot)) { plan.Active = false; return; }
            var from = ctx.SlotState(slot).Position;
            plan.StartPos = from;
            var dt = ctx.BallParams.Dt;
            var e = SimEvent.Make(SimEventKind.PenaltyKeeper, team, ctx.PlayerAtSlot(slot));
            e.Substep = (sbyte)sub;
            e.A = plan.Dir;
            e.B = plan.OffLine ? 1 : 0;
            e.F0 = (plan.KickDue - abs) * dt;
            e.F1 = plan.Lean;
            ctx.SetPosition(ref e, new Vec3(from.X, from.Y, 0f));
            ctx.Emit(e);
            if (plan.Dir == 0 || IsBusy(team)) return;
            // 조기 다이빙: 골문 중앙에서 commitY 옆(중간 높이)을 목표로 몸을 날린다 — 판정은 킥 뒤 011 슛 감시가 이 계획으로
            var goal = ctx.OwnGoal(team);
            var target = new Vec2(goal.X, goal.Y + plan.Dir * _p.PkCommitY);
            var prof = Profile(ctx, team);
            var tt = (plan.KickDue - abs) * dt + PenaltyFlightSeconds(ctx, team);
            ref var dv = ref _dive[team];
            dv.Dives = true;
            dv.Direction = plan.Dir;
            dv.Height = DiveHeight.Mid;
            dv.ReactSubstep = abs;
            dv.ContactSubstep = abs + (long)DMath.Floor(tt / dt + 0.5f);
            dv.Velocity = DiveMotion.FlightVelocity(from, target, _p.ReachBodyHalf + _p.ReachR0 + prof.R0Bonus, prof.DiveSpeed, tt);
            dv.Phase = DivePhase.Flying;
            dv.LandSubstep = dv.ContactSubstep + _landSteps;
            dv.RecoverSubstep = dv.LandSubstep + _recoverSteps;
            ctx.Kinematics.BeginExternal(slot, dv.Velocity);
            var ds = SimEvent.Make(SimEventKind.DiveStart, team, ctx.PlayerAtSlot(slot));
            ds.Substep = (sbyte)sub;
            ds.A = plan.Dir * 10 + (int)DiveHeight.Mid;
            ds.F0 = tt;
            ds.F1 = -plan.Lead;
            ctx.SetPosition(ref ds, new Vec3(target.X, target.Y, 0.6f));
            ctx.Emit(ds);
        }

        /// <summary>074 FR-03: 008 Execute가 PK 킥을 실행한 서브스텝 — 그 예약의 계획에 '킥됨'과 킥 서브스텝을 기록한다.</summary>
        public void OnPenaltyKicked(MatchContext ctx, int kicker, long abs)
        {
            ref var plan = ref _pk[1 - Squad.TeamOfSlot(kicker)];
            if (!plan.Active || plan.Kicked || abs < plan.KickDue) return;
            plan.Kicked = true;
            plan.KickAbs = abs;
        }

        /// <summary>PK 비행시간 명목값: 페널티 지점 → 골라인 / PK 속도 (FR-02 약 0.53 s, 동작 목표용).</summary>
        static float PenaltyFlightSeconds(MatchContext ctx, int team)
        {
            var v = Actions.ShotExecution.SpeedFor(ctx.Actions.P, ctx.Actions.P.PenPower);
            return v > 1f ? Referee.RestartRules.PenaltySpotDist / v : 0.5f;
        }

        /// <summary>
        /// 074 FR-01: 슛 감시가 킥 직후 부른다. 이 팀 PK 계획이 있고 상대 슛이면 한 번만 '킥됨'으로 표시하고 조기 다이빙 방향을 돌려준다(0 = 서서 반응·계획 없음).
        /// startPos = 조기 다이빙 시작 위치, lead = 킥 기준 실제 조기 시각(킥 서브스텝 − 다이빙 시작).
        /// </summary>
        public int PenaltyCommit(MatchContext ctx, int team, out Vec2 startPos, out float lead)
        {
            startPos = Vec2.Zero;
            lead = 0f;
            ref var plan = ref _pk[team];
            if (!plan.Active || !plan.Kicked || plan.Judged || !plan.Started) return 0;
            if (ctx.LastKickSubstep != plan.KickAbs) return 0; // 그 PK 킥의 비행만 (리바운드 슛·뒤 오픈플레이 슛에 적용 안 함)
            plan.Judged = true;
            if (plan.Dir == 0) return 0;
            startPos = plan.StartPos;
            lead = (ctx.LastKickSubstep - plan.DiveAbs) * ctx.BallParams.Dt;
            if (lead < 0f) lead = 0f;
            return plan.Dir;
        }

        /// <summary>074 FR-01: 같은 쪽으로 이미 날고 있는 조기 다이빙을 예정 접촉점으로 다시 겨눈다 (손–공 순간이동 방지).</summary>
        internal void RetargetDive(MatchContext ctx, int team, Vec3 contact, long contactAbs, long abs)
        {
            ref var d = ref _dive[team];
            if (d.Phase != DivePhase.Flying) return;
            var slot = team * Squad.SlotsPerTeam;
            var prof = Profile(ctx, team);
            var tt = (contactAbs - abs) * ctx.BallParams.Dt;
            if (tt <= 0f) return;
            d.Velocity = DiveMotion.FlightVelocity(ctx.SlotState(slot).Position, new Vec2(contact.X, contact.Y), _p.ReachBodyHalf + _p.ReachR0 + prof.R0Bonus, prof.DiveSpeed, tt);
            d.ContactSubstep = contactAbs;
            d.LandSubstep = contactAbs + _landSteps;
            d.RecoverSubstep = d.LandSubstep + _recoverSteps;
            ctx.Kinematics.DriveExternal(slot, d.Velocity);
        }

        /// <summary>074 FR-03: 킥이 나간 PK 중 아직 재차기 판정을 받지 않은 쪽(막는 팀, 없으면 −1). 012가 PK 결과 순간 읽는다.</summary>
        public int PendingPenaltyTeam()
        {
            for (int t = 0; t < 2; t++)
                if (_pk[t].Active && _pk[t].Kicked && !_pk[t].Settled) return t;
            return -1;
        }

        /// <summary>074 FR-03: PK 결과가 나왔다 (골·GK 처리·아웃·골대) — 라인 이탈이었으면 true, 계획은 판정 끝으로 표시.</summary>
        public bool SettlePenalty(int team)
        {
            ref var plan = ref _pk[team];
            if (!plan.Active || !plan.Kicked || plan.Settled) return false;
            plan.Settled = true;
            return plan.OffLine;
        }

        internal void StartDive(MatchContext ctx, int team, in SaveJudgement j, long abs, int sub, long contact)
        {
            ref var d = ref _dive[team];
            d.Dives = j.Dives;
            d.Direction = j.Direction;
            d.Height = j.Height;
            d.ReactSubstep = abs;
            d.ContactSubstep = contact;
            if (!j.Dives) { d.Phase = DivePhase.None; return; }
            var slot = team * Squad.SlotsPerTeam;
            var prof = ProfileFor(ctx, team, _watch.Spread); // 073 FR-21: 판정과 같은 스프레드
            var from = ctx.SlotState(slot).Position;
            var cxy = new Vec2(j.ContactPoint.X, j.ContactPoint.Y);
            var tt = (contact - abs) * ctx.BallParams.Dt;
            d.Velocity = DiveMotion.FlightVelocity(from, cxy, _p.ReachBodyHalf + _p.ReachR0 + prof.R0Bonus, prof.DiveSpeed, tt); // R = r0 + v·τ: 몸은 r0(팔·기울임)만큼 덜 간다
            d.Phase = DivePhase.Flying;
            d.LandSubstep = contact + _landSteps;
            d.RecoverSubstep = d.LandSubstep + _recoverSteps;
            ctx.Kinematics.BeginExternal(slot, d.Velocity);

            var e = SimEvent.Make(SimEventKind.DiveStart, team, ctx.PlayerAtSlot(slot));
            e.Substep = (sbyte)sub;
            e.A = j.Direction * 10 + (int)j.Height;
            e.F0 = tt;
            e.F1 = j.ReactTime;
            ctx.SetPosition(ref e, j.ContactPoint);
            ctx.Emit(e);
        }

        void StepDive(MatchContext ctx, int team, long abs, int sub)
        {
            ref var d = ref _dive[team];
            if (d.Phase == DivePhase.None || d.Phase == DivePhase.Reacting) return;
            var slot = team * Squad.SlotsPerTeam;
            if (!ctx.IsSlotActive(slot) || !ctx.Kinematics.IsExternal(slot))
            {
                // 다이빙 중 퇴장·부상(슬롯 비움): 외부 구동을 풀어 걸어 나가기 조향이 먹게 한다 (결정론 리뷰 H1)
                if (ctx.Kinematics.IsActive(slot) && ctx.Kinematics.IsExternal(slot)) ctx.Kinematics.EndExternal(slot);
                d.Phase = DivePhase.None;
                return;
            }
            if (d.Phase == DivePhase.Flying && abs >= d.LandSubstep)
            {
                d.Phase = DivePhase.Down;
                ctx.Kinematics.DriveExternal(slot, Vec2.Zero);
            }
            if (d.Phase == DivePhase.Down && abs >= d.RecoverSubstep)
            {
                d.Phase = DivePhase.None;
                ctx.Kinematics.EndExternal(slot);
                var e = SimEvent.Make(SimEventKind.KeeperRecovered, team, ctx.PlayerAtSlot(slot));
                e.Substep = (sbyte)sub;
                e.A = (int)KeeperMotionKind.GetUp;
                ctx.SetPosition(ref e, ctx.SlotState(slot).Position);
                ctx.Emit(e);
            }
        }

        /// <summary>
        /// 빈 GK 슬롯 채우기 (specs/011 경계: GK 퇴장·부상 후 교체 불가): GK 슬롯이 비었고 나가는 GK가 운동 개체를 다 쓴 뒤,
        /// 자기 골에 가장 가까운 필드 선수(같으면 낮은 슬롯)를 GK 슬롯으로 옮긴다. GK 교체 선택(050 AI 감독)은 이 스펙 범위 밖 —
        /// 그 전에는 교체 여부와 상관없이 바로 옮긴다.
        /// </summary>
        public void EnsureKeeper(MatchContext ctx)
        {
            for (int team = 0; team < 2; team++)
            {
                var k = team * Squad.SlotsPerTeam;
                if (ctx.Squad.IsSlotFilled(k) || ctx.Flow.Walkers.IsWalking(k) || ctx.Kinematics.IsActive(k)) continue;
                var goal = ctx.OwnGoal(team);
                int best = -1;
                float bd = float.MaxValue;
                for (int s = k + 1; s < k + Squad.SlotsPerTeam; s++)
                {
                    if (!ctx.IsSlotControllable(s)) continue;
                    var d = (ctx.SlotState(s).Position - goal).LengthSq();
                    if (d < bd) { bd = d; best = s; }
                }
                if (best < 0) continue;
                ctx.Squad.MoveSlot(best, k);
                ctx.Kinematics.MoveSlot(best, k);
                ref var ball = ref ctx.Ball;
                if (ball.Phase == BallPhase.Controlled && ball.OwnerId == best) ball.OwnerId = k;
                if (ctx.LastTouchSlot == best) ctx.LastTouchSlot = k;
                if (ctx.LastKickSlot == best) ctx.LastKickSlot = k;
                _hasTarget[team] = false;
                _dive[team] = default;
                _plan[team] = KeeperPlan.Empty; // 073: 새 GK는 계획 없이 시작
                _pk[team] = default; // 074
                _hold[team] = default;
            }
        }

        /// <summary>슬롯 선수가 빠질 때(퇴장·부상, MatchFlow.RemoveFromPitch): GK면 다이빙·외부 구동을 끝낸다 (결정론 리뷰 H1).</summary>
        public void OnRemoved(MatchContext ctx, int slot)
        {
            if (!Squad.IsKeeperSlot(slot)) return;
            _dive[Squad.TeamOfSlot(slot)] = default;
            _plan[Squad.TeamOfSlot(slot)] = KeeperPlan.Empty; // 073
            _pk[Squad.TeamOfSlot(slot)] = default; // 074
            _hold[Squad.TeamOfSlot(slot)] = default;
            if (ctx.Kinematics.IsActive(slot) && ctx.Kinematics.IsExternal(slot)) ctx.Kinematics.EndExternal(slot);
        }

        public void Write(StateWriter w)
        {
            w.Write(_rng.SaveState());
            w.Write(_flightSeq);
            for (int t = 0; t < 2; t++)
            {
                w.Write((byte)Roles[t]);
                w.Write(_target[t]); w.Write(_hasTarget[t]); w.Write(_noise[t]); w.Write(_lastEval[t]); w.Write(_inRange[t]); w.Write(_possSeen[t]); w.Write((byte)_lastChoice[t]);
                _dive[t].Write(w);
                _plan[t].Write(w);
                _pk[t].Write(w);
                _hold[t].Write(w);
            }
            _watch.Write(w);
        }

        public void Read(StateReader r)
        {
            _rng.RestoreState(r.ReadRngState());
            _flightSeq = r.ReadInt();
            for (int t = 0; t < 2; t++)
            {
                Roles[t] = (KeeperRole)r.ReadByte();
                _target[t] = r.ReadVec2(); _hasTarget[t] = r.ReadBool(); _noise[t] = r.ReadVec2(); _lastEval[t] = r.ReadLong(); _inRange[t] = r.ReadBool(); _possSeen[t] = r.ReadInt(); _lastChoice[t] = (KeeperActionKind)r.ReadByte();
                _dive[t] = DiveMotion.Read(r);
                _plan[t] = KeeperPlan.Read(r);
                _pk[t] = PenaltyPlan.Read(r);
                _hold[t] = HoldClock.Read(r);
                _profId[t] = int.MinValue; // 매핑은 슬롯 선수에서 다시 계산 (순수 함수)
            }
            _watch.Read(r);
        }

        public void AddTo(ref StableHasher h)
        {
            var s = _rng.SaveState();
            h.Add(s.S0); h.Add(s.S1); h.Add(s.S2); h.Add(s.S3);
            h.Add(_flightSeq);
            for (int t = 0; t < 2; t++)
            {
                h.Add((byte)Roles[t]);
                h.Add(_target[t].X); h.Add(_target[t].Y); h.Add(_hasTarget[t]); h.Add(_noise[t].X); h.Add(_noise[t].Y); h.Add(_lastEval[t]); h.Add(_inRange[t]); h.Add(_possSeen[t]); h.Add((byte)_lastChoice[t]);
                _dive[t].AddTo(ref h);
                _plan[t].AddTo(ref h);
                _pk[t].AddTo(ref h);
                _hold[t].AddTo(ref h);
            }
            _watch.AddTo(ref h);
        }
    }
}
