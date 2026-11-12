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
        /// GK 결정 (FR-16·94, 단계 5): 후보 {위치 선정, 박스 안 느슨한 공 경합, 의도 지점 이동}의 기대효용을 010 선택 규칙으로 고른다.
        /// 공을 가진 GK·데드볼은 호출하지 않는다(대체 판단이 처리). 시나리오 의도는 효용 보너스로만 들어온다.
        /// </summary>
        public KeeperActionKind Decide(MatchContext ctx, int team, in KeeperIntent intent, out Vec2 point)
        {
            var slot = team * Squad.SlotsPerTeam;
            var prof = Profile(ctx, team);
            ref readonly var me = ref ctx.SlotState(slot);
            var pos = _hasTarget[team] ? _target[team] : me.Position;
            int n = 0;
            _cands[n++] = new KeeperCandidate { Kind = KeeperActionKind.Position, Point = pos, Utility = _p.ChoicePositionUtility };
            ref readonly var ball = ref ctx.Ball;
            if (ball.Phase != BallPhase.Controlled)
            {
                var target = new Vec2(ball.Position.X + ball.Velocity.X * _p.ChoiceContestLookahead, ball.Position.Y + ball.Velocity.Y * _p.ChoiceContestLookahead);
                if (Match.Stages.PhysicsStage.InOwnBox(ctx, team, new Vec3(target.X, target.Y, 0f)))
                {
                    var vmax = ctx.Kinematics.Profile(slot).MaxSpeed;
                    var tGk = (target - me.Position).Length() / DMath.Max(vmax, 0.1f) + prof.ReactTime;
                    var tOpp = float.MaxValue;
                    var opp = 1 - team;
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
            if (intent.Has && intent.Kind == KeeperActionKind.MoveTo)
                _cands[n++] = new KeeperCandidate { Kind = KeeperActionKind.MoveTo, Point = intent.Point, Utility = _p.ChoicePositionUtility - _p.ChoiceMoveCost * (intent.Point - pos).Length() };
            for (int i = 0; i < n; i++)
                if (_cands[i].Kind == _lastChoice[team] && _cands[i].Utility >= 0f) _cands[i].Utility += _p.ChoiceStickiness;
            var pick = _chooser.Choose(_cands, n, in intent, prof.DecisionsN, _rng);
            _lastChoice[team] = _cands[pick].Kind;
            point = _cands[pick].Point;
            return _cands[pick].Kind;
        }

        /// <summary>물리 단계 서브스텝 훅 (공 적분 뒤·잡기 판정 전): 다이빙 진행 → 슛 감시.</summary>
        public void OnSubstep(MatchContext ctx, long abs, int sub)
        {
            for (int t = 0; t < 2; t++) StepDive(ctx, t, abs, sub);
            _watch.Step(ctx, this, abs, sub);
        }

        /// <summary>공의 비행이 바뀜 (킥·굴절). 물리 단계가 킥 적용 때 부른다.</summary>
        public void NotifyFlight() => _watch.NotifyFlight();

        /// <summary>추적 중인 슛의 GK이거나 다이빙 중인 GK면 물리 단계 잡기에서 뺀다.</summary>
        public bool ExcludedFromClaim(int slot) => _watch.Tracks(slot) || IsBusySlot(slot);

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
            var prof = Profile(ctx, team);
            var from = ctx.SlotState(slot).Position;
            var cxy = new Vec2(j.ContactPoint.X, j.ContactPoint.Y);
            var tt = (contact - abs) * ctx.BallParams.Dt;
            d.Velocity = DiveMotion.FlightVelocity(from, cxy, _p.ReachBodyHalf + _p.ReachR0, prof.DiveSpeed, tt); // R = r0 + v·τ: 몸은 r0(팔·기울임)만큼 덜 간다
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
            }
        }

        /// <summary>슬롯 선수가 빠질 때(퇴장·부상, MatchFlow.RemoveFromPitch): GK면 다이빙·외부 구동을 끝낸다 (결정론 리뷰 H1).</summary>
        public void OnRemoved(MatchContext ctx, int slot)
        {
            if (!Squad.IsKeeperSlot(slot)) return;
            _dive[Squad.TeamOfSlot(slot)] = default;
            if (ctx.Kinematics.IsActive(slot) && ctx.Kinematics.IsExternal(slot)) ctx.Kinematics.EndExternal(slot);
        }

        public void Write(StateWriter w)
        {
            w.Write(_rng.SaveState());
            for (int t = 0; t < 2; t++)
            {
                w.Write((byte)Roles[t]);
                w.Write(_target[t]); w.Write(_hasTarget[t]); w.Write(_noise[t]); w.Write(_lastEval[t]); w.Write(_inRange[t]); w.Write(_possSeen[t]); w.Write((byte)_lastChoice[t]);
                _dive[t].Write(w);
            }
            _watch.Write(w);
        }

        public void Read(StateReader r)
        {
            _rng.RestoreState(r.ReadRngState());
            for (int t = 0; t < 2; t++)
            {
                Roles[t] = (KeeperRole)r.ReadByte();
                _target[t] = r.ReadVec2(); _hasTarget[t] = r.ReadBool(); _noise[t] = r.ReadVec2(); _lastEval[t] = r.ReadLong(); _inRange[t] = r.ReadBool(); _possSeen[t] = r.ReadInt(); _lastChoice[t] = (KeeperActionKind)r.ReadByte();
                _dive[t] = DiveMotion.Read(r);
                _profId[t] = int.MinValue; // 매핑은 슬롯 선수에서 다시 계산 (순수 함수)
            }
            _watch.Read(r);
        }

        public void AddTo(ref StableHasher h)
        {
            var s = _rng.SaveState();
            h.Add(s.S0); h.Add(s.S1); h.Add(s.S2); h.Add(s.S3);
            for (int t = 0; t < 2; t++)
            {
                h.Add((byte)Roles[t]);
                h.Add(_target[t].X); h.Add(_target[t].Y); h.Add(_hasTarget[t]); h.Add(_noise[t].X); h.Add(_noise[t].Y); h.Add(_lastEval[t]); h.Add(_inRange[t]); h.Add(_possSeen[t]); h.Add((byte)_lastChoice[t]);
                _dive[t].AddTo(ref h);
            }
            _watch.AddTo(ref h);
        }
    }
}
