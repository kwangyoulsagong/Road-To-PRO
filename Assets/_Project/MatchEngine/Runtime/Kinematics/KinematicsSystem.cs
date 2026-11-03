using System;
using RoadToPro.Core.Hashing;
using RoadToPro.Core.Math;

namespace RoadToPro.MatchEngine.Kinematics
{
    /// <summary>
    /// 선수 운동 시스템 (specs/005). 선수 상태·프로필·지연 큐·기록을 소유하고 서브스텝(100 Hz)을 진행한다.
    /// 처리 순서는 생성 시 id 오름차순으로 고정 (입력 순서 무관, AC-12). 서브스텝 경로 할당 0.
    /// 서브스텝 순서: ① 명령 활성화 ② 행동 목표 속도(스냅숏 기준) ③ Separation ④ 합성 ⑤ 한계 풀이·적분
    /// ⑥ 겹침 위치 투영 ⑦ 피치 경계 ⑧ 피로 ⑨ 기록·진단·모니터 ⑩ 수치 검사.
    /// </summary>
    public sealed class KinematicsSystem
    {
        public const int MaxPlayers = 32;

        readonly KinematicsParams _p;
        readonly int _n;
        readonly int[] _ids;
        readonly PlayerMotionState[] _s;
        readonly PlayerMotionProfile[] _prof;
        readonly MotionStats[] _stats;
        readonly CommandQueue _queue;
        readonly bool[] _contact;
        readonly Vec2[] _snapPos, _snapVel, _push, _desired, _faultVel;
        readonly bool[] _fault;
        readonly float[] _sepCorr;
        readonly SolveInfo[] _info;
        readonly int[] _targetIdx;
        readonly Separation _sep;

        public long Substep { get; private set; }
        public int Count => _n;
        public KinematicsParams Params => _p;

        /// <summary>진단 출력 (FR-51). null이면 호출 0.</summary>
        public IKinematicsDiagnostics Diagnostics { get; set; }
        /// <summary>운동 한계 모니터 (FR-52). 검증 모드에서만 설정. null이면 검사 0.</summary>
        public MotionLimitMonitor Monitor { get; set; }

        public KinematicsSystem(KinematicsParams p, PlayerSetup[] players)
        {
            if (p == null) throw new ArgumentNullException(nameof(p));
            if (players == null) throw new ArgumentNullException(nameof(players));
            if (players.Length > MaxPlayers) throw new ArgumentException($"선수는 최대 {MaxPlayers}명", nameof(players));
            _p = p;
            _n = players.Length;
            var sorted = (PlayerSetup[])players.Clone();
            Array.Sort(sorted, (a, b) => a.Id.CompareTo(b.Id));
            _ids = new int[_n];
            _s = new PlayerMotionState[_n];
            _prof = new PlayerMotionProfile[_n];
            _stats = new MotionStats[_n];
            for (int i = 0; i < _n; i++)
            {
                if (i > 0 && sorted[i].Id == sorted[i - 1].Id) throw new ArgumentException($"선수 id 중복: {sorted[i].Id}", nameof(players));
                var su = sorted[i];
                if (!Finite(su.Position) || !Finite(su.Velocity) || !FloatGuard.IsFinite(su.W) || !FloatGuard.IsFinite(su.C))
                    throw new ArgumentException($"선수 {su.Id} 초기 상태에 NaN/무한대", nameof(players));
                _ids[i] = su.Id;
                _prof[i] = PlayerMotionProfile.Create(in su.Attributes, p);
                var speed = su.Velocity.Length();
                // 몸 방향: 움직이고 있으면 속도 방향 (피벗 시작에서 속도가 순간 회전하지 않게), 아니면 입력 방향, 그것도 0이면 +x (리뷰 R3)
                var facing = speed > 1e-4f ? su.Velocity / speed : su.Facing.Normalized();
                if (facing == Vec2.Zero) facing = new Vec2(1f, 0f);
                _s[i] = new PlayerMotionState
                {
                    Id = su.Id,
                    Position = su.Position,
                    Velocity = su.Velocity,
                    Facing = facing,
                    Pivoting = speed < p.PivotExit,
                    W = Clamp01(su.W),
                    C = Clamp01(su.C),
                    Active = SteeringCommand.Idle(),
                };
            }
            _queue = new CommandQueue(_n);
            _contact = new bool[_n];
            _snapPos = new Vec2[_n];
            _snapVel = new Vec2[_n];
            _push = new Vec2[_n];
            _desired = new Vec2[_n];
            _faultVel = new Vec2[_n];
            _fault = new bool[_n];
            _sepCorr = new float[_n];
            _info = new SolveInfo[_n];
            _targetIdx = new int[_n];
            for (int i = 0; i < _n; i++) _targetIdx[i] = -1;
            _sep = new Separation(_n < 2 ? 2 : _n, p);
        }

        /// <summary>id → 인덱스 (id 오름차순 이진 탐색). 없으면 −1.</summary>
        public int IndexOf(int id) => Array.BinarySearch(_ids, id) is var k && k >= 0 ? k : -1;

        public ref readonly PlayerMotionState State(int index) => ref _s[index];
        public ref readonly PlayerMotionProfile Profile(int index) => ref _prof[index];
        public ref readonly MotionStats Stats(int index) => ref _stats[index];
        public int PendingCommands(int index) => _queue.Pending(index);

        /// <summary>결정 틱 명령. 반응 지연(인지 + 운동 개시 + 피로 가산) 뒤에 활성화 (FR-06·28).</summary>
        public void SetCommand(int id, in SteeringCommand cmd)
        {
            var i = Require(id);
            RequireFinite(in cmd);
            var delay = Fatigue.ReactionDelay(in _prof[i], _s[i].W, _p);
            var steps = (long)DMath.Floor(delay / _p.Dt + 0.5f);
            if (_queue.Enqueue(i, in cmd, Substep + steps)) Diagnostics?.OnCommandDropped(Substep, id);
        }

        /// <summary>지연 없이 바로 적용 (킥오프·재시작 배치, 테스트). 대기 중 명령은 버린다.</summary>
        public void SetCommandImmediate(int id, in SteeringCommand cmd)
        {
            var i = Require(id);
            RequireFinite(in cmd);
            _queue.Clear(i);
            _s[i].Active = cmd;
            _targetIdx[i] = IndexOf(cmd.TargetId);
        }

        /// <summary>접촉 (008이 부른다, FR-04): 속도 × (1 − 손실), 다음 서브스텝 a_cap × 초과 허용 배율.</summary>
        public void ApplyContact(int id)
        {
            var i = Require(id);
            _s[i].Velocity *= 1f - _prof[i].ContactLoss;
            _contact[i] = true;
        }

        /// <summary>
        /// 검증 전용 (FR-52 자가 검사): 다음 서브스텝 적분 결과 속도를 덮어써 "한계를 깨는 시뮬"을 흉내 낸다.
        /// 모니터가 그 서브스텝·선수를 보고해야 한다. 경기 코드는 부르지 않는다.
        /// </summary>
        public void InjectVelocityFault(int id, Vec2 velocity)
        {
            var i = Require(id);
            _fault[i] = true;
            _faultVel[i] = velocity;
        }

        public void Step()
        {
            var p = _p;
            var n = _n;
            var substep = Substep;

            // ① 명령 활성화 + 스냅숏
            for (int i = 0; i < n; i++)
            {
                if (_queue.Activate(i, substep, ref _s[i].Active)) _targetIdx[i] = IndexOf(_s[i].Active.TargetId);
                _snapPos[i] = _s[i].Position;
                _snapVel[i] = _s[i].Velocity;
                _sepCorr[i] = 0f;
            }

            // ③ Separation (스냅숏)
            _sep.Push(_snapPos, n, _push, p);

            var monitor = Monitor;
            var diag = Diagnostics;
            var vA = p.VmaxAcute; var vC = p.VmaxChronic; var aA = p.AccAcute; var aC = p.AccChronic;
            for (int i = 0; i < n; i++)
            {
                ref var s = ref _s[i];
                ref readonly var prof = ref _prof[i];
                var fatigueW = 1f - s.W;
                var vmaxEff = prof.MaxSpeed * (1f - vA * fatigueW - vC * s.C);   // Fatigue.EffectiveMaxSpeed 인라인 (핫패스)
                var a0Eff = prof.MaxAccel * (1f - aA * fatigueW - aC * s.C);     // Fatigue.EffectiveMaxAccel 인라인

                // ② 행동 ④ 합성 — 흔한 행동(Seek·Arrive·Intercept·대형·Idle)은 스칼라 경로, 대상 선수 행동은 Steering(Vec2)
                ref readonly var cmd = ref s.Active;
                var maxSpeed = cmd.MaxSpeed > 0f && cmd.MaxSpeed < vmaxEff ? cmd.MaxSpeed : vmaxEff;
                float bx, by;
                var px = _snapPos[i].X; var py = _snapPos[i].Y;
                var vx = _snapVel[i].X; var vy = _snapVel[i].Y;
                switch (cmd.Kind)
                {
                    case SteeringKind.Idle:
                        bx = 0f; by = 0f;
                        break;
                    case SteeringKind.Seek:
                        SeekXY(px, py, cmd.Point, maxSpeed, p, out bx, out by);
                        break;
                    case SteeringKind.InterceptBall:
                        // 공 인터셉트는 TTI와 같은 가정(전력 질주로 통과)으로 달리고, 컨트롤 반경 안에서만 Arrive로 멈춘다 (FR-41 같은 함수 원칙)
                        if (DistSqClamped(px, py, cmd.Point, p) > p.ControlRadius * p.ControlRadius) SeekXY(px, py, cmd.Point, maxSpeed, p, out bx, out by);
                        else ArriveXY(px, py, vx, vy, cmd.Point, maxSpeed, prof.MaxDecel, p, out bx, out by);
                        break;
                    case SteeringKind.Arrive:
                    case SteeringKind.Formation:
                        ArriveXY(px, py, vx, vy, cmd.Point, maxSpeed, prof.MaxDecel, p, out bx, out by);
                        break;
                    default:
                    {
                        var b = TargetBehavior(i, in cmd, maxSpeed);
                        bx = b.X; by = b.Y;
                        break;
                    }
                }

                // FR-27 우선순위 합성 (SteeringCombiner.Combine 스칼라 인라인): 충돌 회피 > 행동 > 대형
                var sx = _push[i].X * p.WSeparation;
                var sy = _push[i].Y * p.WSeparation;
                float dx, dy, rem = vmaxEff;
                if (sx == 0f && sy == 0f) { dx = 0f; dy = 0f; }
                else
                {
                    var sl = DMath.Sqrt(sx * sx + sy * sy);
                    if (sl > rem) { var k = rem / sl; sx *= k; sy *= k; sl = rem; }
                    dx = sx; dy = sy; rem -= sl;
                }
                if (rem > 0f)
                {
                    bx *= p.WBehavior; by *= p.WBehavior;
                    var bl2 = bx * bx + by * by;
                    var bl = 0f;
                    if (bl2 > 0f)
                    {
                        bl = DMath.Sqrt(bl2);
                        if (bl > rem) { var k = rem / bl; bx *= k; by *= k; bl = rem; }
                        dx += bx; dy += by;
                    }
                    rem -= bl;
                    if (cmd.HasFormation && rem > 0f)
                    {
                        ArriveXY(px, py, vx, vy, cmd.FormationPoint, maxSpeed, prof.MaxDecel, p, out var fx, out var fy);
                        fx *= p.WFormation; fy *= p.WFormation;
                        var fl2 = fx * fx + fy * fy;
                        if (fl2 > rem * rem) { var k = rem / DMath.Sqrt(fl2); fx *= k; fy *= k; }
                        dx += fx; dy += fy;
                    }
                }
                var desired = new Vec2(dx, dy);
                if (cmd.Kind == SteeringKind.OffsetArrive && _targetIdx[i] >= 0 && _targetIdx[i] != i)
                    desired = Steering.LimitBackpedal(desired, _snapPos[_targetIdx[i]], cmd.Point, p); // 밀어내기를 더해도 백페달 ≤ 4 m/s (FR-26)
                _desired[i] = desired;

                // ⑤ 한계 풀이·적분
                var capScale = _contact[i] ? prof.ContactCapScale : 1f;
                var vBefore = s.Velocity;
                var fBefore = s.Facing;
                MotionSolver.Step(ref s, in prof, desired, vmaxEff, a0Eff, capScale, p, out _info[i]);
                if (_fault[i])
                {
                    var pos0 = s.Position - s.Velocity * p.Dt;
                    s.Velocity = _faultVel[i];
                    s.Position = pos0 + s.Velocity * p.Dt;
                    _fault[i] = false;
                }
                monitor?.Check(substep, s.Id, s.Pivoting, vBefore, s.Velocity, fBefore, s.Facing, in prof, vmaxEff, a0Eff, capScale, p.Dt);
                _contact[i] = false;
            }

            // ⑥ 겹침 위치 투영 ⑦ 피치 경계
            _sep.Project(_s, _sepCorr, p);
            for (int i = 0; i < n; i++)
            {
                var pos = _s[i].Position;
                var x = pos.X < p.MinX ? p.MinX : (pos.X > p.MaxX ? p.MaxX : pos.X);
                var y = pos.Y < p.MinY ? p.MinY : (pos.Y > p.MaxY ? p.MaxY : pos.Y);
                _s[i].Position = new Vec2(x, y);
            }

            // ⑧ 피로 ⑨ 기록·진단 ⑩ 수치 검사
            for (int i = 0; i < n; i++)
            {
                ref var s = ref _s[i];
                var v = DMath.Sqrt(s.Velocity.X * s.Velocity.X + s.Velocity.Y * s.Velocity.Y);
                var sv = _snapVel[i];
                var aLong = (v - DMath.Sqrt(sv.X * sv.X + sv.Y * sv.Y)) / p.Dt;
                Fatigue.Step(ref s.W, ref s.C, v, in _prof[i], p);
                _stats[i].Update(v, aLong, p);
                if (diag != null)
                {
                    var info = _info[i];
                    diag.OnPlayerSubstep(new KinematicsSample(substep, s.Id, _desired[i], s.Velocity, info.LongAccel, info.LatAccel,
                        info.FrictionUse, s.Pivoting, info.PivotTurn / p.Dt, s.W, s.C, _queue.Pending(i), _sepCorr[i]));
                }
                if (!Finite(in s)) throw new NumericException($"운동 상태에 NaN/무한대: 선수 {s.Id}, 서브스텝 {substep}");
            }

            Substep = substep + 1;
        }

        /// <summary>대상 선수가 있는 행동 (Pursue·Evade·마킹·저킹). 대상 없음 → 정지.</summary>
        Vec2 TargetBehavior(int i, in SteeringCommand cmd, float maxSpeed)
        {
            var t = _targetIdx[i];
            if (t < 0 || t == i) return Vec2.Zero;
            var p = _p;
            var pos = _snapPos[i];
            var vel = _snapVel[i];
            var tp = _snapPos[t];
            var tv = _snapVel[t];
            switch (cmd.Kind)
            {
                case SteeringKind.PursuePlayer: return Steering.Pursue(pos, vel, tp, tv, maxSpeed, p);
                case SteeringKind.Evade: return Steering.Evade(pos, vel, tp, tv, maxSpeed, p);
                case SteeringKind.OffsetPursuit: return Steering.Mark(pos, tp, tv, cmd.Point, maxSpeed, p);
                case SteeringKind.OffsetArrive: return Steering.Jockey(pos, tp, tv, cmd.Point, cmd.Side, maxSpeed, p);
                default: return Vec2.Zero;
            }
        }

        static float ClampX(float x, KinematicsParams p) => x < p.MinX ? p.MinX : (x > p.MaxX ? p.MaxX : x);
        static float ClampY(float y, KinematicsParams p) => y < p.MinY ? p.MinY : (y > p.MaxY ? p.MaxY : y);

        static float DistSqClamped(float px, float py, Vec2 target, KinematicsParams p)
        {
            var ex = ClampX(target.X, p) - px;
            var ey = ClampY(target.Y, p) - py;
            return ex * ex + ey * ey;
        }

        /// <summary><see cref="Steering.Seek"/>의 스칼라판 (목표는 피치 경계로 자른다).</summary>
        static void SeekXY(float px, float py, Vec2 target, float maxSpeed, KinematicsParams p, out float x, out float y)
        {
            var ex = ClampX(target.X, p) - px;
            var ey = ClampY(target.Y, p) - py;
            var d = DMath.Sqrt(ex * ex + ey * ey);
            if (d < 1e-4f) { x = 0f; y = 0f; return; }
            var k = maxSpeed / d;
            x = ex * k; y = ey * k;
        }

        /// <summary><see cref="Steering.Arrive"/>의 스칼라판 (FR-20).</summary>
        static void ArriveXY(float px, float py, float vx, float vy, Vec2 target, float maxSpeed, float maxDecel, KinematicsParams p, out float x, out float y)
        {
            var ex = ClampX(target.X, p) - px;
            var ey = ClampY(target.Y, p) - py;
            var d = DMath.Sqrt(ex * ex + ey * ey);
            if (d < 1e-4f || (d <= p.ArriveTolerance && vx * vx + vy * vy <= p.ArriveStopSpeed * p.ArriveStopSpeed)) { x = 0f; y = 0f; return; }
            var v = DMath.Sqrt(2f * p.ArriveDecelFactor * maxDecel * d);
            if (v > maxSpeed) v = maxSpeed;
            var k = v / d;
            x = ex * k; y = ey * k;
        }

        /// <summary>전체 상태 해시 (선수 id 순: 상태·대기 명령·기록 + 서브스텝).</summary>
        public void AddTo(ref StableHasher h)
        {
            h.Add(Substep);
            h.Add(_n);
            for (int i = 0; i < _n; i++)
            {
                _s[i].AddTo(ref h);
                h.Add(_contact[i]); // 다음 서브스텝 a_cap 배율에 영향 (리뷰 R1)
                h.Add(_fault[i]);
                if (_fault[i]) { h.Add(_faultVel[i].X); h.Add(_faultVel[i].Y); }
                _queue.AddTo(i, ref h);
                _stats[i].AddTo(ref h);
            }
        }

        int Require(int id)
        {
            var i = IndexOf(id);
            if (i < 0) throw new ArgumentException($"없는 선수 id: {id}", nameof(id));
            return i;
        }

        static void RequireFinite(in SteeringCommand c)
        {
            if (!Finite(c.Point) || !Finite(c.FormationPoint) || !FloatGuard.IsFinite(c.MaxSpeed) || !FloatGuard.IsFinite(c.Side))
                throw new ArgumentException("명령에 NaN/무한대 (리뷰 R4: 입력 단계에서 거부)", nameof(c));
        }

        static bool Finite(Vec2 v) => FloatGuard.IsFinite(v.X) && FloatGuard.IsFinite(v.Y);

        static bool Finite(in PlayerMotionState s) =>
            FloatGuard.IsFinite(s.Position.X) && FloatGuard.IsFinite(s.Position.Y)
            && FloatGuard.IsFinite(s.Velocity.X) && FloatGuard.IsFinite(s.Velocity.Y)
            && FloatGuard.IsFinite(s.W) && FloatGuard.IsFinite(s.C);

        static float Clamp01(float x) => x < 0f ? 0f : (x > 1f ? 1f : (x >= 0f ? x : 0f));
    }
}
