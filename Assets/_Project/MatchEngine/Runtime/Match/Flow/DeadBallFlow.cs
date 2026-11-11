using RoadToPro.Core.Hashing;
using RoadToPro.Core.Math;
using RoadToPro.Core.Serialization;
using RoadToPro.MatchEngine.Ball;
using RoadToPro.MatchEngine.Match.Events;

namespace RoadToPro.MatchEngine.Match.Flow
{
    /// <summary>데드볼 진행 단계 (specs/058 FR-02·07). 값은 <see cref="SimEventKind.DeadBallPhase"/> 이벤트의 A.</summary>
    public enum DeadBallPhase : byte
    {
        None = 0,
        /// <summary>골 직후: 득점자는 코너 쪽으로 조깅, 나머지는 감속 (FR-02 ①).</summary>
        Celebration = 1,
        /// <summary>전원이 킥오프 배치 지점으로 걷거나 조깅해 돌아간다 (FR-02 ②).</summary>
        Return = 2,
        /// <summary>전원 도착(또는 상한 + 규칙 위반 0명) → 킥오프 준비 지연 (FR-02 ③).</summary>
        Ready = 3,
        /// <summary>재시작: 키커는 공으로, 나머지는 세트피스 배치로 (FR-07).</summary>
        Approach = 4,
        /// <summary>재시작 준비 지연 (FR-09).</summary>
        RestartReady = 5,
    }

    /// <summary>
    /// 데드볼 이동 진행기 (specs/058 FR-01~10). 선수를 배치 지점에 놓지(Place) 않고 목표·속도만 정한다 — 판단 단계가 그 목표로
    /// 이동 명령을 낸다. 공 재배치는 골·재시작마다 한 번, 화면에 보이지 않는 시각에만 하고 <see cref="SimEventKind.BallRelocated"/>로 남긴다.
    /// 틱 끝(<see cref="MatchFlow.EndTick"/>)에서 슬롯 오름차순으로 판정한다. RNG를 쓰지 않는다.
    /// </summary>
    public sealed class DeadBallFlow
    {
        readonly MatchContext _ctx;

        public DeadBallPhase Phase { get; private set; }
        public long PhaseStartTick { get; private set; } = -1;
        /// <summary>골 순간 틱 (골 데드볼이 아니면 −1).</summary>
        public long GoalTick { get; private set; } = -1;
        /// <summary>득점 팀 선수 중 득점자 슬롯 (자책골·모름이면 −1).</summary>
        public int Scorer { get; private set; } = -1;
        /// <summary>이번 데드볼의 공 재배치가 끝났다.</summary>
        public bool Relocated { get; private set; }
        bool _heldReported;

        internal DeadBallFlow(MatchContext ctx) => _ctx = ctx;

        public bool IsGoal => Phase == DeadBallPhase.Celebration || Phase == DeadBallPhase.Return || Phase == DeadBallPhase.Ready;
        /// <summary>골 데드볼에서 아직 킥오프 준비 전 (판단 덮어쓰기를 막는다).</summary>
        public bool GoalPending => Phase == DeadBallPhase.Celebration || Phase == DeadBallPhase.Return;
        /// <summary>준비 시각이 아직 없다 — 골 세리머니·복귀, 재시작 접근. 시나리오 세트피스 선택을 준비 단계까지 미룬다.</summary>
        public bool Pending => GoalPending || Phase == DeadBallPhase.Approach;

        // ── 시작 · 끝 ──

        internal void BeginGoal(int scorerSlot, int scoringTeam)
        {
            GoalTick = _ctx.Tick;
            Scorer = scorerSlot >= 0 && Squad.TeamOfSlot(scorerSlot) == scoringTeam ? scorerSlot : -1;
            Relocated = false;
            _heldReported = false;
            SetPhase(DeadBallPhase.Celebration);
        }

        /// <summary>재시작 데드볼 시작 (FR-07). 골 데드볼 도중이면 끊는다 (FR-06).</summary>
        internal void BeginRestart()
        {
            GoalTick = -1;
            Scorer = -1;
            Relocated = false;
            _heldReported = false;
            SetPhase(DeadBallPhase.Approach);
        }

        /// <summary>데드볼이 끝났다 (킥 적용·기간 끝·골 취소 뒤 재시작으로 전환).</summary>
        internal void End()
        {
            Phase = DeadBallPhase.None;
            PhaseStartTick = -1;
            GoalTick = -1;
            Scorer = -1;
            Relocated = false;
            _heldReported = false;
        }

        void SetPhase(DeadBallPhase p)
        {
            Phase = p;
            PhaseStartTick = _ctx.Tick;
            var e = SimEvent.Make(SimEventKind.DeadBallPhase);
            e.Substep = SimEvent.TickEnd;
            e.A = (int)p;
            _ctx.Emit(e);
        }

        // ── 목표 ──

        /// <summary>복귀 속도 비 (FR-03): 슬롯 순서로 [최소, 최대] 안에 고르게 나눈다 (RNG 없음).</summary>
        public float ReturnSpeedRatio(int slot)
        {
            var p = _ctx.Params;
            var u = (slot * 7 % 11) / 10f;
            return p.ReturnSpeedMin + (p.ReturnSpeedMax - p.ReturnSpeedMin) * u;
        }

        /// <summary>슬롯의 복귀 최고 속도 (m/s).</summary>
        public float ReturnSpeed(int slot) => _ctx.Kinematics.Profile(slot).MaxSpeed * ReturnSpeedRatio(slot);

        /// <summary>골 데드볼 목표 (킥오프 배치 지점).</summary>
        public Vec2 KickoffTarget(int slot) => KickoffLayout.Target(_ctx, _ctx.Flow.RestartTeam, _ctx.Flow.RestartTaker, slot);

        /// <summary>
        /// 세리머니 단계 득점자 목표 (FR-02 ①·FR-63): 가장 가까운 코너 플래그. 060이 세리머니 동선을 정하면 교체된다.
        /// 코너 플래그는 피치 경계 위라 피치 밖 3 m 제한 안이다.
        /// </summary>
        public Vec2 CelebrationTarget(int slot)
        {
            var pos = _ctx.SlotState(slot).Position;
            var L = _ctx.Pitch.LengthM;
            var W = _ctx.Pitch.WidthM;
            const float inset = 1f;
            var x = pos.X < L * 0.5f ? inset : L - inset;
            var y = pos.Y < W * 0.5f ? inset : W - inset;
            return new Vec2(x, y);
        }

        // ── 틱 끝 판정 ──

        internal void EndTick()
        {
            switch (Phase)
            {
                case DeadBallPhase.Celebration:
                    if (_ctx.Tick - GoalTick + 1 >= _ctx.Params.CelebrationTicks) SetPhase(DeadBallPhase.Return);
                    break;
                case DeadBallPhase.Return:
                    TryRelocateGoalBall();
                    if (Relocated) CheckReturnArrival();
                    break;
                case DeadBallPhase.Approach:
                    TryRelocateRestartBall();
                    if (Relocated) CheckRestartReady();
                    break;
            }
        }

        /// <summary>
        /// FR-08: 재시작 공 재배치는 공이 멈췄거나, 경기장 밖으로 [제안] 3 m 이상 나갔거나, 대기 상한이 지나면 한 번.
        /// 공이 이미 지점에 멈춰 있으면 이벤트 없이 끝낸다.
        /// </summary>
        void TryRelocateRestartBall()
        {
            if (Relocated) return;
            var flow = _ctx.Flow;
            ref var ball = ref _ctx.Ball;
            var spot = flow.RestartSpot;
            var p = _ctx.Params;
            var pos = ball.Position.XY;
            var stopped = ball.Velocity.LengthSq() < 0.25f;
            var m = p.BallOutRelocate;
            var farOut = pos.X < -m || pos.Y < -m || pos.X > _ctx.Pitch.LengthM + m || pos.Y > _ctx.Pitch.WidthM + m;
            if (!stopped && !farOut && _ctx.Tick - PhaseStartTick < p.RelocateMaxWaitTicks) return;
            Relocated = true;
            if (stopped && (pos - spot).LengthSq() < 1e-6f && ball.Phase != BallPhase.Controlled) return;
            _ctx.Ball = BallState.AtRest(spot, _ctx.BallParams.Radius);
            var e = SimEvent.Make(SimEventKind.BallRelocated);
            e.Substep = SimEvent.TickEnd;
            e.A = EventCodes.RelocateRestart;
            _ctx.SetPosition(ref e, new Vec3(spot.X, spot.Y, _ctx.BallParams.Radius));
            _ctx.Emit(e);
        }

        /// <summary>
        /// FR-09: 키커가 공을 잡고 나머지(형태 목표가 있는 선수)의 [제안] 80% 이상이 목표 3 m 안이면 준비. 상한([미정], 대체 30 s)이면
        /// 키커가 공을 잡은 상태에서 현재 위치로 준비.
        /// </summary>
        void CheckRestartReady()
        {
            var flow = _ctx.Flow;
            var taker = flow.RestartTaker;
            ref readonly var ball = ref _ctx.Ball;
            if (taker < 0 || !(ball.Phase == BallPhase.Controlled && ball.OwnerId == taker)) return; // 키커는 흐름이 다시 고른다 (ReselectTakerIfLost)
            var p = _ctx.Params;
            var board = _ctx.Board;
            int n = 0, near = 0;
            var r2 = p.RestartArriveRadius * p.RestartArriveRadius;
            for (int s = 0; s < Squad.Slots; s++)
            {
                if (s == taker || !_ctx.IsSlotControllable(s) || !board.HasShapeTarget(s)) continue;
                n++;
                if ((_ctx.SlotState(s).Position - board.ShapeTarget(s)).LengthSq() <= r2) near++;
            }
            var arrived = n == 0 || near >= p.RestartArriveShare * n;
            if (!arrived && _ctx.Tick - PhaseStartTick < p.RestartWaitMaxTicks) return;
            SetPhase(DeadBallPhase.RestartReady);
            flow.SetReady(_ctx.Tick + 1 + p.RestartDelayTicks);
        }

        /// <summary>FR-04: 복귀 단계(라이브 골 장면이 끝난 뒤)에 공이 멈췄거나 대기 상한이 지나면 센터 스폿으로 한 번 옮긴다.</summary>
        void TryRelocateGoalBall()
        {
            if (Relocated) return;
            ref var ball = ref _ctx.Ball;
            var center = KickoffLayout.Center(_ctx);
            var stopped = ball.Velocity.LengthSq() < 0.25f;
            if (!stopped && _ctx.Tick - PhaseStartTick < _ctx.Params.RelocateMaxWaitTicks) return;
            Relocated = true;
            if ((ball.Position.XY - center).LengthSq() < 1e-6f && ball.Phase != BallPhase.Controlled) return; // 이미 그 자리
            _ctx.Ball = BallState.AtRest(center, _ctx.BallParams.Radius);
            var e = SimEvent.Make(SimEventKind.BallRelocated);
            e.Substep = SimEvent.TickEnd;
            e.A = EventCodes.RelocateGoal;
            _ctx.SetPosition(ref e, new Vec3(center.X, center.Y, _ctx.BallParams.Radius));
            _ctx.Emit(e);
        }

        /// <summary>FR-02 ③·FR-03: 전원 도착 → 준비. 상한이면 규칙 위반 0명일 때만, 아니면 진단 1회 뒤 기다린다.</summary>
        void CheckReturnArrival()
        {
            var flow = _ctx.Flow;
            var kickTeam = flow.RestartTeam;
            var kicker = flow.RestartTaker;
            var r2 = _ctx.Params.ArriveRadius * _ctx.Params.ArriveRadius;
            bool all = kicker >= 0 && _ctx.IsSlotControllable(kicker); // 058 리뷰 H2: 키커 없음이면 준비하지 않는다
            int violator = -1;
            ref readonly var ball = ref _ctx.Ball;
            for (int s = 0; s < Squad.Slots; s++)
            {
                if (!_ctx.IsSlotControllable(s)) continue;
                var pos = _ctx.SlotState(s).Position;
                if (s == kicker)
                {
                    if (!(ball.Phase == BallPhase.Controlled && ball.OwnerId == s)) all = false;
                    continue;
                }
                if ((pos - KickoffTarget(s)).LengthSq() > r2) all = false;
                if (violator < 0 && KickoffLayout.Violates(_ctx, kickTeam, kicker, s, pos)) violator = s;
            }
            if (!all)
            {
                var capped = _ctx.Tick - GoalTick + 1 >= _ctx.Params.GoalCelebrationTicks;
                if (!capped) return;
                // 상한: 키커가 공을 못 잡았으면 시작할 수 없다 — 위반자로 친다
                if (violator < 0 && kicker >= 0 && !(ball.Phase == BallPhase.Controlled && ball.OwnerId == kicker)) violator = kicker;
                if (violator >= 0)
                {
                    if (!_heldReported)
                    {
                        _heldReported = true;
                        var e = SimEvent.Make(SimEventKind.RestartHeld, Squad.TeamOfSlot(violator), _ctx.PlayerAtSlot(violator));
                        e.Substep = SimEvent.TickEnd;
                        _ctx.SetPosition(ref e, _ctx.SlotState(violator).Position);
                        _ctx.Emit(e);
                    }
                    return;
                }
            }
            SetPhase(DeadBallPhase.Ready);
            flow.SetReady(_ctx.Tick + 1 + _ctx.Params.KickoffDelayTicks);
        }

        // ── 직렬화 ──

        internal void Write(StateWriter w)
        {
            w.Write((byte)Phase); w.Write(PhaseStartTick); w.Write(GoalTick); w.Write(Scorer); w.Write(Relocated); w.Write(_heldReported);
        }

        internal void Read(StateReader r)
        {
            Phase = (DeadBallPhase)r.ReadByte(); PhaseStartTick = r.ReadLong(); GoalTick = r.ReadLong(); Scorer = r.ReadInt(); Relocated = r.ReadBool(); _heldReported = r.ReadBool();
        }

        internal void AddTo(ref StableHasher h)
        {
            h.Add((byte)Phase); h.Add(PhaseStartTick); h.Add(GoalTick); h.Add(Scorer); h.Add(Relocated); h.Add(_heldReported);
        }
    }
}
