using System;
using System.Collections.Generic;
using RoadToPro.Core.Hashing;
using RoadToPro.Core.Math;
using RoadToPro.Core.Serialization;
using RoadToPro.Core.Time;
using RoadToPro.MatchEngine.Ball;
using RoadToPro.MatchEngine.Match.Events;
using RoadToPro.MatchEngine.Match.Flow;
using RoadToPro.MatchEngine.Match.Input;
using RoadToPro.MatchEngine.Match.Pipeline;
using RoadToPro.MatchEngine.Match.Replay;
using RoadToPro.MatchEngine.Match.Result;
using RoadToPro.MatchEngine.Match.Stages;
using RoadToPro.MatchEngine.Match.Stages.StandIn;

namespace RoadToPro.MatchEngine.Match
{
    /// <summary>경기 실행 옵션.</summary>
    public sealed class MatchSimOptions
    {
        /// <summary>단계 배열을 만드는 함수 (테스트·대체 단계 교체). null이면 <see cref="MatchSim.DefaultStages"/>.</summary>
        public Func<IMatchStage[]> Stages;
        /// <summary>결정 대상 스케줄러 (010 자리). null이면 매 틱 전원.</summary>
        public Func<IDecisionScheduler> Scheduler;
        public CheckpointMode Checkpoints = CheckpointMode.None;
        public InputLog Inputs;
        public RefereeScript Referee;
        public int LogCapacity = SimEventLog.DefaultCapacity;
        /// <summary>결과에 이벤트 로그를 넣는다 (전체 정밀 기본 true).</summary>
        public bool KeepEventLog = true;
        /// <summary>승부차기·페널티 판정 (008·011 자리). null이면 대체 판정. 경기마다 새 인스턴스를 만든다.</summary>
        public Func<Flow.IPenaltyJudge> PenaltyJudge;
        /// <summary>팀 형태 주입 입력 (specs/009). null이면 대체 구현.</summary>
        public Tactics.ShapeInputProviders ShapeInputs;
        public Tactics.ITeamShapeTelemetry ShapeTelemetry;
        public Tactics.IShapeDiagnostics ShapeDiagnostics;
        /// <summary>시나리오 주입 입력 (specs/013). null이면 대체 구현.</summary>
        public Scenarios.ScenarioInputProviders ScenarioInputs;
        public Scenarios.IScenarioDiagnostics ScenarioDiagnostics;
        /// <summary>강제 선택 (FR-53, 검증·미리보기 전용).</summary>
        public Scenarios.ScenarioForceRequest ScenarioForce;
        /// <summary>판단 AI 진단 (specs/010 FR-60~62). 경기마다 새로 만드는 함수 — null이면 끔.</summary>
        public Func<UtilityAI.IDecisionDiagnostics> DecisionDiagnostics;

        public MatchSimOptions Clone() => new MatchSimOptions
        {
            Stages = Stages, Scheduler = Scheduler, Checkpoints = Checkpoints, Inputs = Inputs?.Clone(), Referee = Referee?.Clone(),
            LogCapacity = LogCapacity, KeepEventLog = KeepEventLog, PenaltyJudge = PenaltyJudge,
            ShapeInputs = ShapeInputs, ShapeTelemetry = ShapeTelemetry, ShapeDiagnostics = ShapeDiagnostics,
            ScenarioInputs = ScenarioInputs, ScenarioDiagnostics = ScenarioDiagnostics, ScenarioForce = ScenarioForce,
            DecisionDiagnostics = DecisionDiagnostics,
        };
    }

    /// <summary>
    /// 경기 하나 (specs/006). 결정 틱마다 FR-01 순서로 단계 0~8을 실행하고, 진행 판정(<see cref="MatchFlow"/>) 뒤 단계 9가 이벤트를 발행한다.
    /// 10초(100틱)마다 체크포인트 해시(이벤트 누적 해시 + 상태 해시)를 남기고, 모드에 따라 상태 스냅샷을 보관한다.
    /// 단계 예외·NaN은 그 경기만 실패 결과로 끝낸다 (FR-72). 단일 스레드 — 경기 여러 개는 각자 다른 스레드에서 돌려도 된다 (FR-71).
    /// </summary>
    public sealed class MatchSim
    {
        public const byte SnapshotTagCore = 1, SnapshotTagFlow = 2, SnapshotTagSquad = 3, SnapshotTagOfficials = 4,
            SnapshotTagKinematics = 5, SnapshotTagScheduler = 6, SnapshotTagKeepers = 7, SnapshotTagActions = 8, SnapshotTagStageBase = 10;

        readonly MatchSetup _setup;
        readonly MatchTuning _tuning;
        readonly MatchSimOptions _options;
        readonly MatchContext _ctx;
        readonly IMatchStage[] _stages;
        readonly IDecisionScheduler _scheduler;
        readonly SimEventLog _log;
        readonly CheckpointLog _checkpoints = new CheckpointLog();
        readonly List<MatchSnapshot> _snapshots = new List<MatchSnapshot>();
        readonly SortedSet<long> _keepTicks = new SortedSet<long>();
        readonly StateWriter _writer = new StateWriter(32 * 1024);
        readonly StateReader _reader = new StateReader();
        readonly int[] _sched = new int[Squad.Slots];
        long _nextTick;
        MatchResult _result;
        int _failedStage = -1;
        string _failure;
        int _currentStage = -1;

        /// <summary>단계별 시간 측정 (AC-21). null이면 호출 0.</summary>
        public IStageTimer Timer;

        public MatchSim(MatchSetup setup, MatchTuning tuning, MatchSimOptions options = null)
        {
            if (setup == null) throw new ArgumentNullException(nameof(setup));
            if (tuning == null) throw new ArgumentNullException(nameof(tuning));
            setup.Validate();
            _setup = setup.Clone(); // 호출자가 나중에 바꿔도 이 경기·포크는 영향 없음 (리뷰 R7)
            _tuning = tuning;
            _options = (options ?? new MatchSimOptions()).Clone(); // 입력 로그·심판 스크립트를 경기마다 따로 (리뷰 R2)
            _log = new SimEventLog(_options.LogCapacity);
            _ctx = new MatchContext(setup, tuning, _options.Inputs ?? new InputLog(), _options.Referee ?? new RefereeScript());
            _ctx.Log = _log;
            _ctx.View = new MatchView(_ctx);
            _stages = _options.Stages != null ? _options.Stages() : DefaultStages(_setup, _tuning, _options);
            if (_stages == null || _stages.Length != Pipeline.Stages.Count) throw new ArgumentException($"단계는 {Pipeline.Stages.Count}개");
            for (int i = 0; i < _stages.Length; i++)
                if (_stages[i] == null || (int)_stages[i].Id != i) throw new ArgumentException($"단계 {i} 자리에 {_stages[i]?.Id} — FR-01 순서와 다르다");
            _scheduler = _options.Scheduler != null ? _options.Scheduler()
                : (_stages[(int)StageId.ScenarioOrUtility] as IDecisionSchedulerProvider)?.Scheduler ?? new FixedPeriodScheduler(1); // 010: 결정 단계의 LOD
            if (_options.PenaltyJudge != null) _ctx.Flow.PenaltyJudge = _options.PenaltyJudge();
            _ctx.Flow.Start();
            _ctx.Recorder.EmitInitialFrame();
            if (_options.Checkpoints == CheckpointMode.All) _snapshots.Add(Capture());
        }

        /// <summary>설정에 맞는 기본 단계: 두 팀에 전술이 있으면 009 단계, 없으면 006 대체 단계.</summary>
        public static IMatchStage[] DefaultStages(MatchSetup setup, MatchTuning tuning, MatchSimOptions options)
        {
            if (setup.Home.Tactics == null) return DefaultStages();
            if (tuning.Shape == null) throw new MatchSetupException("전술을 쓰려면 MatchTuning.Shape가 필요하다");
            var data = tuning.Shape;
            var p = Tactics.ShapeParams.Create(data.Tuning, setup.Pitch);
            var catalog = data.Formations;
            var f0 = catalog.IndexOf(setup.Home.Tactics.FormationId);
            var f1 = catalog.IndexOf(setup.Away.Tactics.FormationId);
            if (f0 < 0 || f1 < 0) throw new MatchSetupException($"없는 포메이션: {setup.Home.Tactics.FormationId} / {setup.Away.Tactics.FormationId}");
            var inputs = options?.ShapeInputs ?? new Tactics.ShapeInputProviders();
            var stages = DefaultStages();
            stages[(int)StageId.TeamTactics] = new TacticsStage(setup, catalog.Count, f0, f1);
            stages[(int)StageId.Phase] = new PhaseStage(p);
            if (tuning.Scenario == null)
            {
                stages[(int)StageId.Shape] = new ShapeStage(p, catalog, inputs, setup, f0, f1, options?.ShapeTelemetry, options?.ShapeDiagnostics);
                stages[(int)StageId.ScenarioOrUtility] = new UtilityDecisionStage(setup, tuning) { Diagnostics = options?.DecisionDiagnostics?.Invoke() }; // 010
                return stages;
            }

            // 013: 경기마다 런타임을 만들고 형태의 배역 입력으로 끼운다 (호출자의 입력 묶음은 고치지 않는다)
            var runtime = new Scenarios.ScenarioRuntime(tuning.Scenario, setup, options?.ScenarioInputs, options?.ScenarioDiagnostics, options?.ScenarioForce);
            var shapeInputs = new Tactics.ShapeInputProviders { Traits = inputs.Traits, Roles = inputs.Roles, PitchControl = inputs.PitchControl, Signals = inputs.Signals, Cast = runtime };
            var shape = new ShapeStage(p, catalog, shapeInputs, setup, f0, f1, options?.ShapeTelemetry, options?.ShapeDiagnostics);
            runtime.BindRoles(new Scenarios.FormationRoleSource(shape));
            stages[(int)StageId.Shape] = new ScenarioShapeStage(shape, runtime);
            stages[(int)StageId.ScenarioOrUtility] = new ScenarioStage(runtime, new UtilityDecisionStage(setup, tuning, runtime) { Diagnostics = options?.DecisionDiagnostics?.Invoke() }); // 013 선택 + 010 판단
            return stages;
        }

        public static IMatchStage[] DefaultStages() => new IMatchStage[]
        {
            new NoOpStage(StageId.TeamTactics),
            new NoOpStage(StageId.Phase),
            new HoldShapeStage(),
            new NoOpStage(StageId.RoleAdjust),
            new NoOpStage(StageId.Tendency),
            new SimpleDecisionStage(),
            new SimpleSteeringStage(),
            new PhysicsStage(),
            new SimpleRefereeStage(),
            new PublishStage(),
        };

        // ── 조회 ──

        public MatchSetup Setup => _setup;
        public MatchTuning Tuning => _tuning;
        public MatchSimOptions Options => _options;
        public IMatchView View => _ctx.View;
        /// <summary>단계·검증 코드용 상태 (테스트는 읽기 위주로 쓴다).</summary>
        public MatchContext Context => _ctx;
        public SimEventLog Log => _log;
        public IReadOnlyList<Checkpoint> Checkpoints => _checkpoints.Items;
        public IReadOnlyList<MatchSnapshot> Snapshots => _snapshots;
        public IMatchStage Stage(StageId id) => _stages[(int)id];
        /// <summary>다음에 처리할 틱 (= 처리한 틱 수).</summary>
        public long NextTick => _nextTick;
        public bool IsFinished => _ctx.Flow.IsFinished;
        public MatchResult Result => _result;
        public ReplayRecorder Recorder => _ctx.Recorder;

        public void Subscribe(ISimEventSubscriber s) => _ctx.Publisher.Subscribe(s);

        // ── 입력 ──

        /// <summary>
        /// 교체 요청 (팀 AI·025·테스트). 입력 로그에 (다음 틱, 나감, 들어옴)으로 기록되고 그 틱 시작에 대기열에 들어가
        /// 다음 데드볼·휴식에서 처리된다 — 재구성·재시뮬이 같은 요청을 다시 낸다 (리뷰 B2). team은 확인용.
        /// </summary>
        public void RequestSubstitution(int team, int outId, int inId, bool concussion = false) =>
            AddInput(new ExternalInput(_nextTick, outId, concussion ? InputKind.SubstituteConcussion : InputKind.Substitute, inId));

        /// <summary>팀 지시 변경 요청 (009 FR-71): 다음 결정 틱에 반영된다.</summary>
        public void RequestInstruction(int team, Tactics.InstructionId id, float value) =>
            AddInput(new ExternalInput(_nextTick, team * 100 + (int)id, InputKind.TeamInstruction, value));

        /// <summary>포메이션 변경 요청 (009 FR-71). index는 카탈로그 번호.</summary>
        public void RequestFormation(int team, int index) =>
            AddInput(new ExternalInput(_nextTick, team, InputKind.TeamFormation, index));

        /// <summary>외부 입력 추가 (FR-62). 이미 지난 틱은 거부.</summary>
        public void AddInput(in ExternalInput input)
        {
            if (input.Tick < _nextTick) throw new ArgumentException($"이미 지난 틱 {input.Tick} (다음 틱 {_nextTick})");
            _ctx.Inputs.Add(input);
        }

        /// <summary>틱 tick 직전 상태 스냅샷을 보관한다 (KeyMomentsOnly 모드, FR-55).</summary>
        public void KeepSnapshotAt(long tick) => _keepTicks.Add(tick);

        /// <summary>검증·테스트 전용: 공 상태를 강제로 놓는다 (AC-09 고정 배치). 경기 코드는 부르지 않는다.</summary>
        public void ForceBallState(in BallState s) => _ctx.Ball = s;

        // ── 실행 ──

        /// <summary>한 틱 진행 (FR-70). 끝났으면 false.</summary>
        public bool StepTick()
        {
            if (_ctx.Flow.IsFinished) return false;
            var tick = _nextTick;
            _ctx.Tick = tick;
            var board = _ctx.Board;
            try
            {
                board.Clear();
                board.Leave();
                _currentStage = -1;
                _ctx.Flow.BeginTick();
                ApplySubstitutionInputs(tick);
                SelectDecisions(tick);
                for (int s = 0; s < Pipeline.Stages.Count - 1; s++) RunStage(s);
                board.Leave();
                _currentStage = Pipeline.Stages.Count; // 진행 판정
                _ctx.Flow.EndTick();
                CheckFinite();
                RunStage(Pipeline.Stages.Count - 1);
                board.Leave();
            }
            catch (Exception ex)
            {
                Fail(ex);
            }
            _nextTick = tick + 1;
            if (_checkpoints.IsDue(_nextTick))
            {
                _checkpoints.Record(_nextTick, _log.Hash, StateHash());
                if (_options.Checkpoints == CheckpointMode.All) _snapshots.Add(Capture());
            }
            if (_keepTicks.Count > 0 && _keepTicks.Contains(_nextTick) && _options.Checkpoints != CheckpointMode.All) _snapshots.Add(Capture());
            if (!_ctx.Flow.IsFinished && _nextTick >= SimClock.MaxMatchTicks + 2 * SimClock.TicksPerMinute)
            {
                _ctx.Tick = _nextTick - 1;
                Fail(new InvalidOperationException("경기 시간이 상한을 넘었다 (종료 판정 누락)"));
            }
            if (_ctx.Flow.IsFinished && _result == null) BuildResult();
            return true;
        }

        public void StepTicks(long n)
        {
            for (long i = 0; i < n && StepTick(); i++) { }
        }

        /// <summary>tick을 처리하기 직전까지 (NextTick == tick).</summary>
        public void RunUntilTick(long tick)
        {
            while (_nextTick < tick && StepTick()) { }
        }

        public MatchResult RunToEnd()
        {
            while (StepTick()) { }
            return _result;
        }

        void RunStage(int s)
        {
            _currentStage = s;
            _ctx.Board.Enter((StageId)s);
            var timer = Timer;
            if (timer != null)
            {
                timer.Begin((StageId)s);
                _stages[s].Execute(_ctx);
                timer.End((StageId)s);
            }
            else _stages[s].Execute(_ctx);
        }

        void ApplySubstitutionInputs(long tick)
        {
            var inputs = _ctx.Inputs;
            for (int i = inputs.FirstAtOrAfter(tick); i < inputs.Count && inputs[i].Tick == tick; i++)
            {
                var e = inputs[i];
                if (e.Kind != InputKind.Substitute && e.Kind != InputKind.SubstituteConcussion) continue;
                var inId = (int)e.Value;
                var sq = _ctx.Squad;
                var team = sq.Has(e.PlayerId) ? sq.TeamOf(e.PlayerId) : sq.Has(inId) ? sq.TeamOf(inId) : 0;
                _ctx.Flow.RequestSubstitution(new SubstitutionRequest { Team = team, OutId = e.PlayerId, InId = inId, Concussion = e.Kind == InputKind.SubstituteConcussion });
            }
        }

        void SelectDecisions(long tick)
        {
            var n = _scheduler.Select(tick, _ctx.View, _sched);
            // 오름차순 + 중복 제거 (삽입 정렬, FR-05)
            for (int i = 1; i < n; i++)
            {
                var x = _sched[i];
                int j = i - 1;
                while (j >= 0 && _sched[j] > x) { _sched[j + 1] = _sched[j]; j--; }
                _sched[j + 1] = x;
            }
            int m = 0;
            for (int i = 0; i < n; i++)
            {
                if ((uint)_sched[i] >= Squad.Slots) continue;
                if (m > 0 && _ctx.DecisionList[m - 1] == _sched[i]) continue;
                _ctx.DecisionList[m++] = _sched[i];
            }
            _ctx.DecisionCount = m;
        }

        void CheckFinite()
        {
            ref readonly var b = ref _ctx.Ball;
            if (!FloatGuard.IsFinite(b.Position.X) || !FloatGuard.IsFinite(b.Position.Y) || !FloatGuard.IsFinite(b.Position.Z)
                || !FloatGuard.IsFinite(b.Velocity.X) || !FloatGuard.IsFinite(b.Velocity.Y) || !FloatGuard.IsFinite(b.Velocity.Z))
                throw new NumericException($"공 상태 NaN/무한대 (틱 {_ctx.Tick})");
        }

        void Fail(Exception ex)
        {
            _failedStage = _currentStage;
            _failure = $"{ex.GetType().Name}: {ex.Message}";
            _ctx.Board.Leave();
            try
            {
                _ctx.Flow.Fail(_currentStage, _failure);
                _ctx.Publisher.Flush(_log, _ctx.View);
            }
            catch (Exception)
            {
                // 대기열 넘침 등: 대기 이벤트를 버리고 실패 이벤트 1건만 남긴다 (리뷰 R4)
                _ctx.Publisher.Clear();
                _ctx.Flow.ForceFailed(_currentStage);
                try { _ctx.Publisher.Flush(_log, _ctx.View); } catch (Exception) { _ctx.Publisher.Clear(); }
            }
        }

        // ── 해시 · 스냅샷 ──

        /// <summary>전체 시뮬 상태 해시 (체크포인트 StateHash, FR-34).</summary>
        public ulong StateHash()
        {
            var h = StableHasher.Create();
            h.Add(_nextTick);
            h.Add(_log.LastSeq);
            _ctx.AddStateTo(ref h);
            _scheduler.AddTo(ref h);
            _ctx.Keepers.AddTo(ref h);
            _ctx.Actions.AddTo(ref h);
            for (int i = 0; i < _stages.Length; i++) _stages[i].AddTo(ref h);
            return h.Value;
        }

        /// <summary>지금(NextTick 직전) 상태 스냅샷 (FR-60).</summary>
        public MatchSnapshot Capture()
        {
            var w = _writer;
            w.Reset();
            w.Write(MatchSnapshot.FormatVersion);
            w.Write(_nextTick);
            w.Write(_log.LastSeq);
            w.BeginSection(SnapshotTagCore); _ctx.WriteCore(w); w.EndSection();
            w.BeginSection(SnapshotTagFlow); _ctx.Flow.Write(w); w.EndSection();
            w.BeginSection(SnapshotTagSquad); _ctx.Squad.Write(w); w.EndSection();
            w.BeginSection(SnapshotTagOfficials); _ctx.Officials.Write(w); w.EndSection();
            w.BeginSection(SnapshotTagKinematics); _ctx.Kinematics.WriteState(w); w.EndSection();
            w.BeginSection(SnapshotTagScheduler); _scheduler.WriteState(w); w.EndSection();
            w.BeginSection(SnapshotTagKeepers); _ctx.Keepers.Write(w); w.EndSection();
            w.BeginSection(SnapshotTagActions); _ctx.Actions.Write(w); w.EndSection();
            for (int i = 0; i < _stages.Length; i++)
            {
                w.BeginSection((byte)(SnapshotTagStageBase + i));
                _stages[i].WriteState(w);
                w.EndSection();
            }
            return new MatchSnapshot(_nextTick, _log.LastSeq, _log.Hash, StateHash(), _checkpoints.Items.Count, w.ToArray());
        }

        /// <summary>
        /// 스냅샷에서 복원 (FR-60·61). 이벤트 로그·체크포인트는 원본(source) 앞부분을 복사한다 — 복원 뒤 이어서 돌리면
        /// 중단 없는 실행과 비트 단위로 같다 (AC-14). 리플레이 링은 비운다.
        /// </summary>
        public void Restore(MatchSnapshot snap, SimEventLog sourceLog, IReadOnlyList<Checkpoint> sourceCheckpoints)
        {
            if (snap == null) throw new ArgumentNullException(nameof(snap));
            if (sourceLog == null) throw new ArgumentNullException(nameof(sourceLog));
            if (ReferenceEquals(sourceLog, _log)) throw new ArgumentException("자기 로그로 되감기는 지원하지 않는다 — Fork()에 복원한다 (리뷰 R3)");
            if (sourceLog.HashOfPrefix(snap.Seq) != snap.EventHash) throw new InvalidOperationException("원본 로그 앞부분 해시가 스냅샷과 다르다 (다른 경기 로그)");
            var r = _reader;
            r.Reset(snap.Data, snap.Data.Length);
            var ver = r.ReadByte();
            if (ver != MatchSnapshot.FormatVersion) throw new FormatException($"스냅샷 형식 버전 {ver} (지원 {MatchSnapshot.FormatVersion})");
            var tick = r.ReadLong();
            var seq = r.ReadLong();
            r.BeginSection(SnapshotTagCore); _ctx.ReadCore(r); r.EndSection();
            r.BeginSection(SnapshotTagFlow); _ctx.Flow.Read(r); r.EndSection();
            r.BeginSection(SnapshotTagSquad); _ctx.Squad.Read(r); r.EndSection();
            r.BeginSection(SnapshotTagOfficials); _ctx.Officials.Read(r); r.EndSection();
            r.BeginSection(SnapshotTagKinematics); _ctx.Kinematics.ReadState(r); r.EndSection();
            r.BeginSection(SnapshotTagScheduler); _scheduler.ReadState(r); r.EndSection();
            r.BeginSection(SnapshotTagKeepers); _ctx.Keepers.Read(r); r.EndSection();
            r.BeginSection(SnapshotTagActions); _ctx.Actions.Read(r); r.EndSection();
            for (int i = 0; i < _stages.Length; i++)
            {
                r.BeginSection((byte)(SnapshotTagStageBase + i));
                _stages[i].ReadState(r);
                r.EndSection();
            }
            if (!r.AtEnd) throw new FormatException("스냅샷 뒤에 남은 데이터");
            _nextTick = tick;
            _log.CopyPrefix(sourceLog, seq);
            _checkpoints.Clear();
            if (sourceCheckpoints != null)
                for (int i = 0; i < sourceCheckpoints.Count && sourceCheckpoints[i].Tick <= tick; i++)
                {
                    var c = sourceCheckpoints[i];
                    _checkpoints.Record(c.Tick, c.EventHash, c.StateHash);
                }
            _snapshots.Clear();
            if (_options.Checkpoints == CheckpointMode.All) _snapshots.Add(snap);
            _ctx.Publisher.Clear();
            _ctx.Board.Clear();
            _ctx.Recorder.ClearRing();
            if (tick == 0) _ctx.Recorder.EmitInitialFrame();
            _result = null;
            _failedStage = -1;
            _failure = null;
            if (StateHash() != snap.StateHash) throw new InvalidOperationException("복원 직후 상태 해시가 스냅샷과 다르다 (직렬화 누락)");
        }

        /// <summary>tick 이하 가장 가까운 보관 스냅샷 (없으면 null).</summary>
        public MatchSnapshot NearestSnapshot(long tick)
        {
            MatchSnapshot best = null;
            foreach (var s in _snapshots)
                if (s.Tick <= tick && (best == null || s.Tick > best.Tick)) best = s;
            return best;
        }

        /// <summary>같은 설정·입력으로 새 시뮬을 만든다 (재구성·재시뮬용, 보관 모드 None).</summary>
        public MatchSim Fork(InputLog inputs = null, CheckpointMode mode = CheckpointMode.None)
        {
            var o = _options.Clone();
            o.Checkpoints = mode;
            o.Inputs = inputs ?? _ctx.Inputs.Clone(); // AddInput·교체 요청까지 넘긴다 (리뷰 B1)
            o.ShapeTelemetry = null; // 재구성이 지나간 틱을 다시 보고하면 집계가 중복된다 (009 리뷰 R1)
            o.ShapeDiagnostics = null;
            o.ScenarioDiagnostics = null;
            o.DecisionDiagnostics = null; // 010: 재구성 중복 보고 방지
            var sim = new MatchSim(_setup, _tuning, o);
            for (int i = 0; i < _stages.Length; i++)
                if (ReferenceEquals(sim._stages[i], _stages[i])) throw new InvalidOperationException($"단계 팩토리가 같은 인스턴스를 돌려준다 (단계 {i}) — 경기마다 새로 만들어야 한다 (리뷰 R1)");
            if (ReferenceEquals(sim._scheduler, _scheduler)) throw new InvalidOperationException("스케줄러 팩토리가 같은 인스턴스를 돌려준다 (리뷰 R1)");
            return sim;
        }

        // ── 재구성 · 클립 (FR-52·53) ──

        /// <summary>
        /// 구간 [fromTick, toTick)의 30 Hz 프레임을 가장 가까운 이전 체크포인트에서 다시 돌려 만든다. 0 미만·경기 끝을 넘는 구간은 자른다.
        /// 프레임 k(시각 k/30 s)는 3·fromTick ≤ k &lt; 3·toTick. 구간 안 이벤트를 붙인다. 원본 실행 상태는 바꾸지 않는다.
        /// </summary>
        public ReplayClip ExtractClip(long fromTick, long toTick)
        {
            if (fromTick < 0) fromTick = 0;
            var end = _ctx.Flow.IsFinished ? _ctx.Flow.EndTickValue + 1 : _nextTick;
            if (toTick > end) toTick = end;
            if (toTick <= fromTick) throw new ArgumentException($"빈 구간 [{fromTick}, {toTick})");
            var start = fromTick == 0 ? 0 : fromTick - 1;
            var snap = NearestSnapshot(start);
            var sim = Fork();
            if (snap != null && snap.Tick > 0) sim.Restore(snap, _log, _checkpoints.Items);
            var sink = new ClipSink(3 * fromTick, 3 * toTick);
            sim.Recorder.RingEnabled = false;
            sim.Recorder.Sink = sink;
            if (fromTick == 0 && (snap == null || snap.Tick == 0)) sink.OnFrame(sim.Recorder.InitialFrame);
            sim.RunUntilTick(toTick);
            return new ReplayClip(fromTick, toTick, sink.Frames.ToArray(), _log.Range(fromTick, toTick - 1));
        }

        sealed class ClipSink : IReplayFrameSink
        {
            readonly long _from, _to;
            public readonly List<ReplayFrame> Frames = new List<ReplayFrame>();
            public ClipSink(long from, long to) { _from = from; _to = to; }
            public void OnFrame(ReplayFrame f)
            {
                if (f.Index >= _from && f.Index < _to) Frames.Add(f.Clone());
            }
        }

        // ── 키 모먼트 재시뮬 (FR-62·63) ──

        /// <summary>
        /// input.Tick부터 입력을 넣고 경기 끝까지 다시 계산한 새 시뮬. 그 이전 이벤트·체크포인트는 원본과 같다 (AC-15).
        /// 보관 스냅샷이 있으면 가장 가까운 것에서, 없으면 처음부터 (결정론이라 결과는 같다).
        /// </summary>
        public MatchSim Resimulate(in ExternalInput input)
        {
            var inputs = _ctx.Inputs.Clone();
            inputs.Add(input);
            var sim = Fork(inputs);
            var snap = NearestSnapshot(input.Tick);
            if (snap != null && snap.Tick > 0) sim.Restore(snap, _log, _checkpoints.Items);
            sim.RunToEnd();
            return sim;
        }

        // ── 결과 ──

        void BuildResult()
        {
            var f = _ctx.Flow;
            var r = new MatchResult
            {
                Seed = _setup.Seed,
                TuningHash = _tuning.Hash,
                Reason = f.EndReason,
                EndTick = f.EndTickValue,
                ExtraTimePlayed = f.Period >= 2,
                ShootoutPlayed = f.ShootoutPlayed,
                EventLogHash = _log.Hash,
                Checkpoints = ToArray(_checkpoints.Items),
                FinalStateHash = StateHash(),
                Inputs = _ctx.Inputs.ToArray(),
                EventLog = _options.KeepEventLog ? _log.ToArray() : null,
                ScenarioIds = (string[])_tuning.Scenario?.Library.Ids.Clone(),
            };
            for (int t = 0; t < 2; t++)
            {
                r.RegularGoals[t] = f.RegularGoals(t);
                r.ExtraGoals[t] = f.ExtraGoals(t);
                r.ShootoutGoals[t] = f.ShootoutGoals(t);
            }
            for (int p = 0; p < 4; p++) r.PeriodLengths[p] = f.PeriodLength(p);
            if (f.EndReason == EndReason.Failed)
            {
                r.FailedStage = _failedStage;
                r.FailedTick = f.EndTickValue;
                r.FailureMessage = _failure;
            }
            var goals = new List<GoalRecord>();
            var cards = new List<CardRecord>();
            var subs = new List<SubstitutionRecord>();
            var inj = new List<InjuryRecord>();
            for (int i = 0; i < _log.Count; i++)
            {
                ref readonly var e = ref _log[i];
                switch (e.Kind)
                {
                    case SimEventKind.Goal:
                        goals.Add(new GoalRecord { Seq = e.Seq, Tick = e.Tick, Time = e.Time, Team = e.Team, ScorerId = e.PlayerId, OwnGoal = e.A == 1 });
                        break;
                    case SimEventKind.VarOverturn:
                        for (int g = goals.Count - 1; g >= 0; g--)
                            if (goals[g].Team == e.Team) { goals.RemoveAt(g); break; }
                        break;
                    case SimEventKind.Card:
                        cards.Add(new CardRecord { Tick = e.Tick, Time = e.Time, Team = e.Team, PlayerId = e.PlayerId, Red = e.A == EventCodes.CardRed });
                        break;
                    case SimEventKind.Substitution:
                        subs.Add(new SubstitutionRecord { Tick = e.Tick, Time = e.Time, Team = e.Team, InId = e.PlayerId, OutId = e.OtherId, Concussion = e.B == EventCodes.SubConcussion });
                        break;
                    case SimEventKind.Injury:
                        inj.Add(new InjuryRecord { Tick = e.Tick, Time = e.Time, Team = e.Team, PlayerId = e.PlayerId });
                        break;
                }
            }
            r.Goals = goals.ToArray();
            r.Cards = cards.ToArray();
            r.Substitutions = subs.ToArray();
            r.Injuries = inj.ToArray();
            var sq = _ctx.Squad;
            var lines = new PlayerLine[sq.PlayerCount];
            for (int i = 0; i < lines.Length; i++)
            {
                var id = sq.IdAt(i);
                var st = sq.StatusOf(id);
                lines[i] = new PlayerLine
                {
                    Id = id,
                    Team = sq.TeamOf(id),
                    FinalStatus = st,
                    PlayedTicks = sq.PlayedTicks(id, f.EndTickValue + 1),
                    Motion = st == PlayerStatus.OnPitch ? _ctx.Kinematics.Stats(sq.SlotOf(id)) : sq.StoredStats(id),
                };
            }
            r.Players = lines;
            _result = r;
        }

        static Checkpoint[] ToArray(IReadOnlyList<Checkpoint> l)
        {
            var a = new Checkpoint[l.Count];
            for (int i = 0; i < a.Length; i++) a[i] = l[i];
            return a;
        }
    }
}
