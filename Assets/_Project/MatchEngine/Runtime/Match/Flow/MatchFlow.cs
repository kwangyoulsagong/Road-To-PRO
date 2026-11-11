using RoadToPro.Core.Hashing;
using RoadToPro.Core.Math;
using RoadToPro.Core.Serialization;
using RoadToPro.Core.Time;
using RoadToPro.MatchEngine.Ball;
using RoadToPro.MatchEngine.Match.Events;

namespace RoadToPro.MatchEngine.Match.Flow
{
    public enum FlowState : byte
    {
        DeadBall = 0,
        InPlay = 1,
        Finished = 2,
    }

    public enum RestartKind : byte
    {
        None = 0,
        Kickoff = 1,
        ThrowIn = 2,
        GoalKick = 3,
        Corner = 4,
        FreeKick = 5,
        Penalty = 6,
        DropBall = 7,
    }

    public enum EndReason : byte
    {
        None = 0,
        Normal = 1,
        Abandoned = 2,
        Failed = 3,
    }

    /// <summary>
    /// 경기 진행 상태 기계 (specs/006 FR-11·14·15·21·22). 단계 밖에서 틱 시작(<see cref="BeginTick"/>)과
    /// 틱 끝(<see cref="EndTick"/>, 심판 단계 뒤·발행 전)에 판정한다.
    /// 기간 끝: 틱 t를 처리한 뒤 경과 = t − 기간 시작 + 1이 명목 + 실제 추가 이상이고 페널티 대기가 아니면 종료 휘슬.
    /// 그 틱의 서브스텝 사건은 모두 종료 시각 이하이므로 인정되고(AC-09), 다음 서브스텝은 시뮬되지 않는다.
    /// 휴식(하프타임 등)은 0틱이다 — 기간이 이어 붙는다 (001 MatchTimeline).
    /// </summary>
    public sealed class MatchFlow
    {
        readonly MatchContext _ctx;
        public readonly Substitutions Subs = new Substitutions();
        public readonly StoppageClock Stoppage;
        /// <summary>데드볼 이동 진행 (specs/058).</summary>
        public readonly DeadBallFlow DeadBall;
        /// <summary>걸어 나감·들어옴 (specs/058 FR-61·65).</summary>
        public readonly OffPitchWalkers Walkers;
        public IPenaltyJudge PenaltyJudge = new StandInPenaltyJudge();

        public FlowState State { get; private set; }
        public int Period { get; private set; }
        public long PeriodStartTick { get; private set; }
        public bool Announced { get; private set; }
        public int AnnouncedMinutes { get; private set; }
        public RestartKind Restart { get; private set; }
        public int RestartTeam { get; private set; } = -1;
        public Vec2 RestartSpot { get; private set; }
        public int RestartTaker { get; private set; } = -1;
        public long ReadyTick { get; private set; }
        public bool PenaltyPending { get; private set; }
        public long PenaltyKickTick { get; private set; } = -1;
        public int DeadBallId { get; private set; }
        public EndReason EndReason { get; private set; }
        public long EndTickValue { get; private set; } = -1;
        public int FirstKickoffTeam { get; private set; }
        public int ExtraKickoffTeam { get; private set; } = -1;
        bool _periodStartPending;
        bool _abandonPending;
        bool _shootoutPlayed;

        readonly int[] _regular = new int[2];
        readonly int[] _extra = new int[2];
        readonly int[] _shootout = new int[2];
        readonly long[] _periodLength = new long[4];

        public int Goals(int team) => _regular[team] + _extra[team];
        public int RegularGoals(int team) => _regular[team];
        public int ExtraGoals(int team) => _extra[team];
        public int ShootoutGoals(int team) => _shootout[team];
        public bool ShootoutPlayed => _shootoutPlayed;
        public long PeriodLength(int p) => _periodLength[p];
        public bool IsFinished => State == FlowState.Finished;
        public bool InPlay => State == FlowState.InPlay;

        /// <summary>이번 기간 경과 틱 (지금 틱 포함).</summary>
        public long Elapsed => _ctx.Tick - PeriodStartTick + 1;
        public long NominalTicks => MatchTime.NominalTicks((MatchPeriod)Period);
        /// <summary>실제 추가시간 (발표 전 0).</summary>
        public long ActualAddedTicks
        {
            get
            {
                if (!Announced) return 0;
                var ann = AnnouncedMinutes * SimClock.TicksPerMinute;
                var total = Stoppage.TotalTicksAt(_ctx.Tick); // 058 FR-05: 진행 중 골 정지 포함
                return ann > total ? ann : total;
            }
        }

        internal MatchFlow(MatchContext ctx)
        {
            _ctx = ctx;
            Stoppage = new StoppageClock(ctx.Params.StoppageCoefTicks);
            DeadBall = new DeadBallFlow(ctx);
            Walkers = new OffPitchWalkers(ctx);
        }

        /// <summary>경기 시작 (틱 0 전): 동전 던지기, 킥오프 배치.</summary>
        internal void Start()
        {
            FirstKickoffTeam = _ctx.FlowRng.Bernoulli(0.5f) ? 0 : 1;
            Period = 0;
            PeriodStartTick = 0;
            _periodStartPending = true;
            BeginKickoff(FirstKickoffTeam, 0);
        }

        internal void BeginTick()
        {
            if (!_periodStartPending) return;
            _periodStartPending = false;
            var e = SimEvent.Make(SimEventKind.PeriodStart);
            e.A = Period;
            e.B = Restart == RestartKind.Kickoff ? RestartTeam : -1;
            _ctx.EmitAs(e, 0);
        }

        // ── 단계가 부르는 사건 ──

        /// <summary>
        /// 골 (심판 단계): 점수, 실점 팀 킥오프(자책골 포함, 006 AC-05). 선수는 배치하지 않는다 — 골 후 데드볼(058 FR-01·02)이
        /// 세리머니 → 복귀 → 준비를 진행하고, 골 정지 시간은 골 순간 ~ 킥오프 실제 길이다(FR-05).
        /// </summary>
        public void OnGoal(int scoringTeam, int scorerSlot = -1)
        {
            if (Period >= 2) _extra[scoringTeam]++; else _regular[scoringTeam]++;
            ClearPenalty();
            var conceding = 1 - scoringTeam;
            Stoppage.BeginRunning(EventCodes.StopGoal, _ctx.Tick);
            State = FlowState.DeadBall;
            DeadBallId++;
            Restart = RestartKind.Kickoff;
            RestartTeam = conceding;
            RestartSpot = KickoffLayout.Center(_ctx);
            RestartTaker = KickoffLayout.Kicker(_ctx, conceding);
            ReadyTick = long.MaxValue; // 준비 단계에서 정한다
            _pendingDelay = 0;
            DeadBall.BeginGoal(scorerSlot, scoringTeam);
        }

        /// <summary>
        /// 058 리뷰 H1·H2: 키커가 걸어 나가거나 빠졌으면 다시 고른다 (재시작은 지점에 가장 가까운 선수, 킥오프는 대형 맨 앞).
        /// 키커가 공을 잡고 있었으면 공을 놓는다.
        /// </summary>
        internal void ReselectTakerIfLost()
        {
            if (State != FlowState.DeadBall || RestartTeam < 0) return;
            if (RestartTaker >= 0 && _ctx.IsSlotControllable(RestartTaker)) return;
            if (RestartTaker >= 0 && _ctx.Ball.Phase == BallPhase.Controlled && _ctx.Ball.OwnerId == RestartTaker)
                _ctx.Ball = BallState.AtRest(_ctx.Ball.Position.XY, _ctx.BallParams.Radius);
            RestartTaker = Restart == RestartKind.Kickoff ? KickoffLayout.Kicker(_ctx, RestartTeam) : NearestSlot(RestartTeam, RestartSpot, Restart == RestartKind.GoalKick);
        }

        /// <summary>골 데드볼 준비 단계 진입 (058 FR-02 ③).</summary>
        internal void SetReady(long readyTick)
        {
            ReadyTick = readyTick + _pendingDelay;
            _pendingDelay = 0;
        }

        long _pendingDelay;

        /// <summary>VAR 등으로 골 취소 (012): 점수만 되돌린다. 이미 발표한 추가시간은 줄지 않는다.</summary>
        public void DisallowGoal(int team)
        {
            if (DeadBall.IsGoal) { Stoppage.EndRunning(_ctx.Tick); DeadBall.End(); } // 058 FR-06: 세리머니·복귀를 끊는다
            if (Period >= 2) { if (_extra[team] > 0) _extra[team]--; }
            else if (_regular[team] > 0) _regular[team]--;
        }

        /// <summary>재시작 (심판 단계). 공은 지점에 멈추고, 키커는 그 팀에서 지점에 가장 가까운 선수.</summary>
        public void BeginRestart(RestartKind kind, int team, Vec2 spot)
        {
            State = FlowState.DeadBall;
            DeadBallId++;
            Restart = kind;
            RestartTeam = team;
            RestartSpot = spot;
            RestartTaker = NearestSlot(team, spot, kind == RestartKind.GoalKick);
            ReadyTick = long.MaxValue; // 058 FR-09: 접근이 끝나면 정한다
            _pendingDelay = 0;
            if (DeadBall.IsGoal) Stoppage.EndRunning(_ctx.Tick);
            if (_ctx.Ball.Phase == BallPhase.Controlled) _ctx.Ball = BallState.AtRest(_ctx.Ball.Position.XY, _ctx.BallParams.Radius); // 소유 해제 (공은 그 자리)
            DeadBall.BeginRestart(); // 058 FR-07·08: 공은 접근 단계에서 한 번 옮긴다
            if (kind == RestartKind.Penalty) PenaltyPending = true;
        }

        /// <summary>정지 시간 추가 (012 VAR·카드·항의, 027 부상 처치). 데드볼이면 재시작도 그만큼 늦춘다.</summary>
        public void AddStoppage(int kind, long ticks)
        {
            // 058 FR-05: 골 데드볼 중이면 골 정지(골 ~ 킥오프 실제 길이)가 이 지연을 이미 담는다 — 시계에 또 더하지 않는다 (리뷰 M1)
            if (!Stoppage.Running) Stoppage.Add(kind, ticks);
            if (State != FlowState.DeadBall) return;
            if (ReadyTick == long.MaxValue) _pendingDelay += ticks; // 준비 시각 전: 정해질 때 더한다
            else ReadyTick += ticks;
        }

        /// <summary>재시작 킥이 나갔다 (물리 단계, 킥 적용 서브스텝).</summary>
        public void OnRestartTaken()
        {
            if (Restart == RestartKind.Penalty) PenaltyKickTick = _ctx.Tick;
            if (DeadBall.IsGoal) Stoppage.EndRunning(_ctx.Tick); // 058 FR-05
            DeadBall.End();
            State = FlowState.InPlay;
            Restart = RestartKind.None;
            RestartTaker = -1;
        }

        /// <summary>페널티 킥 결과가 났다 (공이 나감·골·잡힘·시간 초과).</summary>
        public void ClearPenalty()
        {
            PenaltyPending = false;
            PenaltyKickTick = -1;
        }

        /// <summary>7명 미만 (012 FR-91 이벤트): 이번 틱에 중단 결과로 끝낸다 (FR-14, AC-07).</summary>
        public void RequestAbandon() => _abandonPending = true;

        /// <summary>교체 없이 빠짐 (퇴장·부상): 경기 인원에서 바로 빼고, 가까운 터치라인으로 걸어 나가게 한다 (058 FR-65 — 순간이동·즉시 삭제 없음).</summary>
        public void RemoveFromPitch(int slot, PlayerStatus why)
        {
            var k = _ctx.Kinematics;
            var id = _ctx.Squad.PlayerAt(slot);
            _ctx.Squad.Remove(slot, why, _ctx.Tick, k.Stats(slot));
            Walkers.BeginLeave(slot, id, why == PlayerStatus.SentOff ? WalkKind.SentOff : WalkKind.InjuredOff);
            if (_ctx.Ball.Phase == BallPhase.Controlled && _ctx.Ball.OwnerId == slot)
                _ctx.Ball = BallState.AtRest(_ctx.Ball.Position.XY, _ctx.BallParams.Radius);
        }

        public void RequestSubstitution(in SubstitutionRequest r) => Subs.Enqueue(r);

        // ── 틱 끝 판정 ──

        internal void EndTick()
        {
            if (State == FlowState.Finished) return;
            var tick = _ctx.Tick;
            if (_abandonPending)
            {
                var e = SimEvent.Make(SimEventKind.MatchAbandoned);
                e.Substep = SimEvent.TickEnd;
                e.A = _ctx.Squad.OnPitchCount(0);
                e.B = _ctx.Squad.OnPitchCount(1);
                _ctx.Emit(e);
                Finish(EndReason.Abandoned);
                return;
            }
            if (PenaltyKickTick >= 0 && tick - PenaltyKickTick >= 3 * SimClock.TicksPerSecond) ClearPenalty();
            Walkers.EndTick();
            if (State == FlowState.DeadBall) { ReselectTakerIfLost(); DeadBall.EndTick(); }

            var elapsed = Elapsed;
            if (!Announced && elapsed >= NominalTicks)
            {
                Announced = true;
                AnnouncedMinutes = Stoppage.AnnouncedMinutesAt(tick);
                var e = SimEvent.Make(SimEventKind.AddedTimeAnnounced);
                e.Substep = SimEvent.TickEnd;
                e.A = AnnouncedMinutes;
                e.B = (int)Stoppage.TotalTicksAt(tick);
                _ctx.Emit(e);
            }
            if (Announced && elapsed >= NominalTicks + ActualAddedTicks && !PenaltyPending && DeadBall.Phase != DeadBallPhase.Celebration) // 058 FR-60
            {
                EndPeriod();
                return;
            }
            if (State == FlowState.DeadBall && Subs.Queued > 0)
            {
                var n = Subs.Process(_ctx, false, DeadBallId, false);
                if (n > 0) AddStoppage(EventCodes.StopSubstitution, n * _ctx.Params.SubDelayTicks);
            }
        }

        void EndPeriod()
        {
            var tick = _ctx.Tick;
            Stoppage.EndRunning(tick);
            DeadBall.End(); // 058 FR-60: 복귀 단계를 끊는다
            _periodLength[Period] = tick - PeriodStartTick + 1;
            var e = SimEvent.Make(SimEventKind.PeriodEnd);
            e.Substep = SimEvent.TickEnd;
            e.A = Period;
            e.B = (int)(_periodLength[Period] - NominalTicks);
            _ctx.Emit(e);
            ClearPenalty();

            var level = Goals(0) == Goals(1);
            var rules = _ctx.Setup.Rules;
            int next = -1;
            if (Period == 0) next = 1;
            else if (Period == 1 && level && rules.ExtraTime) next = 2;
            else if (Period == 2) next = 3;

            if (next >= 0)
            {
                Walkers.FlushAtBreak(); // 058: 기간 사이에는 걷던 선수를 끝낸다 (이어지는 킥오프 배치, FR-10)
                if (next == 2) Subs.GrantExtraTime();
                if (Subs.Queued > 0) Subs.Process(_ctx, true, DeadBallId, false); // 휴식 교체 (창 미소모)
                Period = next;
                PeriodStartTick = tick + 1;
                Announced = false;
                AnnouncedMinutes = 0;
                Stoppage.ResetPeriod();
                _periodStartPending = true;
                int kickTeam;
                if (next == 1) kickTeam = 1 - FirstKickoffTeam;
                else if (next == 2) { ExtraKickoffTeam = _ctx.FlowRng.Bernoulli(0.5f) ? 0 : 1; kickTeam = ExtraKickoffTeam; }
                else kickTeam = 1 - ExtraKickoffTeam;
                BeginKickoff(kickTeam, tick + 1);
                return;
            }
            if (level && rules.Shootout && (Period == 3 || !rules.ExtraTime))
            {
                _shootoutPlayed = true;
                PenaltyShootout.Run(_ctx, PenaltyJudge, _shootout);
            }
            Finish(EndReason.Normal);
        }

        void Finish(EndReason why)
        {
            if (why == EndReason.Normal)
            {
                var e = SimEvent.Make(SimEventKind.MatchEnd);
                e.Substep = SimEvent.TickEnd;
                e.A = Goals(0);
                e.B = Goals(1);
                _ctx.Emit(e);
            }
            if (_periodLength[Period] == 0) _periodLength[Period] = _ctx.Tick - PeriodStartTick + 1;
            State = FlowState.Finished;
            EndReason = why;
            EndTickValue = _ctx.Tick;
            Walkers.FlushAtBreak(); // 058 리뷰 M3: 걷던 교체를 마저 교대해 결과와 맞춘다
            Subs.Process(_ctx, true, DeadBallId, true); // 남은 요청은 경기 종료로 거부
        }

        /// <summary>단계 예외·NaN (FR-72): 원인 틱·단계와 함께 실패로 끝낸다.</summary>
        internal void Fail(int stage, string message)
        {
            if (State == FlowState.Finished && EndReason == EndReason.Failed) return;
            State = FlowState.Finished; // 이벤트를 내기 전에 상태부터 (Emit이 던져도 경기는 끝난다, 리뷰 R4)
            EndReason = EndReason.Failed;
            EndTickValue = _ctx.Tick;
            if (_periodLength[Period] == 0) _periodLength[Period] = _ctx.Tick - PeriodStartTick + 1;
            var e = SimEvent.Make(SimEventKind.MatchFailed);
            e.Substep = SimEvent.TickEnd;
            e.A = stage;
            _ctx.Emit(e);
        }

        /// <summary>실패 처리 중 대기열을 버린 뒤 실패 이벤트만 다시 낸다.</summary>
        internal void ForceFailed(int stage)
        {
            State = FlowState.Finished;
            EndReason = EndReason.Failed;
            EndTickValue = _ctx.Tick;
            if (_periodLength[Period] == 0) _periodLength[Period] = _ctx.Tick - PeriodStartTick + 1;
            var e = SimEvent.Make(SimEventKind.MatchFailed);
            e.Substep = SimEvent.TickEnd;
            e.A = stage;
            _ctx.Emit(e);
        }

        // ── 킥오프 배치 ──

        void BeginKickoff(int team, long readyFrom)
        {
            State = FlowState.DeadBall;
            DeadBallId++;
            Restart = RestartKind.Kickoff;
            RestartTeam = team;
            DeadBall.End();
            var center = KickoffLayout.Center(_ctx);
            RestartSpot = center;
            ReadyTick = readyFrom + _ctx.Params.KickoffDelayTicks;
            PlaceKickoff(team, center);
        }

        /// <summary>킥오프 배치 (경기·하프 시작, 058 FR-10): <see cref="KickoffLayout"/> 지점에 바로 놓는다. 공은 키커가 잡는다.</summary>
        void PlaceKickoff(int kickTeam, Vec2 center)
        {
            var k = _ctx.Kinematics;
            var kicker = KickoffLayout.Kicker(_ctx, kickTeam);
            for (int s = 0; s < Squad.Slots; s++)
            {
                if (!_ctx.IsSlotActive(s)) continue;
                k.Place(s, KickoffLayout.Target(_ctx, kickTeam, kicker, s), new Vec2(_ctx.AttackDir(Squad.TeamOfSlot(s)), 0f));
            }
            RestartTaker = kicker;
            if (kicker >= 0)
            {
                _ctx.Ball = BallState.Controlled(kicker, new Vec3(center.X, center.Y, _ctx.BallParams.Radius));
                _ctx.PossessionStartTick = _ctx.Tick;
            }
            else _ctx.Ball = BallState.AtRest(center, _ctx.BallParams.Radius);
            _ctx.LastTouchTeam = kickTeam;
            _ctx.LastTouchSlot = kicker;
            ClearPenalty();
        }

        int NearestSlot(int team, Vec2 spot, bool keeperFirst)
        {
            if (keeperFirst && _ctx.IsSlotControllable(team * Squad.SlotsPerTeam)) return team * Squad.SlotsPerTeam;
            int best = -1;
            float bd = float.MaxValue;
            for (int s = team * Squad.SlotsPerTeam; s < (team + 1) * Squad.SlotsPerTeam; s++)
            {
                if (!_ctx.IsSlotControllable(s)) continue;
                if (s == team * Squad.SlotsPerTeam && !keeperFirst && _ctx.Squad.OnPitchCount(team) > 1) continue;
                var d = (_ctx.Kinematics.State(s).Position - spot).LengthSq();
                if (d < bd) { bd = d; best = s; }
            }
            return best;
        }

        // ── 직렬화 ──

        internal void Write(StateWriter w)
        {
            w.Write((byte)State); w.Write(Period); w.Write(PeriodStartTick); w.Write(Announced); w.Write(AnnouncedMinutes);
            w.Write((byte)Restart); w.Write(RestartTeam); w.Write(RestartSpot); w.Write(RestartTaker); w.Write(ReadyTick);
            w.Write(PenaltyPending); w.Write(PenaltyKickTick); w.Write(DeadBallId); w.Write((byte)EndReason); w.Write(EndTickValue);
            w.Write(FirstKickoffTeam); w.Write(ExtraKickoffTeam); w.Write(_periodStartPending); w.Write(_abandonPending); w.Write(_shootoutPlayed);
            for (int t = 0; t < 2; t++) { w.Write(_regular[t]); w.Write(_extra[t]); w.Write(_shootout[t]); }
            for (int p = 0; p < 4; p++) w.Write(_periodLength[p]);
            Stoppage.Write(w);
            Subs.Write(w);
            DeadBall.Write(w);
            Walkers.Write(w);
            w.Write(_pendingDelay);
        }

        internal void Read(StateReader r)
        {
            State = (FlowState)r.ReadByte(); Period = r.ReadInt(); PeriodStartTick = r.ReadLong(); Announced = r.ReadBool(); AnnouncedMinutes = r.ReadInt();
            Restart = (RestartKind)r.ReadByte(); RestartTeam = r.ReadInt(); RestartSpot = r.ReadVec2(); RestartTaker = r.ReadInt(); ReadyTick = r.ReadLong();
            PenaltyPending = r.ReadBool(); PenaltyKickTick = r.ReadLong(); DeadBallId = r.ReadInt(); EndReason = (EndReason)r.ReadByte(); EndTickValue = r.ReadLong();
            FirstKickoffTeam = r.ReadInt(); ExtraKickoffTeam = r.ReadInt(); _periodStartPending = r.ReadBool(); _abandonPending = r.ReadBool(); _shootoutPlayed = r.ReadBool();
            for (int t = 0; t < 2; t++) { _regular[t] = r.ReadInt(); _extra[t] = r.ReadInt(); _shootout[t] = r.ReadInt(); }
            for (int p = 0; p < 4; p++) _periodLength[p] = r.ReadLong();
            Stoppage.Read(r);
            Subs.Read(r);
            DeadBall.Read(r);
            Walkers.Read(r);
            _pendingDelay = r.ReadLong();
        }

        internal void AddTo(ref StableHasher h)
        {
            h.Add((byte)State); h.Add(Period); h.Add(PeriodStartTick); h.Add(Announced); h.Add(AnnouncedMinutes);
            h.Add((byte)Restart); h.Add(RestartTeam); h.Add(RestartSpot.X); h.Add(RestartSpot.Y); h.Add(RestartTaker); h.Add(ReadyTick);
            h.Add(PenaltyPending); h.Add(PenaltyKickTick); h.Add(DeadBallId); h.Add((byte)EndReason); h.Add(EndTickValue);
            h.Add(FirstKickoffTeam); h.Add(ExtraKickoffTeam); h.Add(_periodStartPending); h.Add(_abandonPending); h.Add(_shootoutPlayed);
            for (int t = 0; t < 2; t++) { h.Add(_regular[t]); h.Add(_extra[t]); h.Add(_shootout[t]); }
            for (int p = 0; p < 4; p++) h.Add(_periodLength[p]);
            Stoppage.AddTo(ref h);
            Subs.AddTo(ref h);
            DeadBall.AddTo(ref h);
            Walkers.AddTo(ref h);
            h.Add(_pendingDelay);
        }
    }
}
