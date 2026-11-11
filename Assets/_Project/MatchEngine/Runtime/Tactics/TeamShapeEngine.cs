using RoadToPro.Core.Hashing;
using RoadToPro.Core.Math;
using RoadToPro.Core.Random;
using RoadToPro.Core.Serialization;
using RoadToPro.MatchEngine.Match.Flow;

namespace RoadToPro.MatchEngine.Tactics
{
    /// <summary>한 팀의 결정 틱 입력. 좌표는 팀 관점 정규화, 배열 인덱스는 팀 내 슬롯(0 = GK).</summary>
    public sealed class TeamFrame
    {
        public const int N = FormationDefinition.SlotCount;

        public long Tick;
        public int Period;
        public Vec2 Ball;
        public TeamPhase Phase;
        public TeamPhase Block;
        public long TransitionTicks;
        public float OppOffsideLine = 0.5f;
        public RestartKind Restart;
        public bool RestartAttacking;
        public Vec2 RestartSpot;
        public int RestartTaker = -1;
        public bool LineSyncAdvance;
        /// <summary>GK 목표 (specs/011 FR-93, 팀 관점 정규화). 없으면 009 대체식.</summary>
        public bool HasKeeperTarget;
        public Vec2 KeeperTarget;

        public readonly Vec2[] Own = new Vec2[N], OwnVel = new Vec2[N], Opp = new Vec2[N], OppVel = new Vec2[N];
        public readonly bool[] OwnActive = new bool[N], OppActive = new bool[N], Cast = new bool[N];
        public readonly float[] Fatigue = new float[N];
    }

    /// <summary>한 팀 결과 (FR-03). x 값은 팀 관점 정규화.</summary>
    public struct TeamShapeOutput
    {
        public TeamPhase Phase;
        public ShapeProfile Profile;
        public float DefLineX, OffsideLineX;
        public int FirstResponder, Press1, Press2, Press3, PressCount;
        public bool PressActive;
    }

    /// <summary>
    /// 한 팀의 형태 계산 (FR-30~52·90~92). 입력을 받아 국면 프로필 → 블록 → 슬롯 목표 → 라인 → 재매칭 → 오차·제약 → 존·압박 순으로 계산한다.
    /// 단계(ShapeStage)와 테스트가 같은 경로를 쓴다. 생성 후 틱 경로 할당 0.
    /// </summary>
    public sealed class TeamShapeEngine
    {
        const int N = FormationDefinition.SlotCount;
        const float Dt = 1f / Core.Time.SimClock.TicksPerSecond;
        const int HistLen = 9; // line.syncDelayMax 상한 0.8 s + 현재 틱

        readonly ShapeParams _p;
        readonly FormationCatalog _catalog;
        readonly ShapeInputProviders _in;
        readonly int _team, _slotBase, _rematchOffset;

        FormationDefinition _f;
        ShapeProfile _profile;
        int _formationIndex;
        bool _started;
        int _period = -1;
        int _fieldCount = -1;
        bool _rematchDue = true;
        bool _castOverflow;
        bool _lineHistDirty = true;

        readonly AnchorBlend _blend = new AnchorBlend();
        readonly DefensiveLine _line;
        readonly PressPlanner _press;
        readonly SlotMatcher _matcher = new SlotMatcher();
        readonly SetPieceLayout _setPiece;

        readonly Vec2[] _slotTargets = new Vec2[N], _targets = new Vec2[N], _extra = new Vec2[N], _pc = new Vec2[N], _slotErr = new Vec2[N];
        readonly bool[] _slotActive = new bool[N], _hasTarget = new bool[N], _wasCast = new bool[N], _pressing = new bool[N], _isLineSlot = new bool[N], _candidate = new bool[N], _noSkip = new bool[N], _layoutActive = new bool[N];
        readonly float[] _lineErr = new float[N], _zoneWeight = new float[N], _cost = new float[SlotMatcher.MaxSize * SlotMatcher.MaxSize], _hist = new float[HistLen];
        readonly int[] _slotOf = new int[N], _rows = new int[N], _cols = new int[N], _rowToCol = new int[N];
        readonly int[] _delayTicks = new int[N];
        readonly float[] _sortX = new float[N];
        TeamShapeOutput _output;

        public TeamShapeEngine(ShapeParams p, FormationCatalog catalog, ShapeInputProviders inputs, int team, int formationIndex)
        {
            _p = p;
            _catalog = catalog;
            _in = inputs;
            _team = team;
            _slotBase = team * N;
            _rematchOffset = team == 0 ? 0 : 2 % p.RematchTicks; // 주기가 2틱 이하여도 팀 1이 재매칭한다
            _line = new DefensiveLine(p);
            _press = new PressPlanner(p);
            _setPiece = new SetPieceLayout(p);
            _formationIndex = formationIndex;
            _f = catalog[formationIndex];
            for (int i = 0; i < N; i++) { _slotOf[i] = i; _slotActive[i] = true; _zoneWeight[i] = _f.Slots[i].ZoneWeight; }
            RefreshLineFlags();
        }

        // ── 조회 (진단·테스트, 읽기 전용) ──
        public ref readonly TeamShapeOutput Output => ref _output;
        public Vec2 Target(int i) => _targets[i];
        public bool HasTarget(int i) => _hasTarget[i];
        public Vec2 SlotTarget(int slot) => _slotTargets[slot];
        public int FormationSlotOf(int i) => _slotOf[i];
        public Vec2 SlotError(int i) => _slotErr[i];
        public float LineError(int i) => _lineErr[i];
        public float LineX => _line.X;
        public bool SlotActive(int slot) => _slotActive[slot];
        public bool CastOverflow => _castOverflow;
        public FormationDefinition Formation => _f;
        public bool IsLinePlayer(int i) => _slotOf[i] > 0 && _isLineSlot[_slotOf[i]];

        /// <summary>전술 변경 (FR-71): 다음 계산부터 앵커를 보간해 바꾼다.</summary>
        public void SetFormation(int formationIndex)
        {
            if (formationIndex == _formationIndex) return;
            _formationIndex = formationIndex;
            _f = _catalog[formationIndex];
            for (int i = 0; i < N; i++) _zoneWeight[i] = _f.Slots[i].ZoneWeight;
            _fieldCount = -1;
            RefreshLineFlags();
            _blend.Begin(_p.BlendMax);
        }

        void RefreshLineFlags()
        {
            for (int i = 0; i < N; i++) _isLineSlot[i] = _f.Slots[i].IsLine;
        }

        /// <summary>
        /// 한 틱 계산. errorsEnabled=false면 오차를 0으로 둔다 (점대칭·형태 검증 전용, plan 해석 7).
        /// </summary>
        public void Step(TeamFrame f, in TeamInstructions ins, int[] manMark, in PressSignals signals, SeedRng rng, ITeamShapeTelemetry telemetry = null, bool errorsEnabled = true)
        {
            UpdateRoster(f);
            var teamworkAvg = TeamAverage(f, false);

            var profile = ResolveProfile(f, in ins, out bool counterPress);
            ShapeSolver.Resolve(_p, profile, f.Ball, in ins, out var targetBlock);

            if (!_started || f.Period != _period)
            {
                _blend.Snap(_f, profile, in targetBlock);
                _line.Reset();
                _lineHistDirty = true;
                _started = true;
                _period = f.Period;
                _profile = profile;
            }
            else if (profile != _profile)
            {
                var a = TeamAverage(f, true);
                _blend.Begin(Interp.Lerp(_p.BlendMax, _p.BlendMin, Interp.Clamp01((teamworkAvg + a) * 0.5f)));
                _profile = profile;
            }
            _blend.Step(_f, profile, Dt, in targetBlock, out var block);

            bool possessing = PhaseMachine.IsPossessing(f.Phase) || (f.Phase == TeamPhase.SetPiece && profile <= ShapeProfile.FinalThird);
            bool rematchTick = f.Tick % _p.RematchTicks == _rematchOffset;
            if (rematchTick) RefreshPitchControl(f, possessing);
            BuildExtras(profile);
            ShapeSolver.ComputeTargets(_p, in block, _blend.Current, _extra, _slotActive, _slotTargets);
            for (int i = 0; i < N; i++) _pressing[i] = false;
            for (int o = 0; o < 3; o++) { var w = _press.Presser(o); if (w > 0) _pressing[_slotOf[w] < 0 ? 0 : _slotOf[w]] = true; }
            _pressing[0] = false;
            ShapeSolver.Project(_p, in block, _slotActive, _pressing, _slotTargets);

            UpdateLine(f, in ins, in signals, rng, profile, possessing);
            if (CastChanged(f)) _rematchDue = true;
            if (rematchTick || _rematchDue) Rematch(f);
            if (rematchTick) DrawErrors(f, in ins, rng, errorsEnabled);

            ComposeTargets(f, in ins, manMark, profile, possessing);
            UpdateResponders(f, in ins, in signals, counterPress);
            WriteOutput(f, profile, telemetry);
        }

        ShapeProfile ResolveProfile(TeamFrame f, in TeamInstructions ins, out bool counterPress)
        {
            counterPress = false;
            switch (f.Phase)
            {
                case TeamPhase.BuildUp: return ShapeProfile.BuildUp;
                case TeamPhase.Progression: return ShapeProfile.Progression;
                case TeamPhase.FinalThird: return ShapeProfile.FinalThird;
                case TeamPhase.DefHigh: return ShapeProfile.DefHigh;
                case TeamPhase.DefMid: return ShapeProfile.DefMid;
                case TeamPhase.DefLow: return ShapeProfile.DefLow;
                case TeamPhase.AttTransition:
                    return f.Ball.X < _p.ThirdLow ? ShapeProfile.BuildUp : (f.Ball.X < _p.ThirdHigh ? ShapeProfile.Progression : ShapeProfile.FinalThird);
                case TeamPhase.DefTransition:
                    counterPress = ins.OnLoss == OnLossMode.CounterPress || (ins.OnLoss == OnLossMode.Mixed && f.Ball.X >= 0.5f);
                    return counterPress ? ShapeProfile.DefHigh : BlockProfile(f.Block);
                default:
                    // 세트피스: 같은 기간이면 직전 프로필 유지, 경기 시작·하프 전환이면 재시작 팀 여부로 정한다
                    if (_started && f.Period == _period) return _profile;
                    return f.RestartAttacking ? ShapeProfile.Progression : BlockProfile(f.Block);
            }
        }

        static ShapeProfile BlockProfile(TeamPhase block) =>
            block == TeamPhase.DefHigh ? ShapeProfile.DefHigh : (block == TeamPhase.DefLow ? ShapeProfile.DefLow : ShapeProfile.DefMid);

        void UpdateRoster(TeamFrame f)
        {
            int field = 0;
            for (int i = 1; i < N; i++) if (f.OwnActive[i]) field++;
            if (field == _fieldCount) return;
            _fieldCount = field;
            _rematchDue = true;
            for (int i = 0; i < N; i++) _slotActive[i] = true;
            int remove = N - 1 - field;
            if (remove <= 0) return;

            ReducedVariant variant = null;
            foreach (var r in _f.Reduced) if (r.FieldPlayers == field) { variant = r; break; }
            if (variant != null)
            {
                foreach (var s in variant.RemovedSlots) _slotActive[s] = false;
                return;
            }
            // 변형이 없으면 가장 앞선 슬롯부터 뺀다 (FR-92), 동점은 큰 번호부터
            for (int n = 0; n < remove; n++)
            {
                int best = -1;
                var bx = float.MinValue;
                for (int i = N - 1; i >= 1; i--)
                {
                    if (!_slotActive[i]) continue;
                    var x = _f.Slots[i].Anchors[(int)ShapeProfile.DefMid].X;
                    if (x > bx) { bx = x; best = i; }
                }
                if (best > 0) _slotActive[best] = false;
            }
        }

        float TeamAverage(TeamFrame f, bool concentration)
        {
            float sum = 0f;
            int n = 0;
            for (int i = 0; i < N; i++)
            {
                if (!f.OwnActive[i]) continue;
                var s = _slotBase + i;
                sum += concentration ? _in.Traits.Concentration(s) : _in.Traits.Teamwork(s);
                n++;
            }
            return n == 0 ? 0.5f : sum / n;
        }

        void RefreshPitchControl(TeamFrame f, bool possessing)
        {
            for (int i = 1; i < N; i++)
                _pc[i] = possessing && _slotActive[i] ? _in.PitchControl.GainDirection(_team, _slotTargets[i]) * _p.LambdaPc : default;
        }

        void BuildExtras(ShapeProfile profile)
        {
            for (int i = 1; i < N; i++) _extra[i] = _pc[i];
            for (int i = 1; i < N; i++)
            {
                var slot = _slotOf[i];
                if (slot > 0) _extra[slot] = _pc[slot] + _in.Roles.Offset(_slotBase + i, profile);
            }
        }

        void UpdateLine(TeamFrame f, in TeamInstructions ins, in PressSignals s, SeedRng rng, ShapeProfile profile, bool possessing)
        {
            float sum = 0f;
            int n = 0;
            for (int i = 1; i < N; i++)
                if (_slotActive[i] && _isLineSlot[i]) { sum += _slotTargets[i].X; n++; }
            var baseX = n > 0 ? sum / n : 0.3f;

            var threat = float.MaxValue;
            if (!possessing)
                for (int i = 1; i < N; i++)
                {
                    if (!f.OppActive[i]) continue;
                    if (f.OppVel[i].X < -_p.RunnerSpeed)
                    {
                        var x = f.Opp[i].X - _p.ThreatBufferM / _p.PitchL;
                        if (x < threat) threat = x;
                    }
                }
            if (threat < _p.ThreatFloorX) threat = _p.ThreatFloorX;

            _line.Step(baseX, threat, !possessing, in s, ins.OffsideTrap, rng, Dt);
            if (_lineHistDirty)
            {
                for (int i = 0; i < HistLen; i++) _hist[i] = _line.X;
                _lineHistDirty = false;
            }
            for (int i = HistLen - 1; i > 0; i--) _hist[i] = _hist[i - 1];
            _hist[0] = _line.X;

            if (profile <= ShapeProfile.FinalThird) return;
            for (int i = 1; i < N; i++)
                if (_slotActive[i] && _isLineSlot[i]) _slotTargets[i] = new Vec2(_line.X, _slotTargets[i].Y);
        }

        bool CastChanged(TeamFrame f)
        {
            bool changed = false;
            for (int i = 0; i < N; i++)
            {
                if (_wasCast[i] != f.Cast[i]) changed = true;
                _wasCast[i] = f.Cast[i];
            }
            return changed;
        }

        /// <summary>슬롯 재매칭 (FR-51·91). 배역 중 선수는 슬롯을 비우고, 사람이 모자라면 앞선 슬롯이 먼저 빈다.</summary>
        void Rematch(TeamFrame f)
        {
            _rematchDue = false;
            int rows = 0, cols = 0, free = 0;
            for (int i = 1; i < N; i++)
            {
                if (!f.OwnActive[i]) { _slotOf[i] = -1; continue; }
                if (f.Cast[i]) { _slotOf[i] = -1; continue; }
                _rows[rows++] = i;
                free++;
            }
            for (int s = 1; s < N; s++) if (_slotActive[s]) _cols[cols++] = s;
            _castOverflow = free < 6;
            if (rows == 0 || cols == 0) return;
            for (int r = cols; r < rows; r++) _slotOf[_rows[r]] = -1; // 슬롯보다 많은 선수는 이번 주기에 슬롯 없음
            if (rows > cols) rows = cols;

            for (int r = 0; r < rows; r++)
            {
                var pl = _rows[r];
                var discipline = _in.Roles.Discipline(_slotBase + pl);
                for (int c = 0; c < cols; c++)
                {
                    var s = _cols[c];
                    var dx = (f.Own[pl].X - _slotTargets[s].X) * _p.PitchL;
                    var dy = (f.Own[pl].Y - _slotTargets[s].Y) * _p.PitchW;
                    var cost = dx * dx + dy * dy + _in.Roles.RoleMismatch(_slotBase + pl, s);
                    if (_slotOf[pl] != s) cost += discipline * _p.SwitchPenalty;
                    if (_isLineSlot[s]) cost -= _p.VacancyBonus;
                    _cost[r * SlotMatcher.MaxSize + c] = cost;
                }
            }
            _matcher.Solve(rows, cols, _cost, _rowToCol);
            for (int r = 0; r < rows; r++) _slotOf[_rows[r]] = _cols[_rowToCol[r]];
        }

        void DrawErrors(TeamFrame f, in TeamInstructions ins, SeedRng rng, bool enabled)
        {
            for (int i = 1; i < N; i++)
            {
                if (!f.OwnActive[i]) { _slotErr[i] = default; _lineErr[i] = 0f; continue; }
                var s = _slotBase + i;
                var sigma = (_p.SigmaBase - _p.SigmaSlope * Interp.Clamp01(_in.Traits.Positioning(s))) * (1f + _p.FamiliarityMult * (1f - Interp.Clamp01(_in.Traits.Familiarity(s))));
                var teamwork = _in.Traits.Teamwork(s);
                var sigmaLine = DefensiveLine.CohesionSigma(_p, teamwork, f.Fatigue[i], ins.OffsideTrap);
                var ex = rng.Normal(0f, sigma);
                var ey = rng.Normal(0f, sigma);
                var el = rng.Normal(0f, sigmaLine);
                _slotErr[i] = enabled ? new Vec2(ex, ey) : default;
                _lineErr[i] = enabled ? el : 0f;
                _delayTicks[i] = Interp.Clamp((int)((1f - teamwork) * _p.SyncDelayMax * Core.Time.SimClock.TicksPerSecond), 0, HistLen - 1);
            }
        }

        void ComposeTargets(TeamFrame f, in TeamInstructions ins, int[] manMark, ShapeProfile profile, bool possessing)
        {
            var defendingShape = profile > ShapeProfile.FinalThird;
            var epsN = _p.OffsideEpsM / _p.PitchL;
            var offLimit = 1f - f.OppOffsideLine - epsN;

            // 1단계: 라인 슬롯 (라인 일체성 오차만 x에, 슬롯 오차는 y에 — plan 해석 1)
            var lineMin = float.MaxValue;
            for (int i = 1; i < N; i++)
            {
                _hasTarget[i] = false;
                if (!f.OwnActive[i] || f.Cast[i]) continue;
                var slot = _slotOf[i];
                if (slot <= 0) continue;
                var t = _slotTargets[slot];
                if (defendingShape && _isLineSlot[slot])
                {
                    var lineX = f.LineSyncAdvance ? _hist[_delayTicks[i]] : t.X;
                    t = new Vec2(lineX + _lineErr[i] / _p.PitchL, t.Y + _slotErr[i].Y / _p.PitchW);
                    if (t.X < lineMin) lineMin = t.X;
                }
                else t = new Vec2(t.X + _slotErr[i].X / _p.PitchL, t.Y + _slotErr[i].Y / _p.PitchW);
                _targets[i] = t;
                _hasTarget[i] = true;
            }

            // 2단계: 맨투맨 → 상한·하한 → 피치 안
            for (int i = 1; i < N; i++)
            {
                if (!_hasTarget[i]) continue;
                var slot = _slotOf[i];
                var t = _targets[i];
                if (!possessing && manMark != null && manMark[slot] >= 0 && f.OppActive[manMark[slot]])
                    t = ManMark(f.Opp[manMark[slot]], _slotTargets[slot]);
                if (possessing) { if (t.X > offLimit) t = new Vec2(offLimit, t.Y); }
                else if (lineMin != float.MaxValue && t.X < lineMin && !(defendingShape && _isLineSlot[slot]) && !_in.Roles.IsCover(_slotBase + i))
                    t = new Vec2(lineMin, t.Y);
                _targets[i] = ClampPitch(t);
            }

            // GK: 011 포지셔너 결과 (FR-93). 없으면(형태 엔진 단독 테스트) 009 대체식
            _hasTarget[0] = f.OwnActive[0] && !f.Cast[0];
            if (_hasTarget[0] && f.HasKeeperTarget) _targets[0] = f.KeeperTarget;
            else if (_hasTarget[0])
                _targets[0] = new Vec2(
                    Interp.Clamp(_p.GkBaseX + _p.GkBallX * (f.Ball.X - 0.5f), _p.GkMinX, _p.GkMaxX),
                    Interp.Clamp(0.5f + _p.GkBallY * (f.Ball.Y - 0.5f), _p.GkMinY, _p.GkMaxY));

            if (f.Phase == TeamPhase.SetPiece)
            {
                for (int i = 0; i < N; i++) _layoutActive[i] = f.OwnActive[i] && !f.Cast[i]; // 배역 선수는 벽·존에 쓰지 않는다
                _setPiece.Apply(f.Restart, f.RestartAttacking, f.RestartSpot, ins.Marking, f.RestartTaker, _layoutActive, f.Own, f.Opp, f.OppActive, _targets);
                for (int i = 1; i < N; i++) if (_hasTarget[i]) _targets[i] = ClampPitch(_targets[i]);
            }
        }

        Vec2 ManMark(Vec2 opponent, Vec2 slotTarget)
        {
            var goalSide = new Vec2(opponent.X - _p.ManOffsetM / _p.PitchL, opponent.Y);
            var dx = (goalSide.X - slotTarget.X) * _p.PitchL;
            var dy = (goalSide.Y - slotTarget.Y) * _p.PitchW;
            var d = DMath.Sqrt(dx * dx + dy * dy);
            if (d <= _p.ManLeashM) return goalSide;
            var k = _p.ManLeashM / d;
            return new Vec2(slotTarget.X + (goalSide.X - slotTarget.X) * k, slotTarget.Y + (goalSide.Y - slotTarget.Y) * k);
        }

        Vec2 ClampPitch(Vec2 t)
        {
            var mx = _p.PitchMarginM / _p.PitchL;
            var my = _p.PitchMarginM / _p.PitchW;
            return new Vec2(Interp.Clamp(t.X, mx, 1f - mx), Interp.Clamp(t.Y, my, 1f - my));
        }

        void UpdateResponders(TeamFrame f, in TeamInstructions ins, in PressSignals signals, bool counterPress)
        {
            for (int i = 0; i < N; i++) _candidate[i] = i > 0 && f.OwnActive[i] && !f.Cast[i];
            var ctx = new PressContext
            {
                Tick = f.Tick,
                Defending = !PhaseMachine.IsPossessing(f.Phase) && f.Phase != TeamPhase.SetPiece,
                CounterPressWindow = counterPress && f.TransitionTicks < _p.DefTicks,
                Ball = f.Ball,
                Own = f.Own,
                Opp = f.Opp,
                OwnCandidate = _candidate,
                OwnActive = f.OwnActive,
                OppActive = f.OppActive,
            };
            _press.Step(in signals, in ctx, in ins);

            // 1순위 대응자: 압박 지정 우선 — 지정된 선수의 슬롯은 후보에서 뺀다
            for (int s = 0; s < N; s++) _pressing[s] = false;
            for (int o = 0; o < 3; o++) { var w = _press.Presser(o); if (w > 0 && _slotOf[w] > 0) _pressing[_slotOf[w]] = true; }
            for (int s = 0; s < N; s++) _noSkip[s] = s > 0 && _slotActive[s] && SlotHasPlayer(s);
            var point = signals.PassReceiver >= 0 && f.OppActive[signals.PassReceiver] ? f.Opp[signals.PassReceiver] : f.Ball;
            var slotR = ZoneResponder.Find(point, _slotTargets, _zoneWeight, _noSkip, _pressing, _p.PitchL, _p.PitchW);
            _output.FirstResponder = slotR > 0 ? PlayerOfSlot(slotR) : -1;
        }

        bool SlotHasPlayer(int slot) => PlayerOfSlot(slot) >= 0;

        int PlayerOfSlot(int slot)
        {
            for (int i = 1; i < N; i++) if (_slotOf[i] == slot) return i;
            return -1;
        }

        void WriteOutput(TeamFrame f, ShapeProfile profile, ITeamShapeTelemetry telemetry)
        {
            _output.Phase = f.Phase;
            _output.Profile = profile;
            _output.DefLineX = _line.X;
            _output.OffsideLineX = DefensiveLine.OffsideLine(f.Own, f.OwnActive, f.Ball.X);
            _output.Press1 = _press.Presser(0);
            _output.Press2 = _press.Presser(1);
            _output.Press3 = _press.Presser(2);
            _output.PressCount = _press.Count;
            _output.PressActive = _press.Active;
            if (telemetry == null) return;
            ComputeMetrics(f, out var metrics); // 보드 출력에는 넣지 않는다: 텔레메트리 on/off가 시뮬 입력을 바꾸면 안 된다 (AC-20)
            telemetry.OnTeamShape(_team, f.Tick, f.Phase, in metrics);
        }

        void ComputeMetrics(TeamFrame f, out TeamShapeMetrics m)
        {
            float minX = float.MaxValue, maxX = float.MinValue, minY = float.MaxValue, maxY = float.MinValue;
            int n = 0;
            float bestSpeed = 0f;
            for (int i = 1; i < N; i++)
            {
                if (!f.OwnActive[i]) continue;
                var p = f.Own[i];
                if (p.X < minX) minX = p.X;
                if (p.X > maxX) maxX = p.X;
                if (p.Y < minY) minY = p.Y;
                if (p.Y > maxY) maxY = p.Y;
                _sortX[n++] = p.X;
                var sp = f.OwnVel[i].LengthSq();
                if (sp > bestSpeed) bestSpeed = sp;
            }
            m = default;
            if (n == 0) return;
            System.Array.Sort(_sortX, 0, n);
            m.Length = (maxX - minX) * _p.PitchL;
            m.Width = (maxY - minY) * _p.PitchW;
            m.Area = m.Length * m.Width;
            m.LineHeightM = minX * _p.PitchL;

            // 라인 묶음: 6 m 안이면 같은 라인 (AC-04 기준). 간격은 라인 평균 위치 차이의 평균
            const float SameLineM = 6f;
            int lines = 0;
            float prevMean = 0f, gapSum = 0f;
            for (int i = 0; i < n;)
            {
                int j = i;
                float sum = 0f;
                while (j < n && (_sortX[j] - _sortX[i]) * _p.PitchL <= SameLineM) sum += _sortX[j++];
                var mean = sum / (j - i);
                if (lines > 0) gapSum += (mean - prevMean) * _p.PitchL;
                prevMean = mean;
                lines++;
                i = j;
            }
            m.LineGapM = lines > 1 ? gapSum / (lines - 1) : 0f;

            var ballM = new Vec2(f.Ball.X * _p.PitchL, f.Ball.Y * _p.PitchW);
            for (int i = 1; i < N; i++)
            {
                if (!f.OwnActive[i]) continue;
                var pos = new Vec2(f.Own[i].X * _p.PitchL, f.Own[i].Y * _p.PitchW);
                var v = new Vec2(f.OwnVel[i].X, f.OwnVel[i].Y);
                if (PressureRules.IsPressing(_p, pos, v, ballM, v.LengthSq() >= bestSpeed)) m.PressingPlayers++;
            }
        }

        // ── 상태 ──

        public void Write(StateWriter w)
        {
            w.Write((byte)_profile); w.Write(_formationIndex); w.Write(_started); w.Write(_period); w.Write(_fieldCount); w.Write(_rematchDue); w.Write(_castOverflow); w.Write(_lineHistDirty);
            _blend.Write(w); _line.Write(w); _press.Write(w);
            for (int i = 0; i < N; i++)
            {
                w.Write(_slotOf[i]); w.Write(_slotActive[i]); w.Write(_wasCast[i]); w.Write(_slotErr[i]); w.Write(_lineErr[i]); w.Write(_pc[i]); w.Write(_delayTicks[i]); w.Write(_slotTargets[i]);
            }
            for (int i = 0; i < HistLen; i++) w.Write(_hist[i]);
        }

        public void Read(StateReader r)
        {
            _profile = (ShapeProfile)r.ReadByte(); _formationIndex = r.ReadInt(); _started = r.ReadBool(); _period = r.ReadInt(); _fieldCount = r.ReadInt(); _rematchDue = r.ReadBool(); _castOverflow = r.ReadBool(); _lineHistDirty = r.ReadBool();
            _f = _catalog[_formationIndex];
            for (int i = 0; i < N; i++) _zoneWeight[i] = _f.Slots[i].ZoneWeight;
            RefreshLineFlags();
            _blend.Read(r); _line.Read(r); _press.Read(r);
            for (int i = 0; i < N; i++)
            {
                _slotOf[i] = r.ReadInt(); _slotActive[i] = r.ReadBool(); _wasCast[i] = r.ReadBool(); _slotErr[i] = r.ReadVec2(); _lineErr[i] = r.ReadFloat(); _pc[i] = r.ReadVec2(); _delayTicks[i] = r.ReadInt(); _slotTargets[i] = r.ReadVec2();
            }
            for (int i = 0; i < HistLen; i++) _hist[i] = r.ReadFloat();
        }

        public void AddTo(ref StableHasher h)
        {
            h.Add((byte)_profile); h.Add(_formationIndex); h.Add(_started); h.Add(_period); h.Add(_fieldCount); h.Add(_rematchDue); h.Add(_castOverflow); h.Add(_lineHistDirty);
            _blend.AddTo(ref h); _line.AddTo(ref h); _press.AddTo(ref h);
            for (int i = 0; i < N; i++)
            {
                h.Add(_slotOf[i]); h.Add(_slotActive[i]); h.Add(_wasCast[i]); h.Add(_slotErr[i].X); h.Add(_slotErr[i].Y); h.Add(_lineErr[i]); h.Add(_pc[i].X); h.Add(_pc[i].Y); h.Add(_delayTicks[i]); h.Add(_slotTargets[i].X); h.Add(_slotTargets[i].Y);
            }
            for (int i = 0; i < HistLen; i++) h.Add(_hist[i]);
        }
    }
}
