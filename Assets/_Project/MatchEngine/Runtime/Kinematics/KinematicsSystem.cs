using System;
using RoadToPro.Core.Hashing;
using RoadToPro.Core.Math;
using RoadToPro.Core.Serialization;

namespace RoadToPro.MatchEngine.Kinematics
{
    /// <summary>
    /// 선수 운동 시스템 (specs/005). 선수 상태·프로필·지연 큐·기록을 소유하고 서브스텝(100 Hz)을 진행한다.
    /// 처리 순서는 생성 시 id 오름차순으로 고정 (입력 순서 무관, AC-12). 서브스텝 경로 할당 0.
    /// 서브스텝 순서: ① 명령 활성화 ② 행동 목표 속도(스냅숏 기준) ③ Separation ④ 합성 ⑤ 한계 풀이·적분
    /// ⑥ 겹침 위치 투영 ⑦ 피치 경계 ⑧ 피로 ⑨ 기록·진단·모니터 ⑩ 수치 검사.
    /// 006 추가: 비활성 선수(퇴장·교체 대기)는 적분·Separation·기록에서 빠지고 상태가 얼어 있다. 슬롯 재설정(교체 투입)과
    /// 상태 직렬화(스냅샷). 전원 활성이면 005와 계산·해시가 같다.
    /// </summary>
    public sealed class KinematicsSystem
    {
        public const int MaxPlayers = 32;

        readonly KinematicsParams _p;
        readonly int _n;
        readonly int[] _ids;
        readonly PlayerMotionState[] _s;
        readonly PlayerMotionProfile[] _prof;
        readonly KinematicAttributes[] _attrs;
        readonly bool[] _active;
        int _inactive;
        readonly bool[] _ext;
        readonly Vec2[] _extVel;
        int _external;
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
            _attrs = new KinematicAttributes[_n];
            _active = new bool[_n];
            _ext = new bool[_n];
            _extVel = new Vec2[_n];
            for (int i = 0; i < _n; i++)
            {
                if (i > 0 && sorted[i].Id == sorted[i - 1].Id) throw new ArgumentException($"선수 id 중복: {sorted[i].Id}", nameof(players));
                var su = sorted[i];
                if (!Finite(su.Position) || !Finite(su.Velocity) || !FloatGuard.IsFinite(su.W) || !FloatGuard.IsFinite(su.C))
                    throw new ArgumentException($"선수 {su.Id} 초기 상태에 NaN/무한대", nameof(players));
                _ids[i] = su.Id;
                _attrs[i] = su.Attributes;
                _active[i] = true;
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
        public bool IsActive(int index) => _active[index];
        /// <summary>외부 구동 중인가 (specs/011 FR-17 GK 다이빙).</summary>
        public bool IsExternal(int index) => _ext[index];
        public ref readonly KinematicAttributes Attributes(int index) => ref _attrs[index];

        /// <summary>
        /// 활성 여부 (specs/006: 퇴장·교체 대기). 비활성이 되면 속도 0·대기 명령 비움·Idle이고, 적분·Separation·기록·경계에서 빠진다.
        /// 다른 선수의 대상(Pursue 등)이면 대상 없음으로 본다.
        /// </summary>
        public void SetActive(int id, bool active)
        {
            var i = Require(id);
            if (_active[i] == active) return;
            _active[i] = active;
            _inactive += active ? -1 : 1;
            if (!active)
            {
                if (_ext[i]) { _ext[i] = false; _external--; _extVel[i] = Vec2.Zero; }
                Freeze(i);
            }
        }

        /// <summary>
        /// 슬롯에 새 선수를 넣는다 (specs/006 교체 투입): 프로필 재계산, W = 1·C = 0 (005 FR-35), 정지, 기록 0, 활성.
        /// 슬롯 id는 그대로라 처리 순서가 바뀌지 않는다.
        /// </summary>
        public void ResetSlot(int id, in KinematicAttributes attributes, Vec2 position, Vec2 facing)
        {
            var i = Require(id);
            if (!Finite(position)) throw new ArgumentException("위치에 NaN/무한대", nameof(position));
            _attrs[i] = attributes;
            _prof[i] = PlayerMotionProfile.Create(in attributes, _p);
            var f = facing.Normalized();
            if (f == Vec2.Zero) f = new Vec2(1f, 0f);
            _s[i] = new PlayerMotionState { Id = id, Position = position, Velocity = Vec2.Zero, Facing = f, Pivoting = true, W = 1f, C = 0f, Active = SteeringCommand.Idle() };
            _stats[i] = default;
            _queue.Clear(i);
            _contact[i] = false;
            _fault[i] = false;
            _targetIdx[i] = -1;
            if (_ext[i]) { _ext[i] = false; _external--; _extVel[i] = Vec2.Zero; }
            if (!_active[i]) { _active[i] = true; _inactive--; }
        }

        /// <summary>
        /// 외부 구동 시작 (specs/011 FR-17: GK 다이빙은 005 달리기 모델 밖). 이 선수는 서브스텝마다 <see cref="DriveExternal"/> 속도로만
        /// 움직이고, 조향·한계 풀이·운동 한계 모니터(FR-52)에서 빠진다. 겹침 위치 투영(Separation ⑥)·피치 경계·피로·기록은 그대로 —
        /// 투영을 빼면 다이빙 중 쌓인 겹침이 해제 순간 한 번에 풀려 순간이동처럼 보인다(058 감사기). 대기 명령은 비운다.
        /// </summary>
        public void BeginExternal(int id, Vec2 velocity)
        {
            var i = Require(id);
            if (!Finite(velocity)) throw new ArgumentException("속도에 NaN/무한대", nameof(velocity));
            if (!_ext[i]) { _ext[i] = true; _external++; }
            _extVel[i] = velocity;
            _queue.Clear(i);
            _s[i].Active = SteeringCommand.Idle();
            _targetIdx[i] = -1;
        }

        /// <summary>외부 구동 속도 바꾸기 (착지 뒤 0 등).</summary>
        public void DriveExternal(int id, Vec2 velocity)
        {
            var i = Require(id);
            if (!_ext[i]) throw new InvalidOperationException($"외부 구동 중이 아님: {id}");
            if (!Finite(velocity)) throw new ArgumentException("속도에 NaN/무한대", nameof(velocity));
            _extVel[i] = velocity;
        }

        /// <summary>외부 구동 끝: 정지·Idle·대기 명령 비움으로 005에 돌려준다(일어나기 끝).</summary>
        public void EndExternal(int id)
        {
            var i = Require(id);
            if (!_ext[i]) return;
            _ext[i] = false;
            _external--;
            _extVel[i] = Vec2.Zero;
            Freeze(i);
        }

        /// <summary>
        /// 선수를 다른 슬롯으로 옮긴다 (specs/011: GK 퇴장 뒤 필드 선수가 GK 슬롯을 맡음). 능력치·상태·피로·기록을 그대로 옮기고
        /// 원래 슬롯은 비활성. 대상 슬롯은 비활성이어야 한다. 대기 명령은 비운다(다음 결정에서 다시).
        /// </summary>
        public void MoveSlot(int fromId, int toId)
        {
            var f = Require(fromId);
            var t = Require(toId);
            if (_active[t]) throw new InvalidOperationException($"대상 슬롯 {toId}가 활성");
            if (!_active[f]) throw new InvalidOperationException($"원래 슬롯 {fromId}가 비활성");
            _attrs[t] = _attrs[f];
            _prof[t] = _prof[f];
            var s = _s[f];
            s.Id = toId;
            s.Active = SteeringCommand.Idle();
            _s[t] = s;
            _stats[t] = _stats[f];
            _queue.Clear(t);
            _contact[t] = false;
            _fault[t] = false;
            _targetIdx[t] = -1;
            if (_ext[t]) { _ext[t] = false; _external--; _extVel[t] = Vec2.Zero; }
            _active[t] = true;
            _inactive--;
            SetActive(fromId, false);
        }

        /// <summary>즉시 배치 (킥오프·재시작·하프타임): 위치·방향을 놓고 정지·Idle. 피로·기록은 유지.</summary>
        public void Place(int id, Vec2 position, Vec2 facing)
        {
            var i = Require(id);
            if (!Finite(position)) throw new ArgumentException("위치에 NaN/무한대", nameof(position));
            var f = facing.Normalized();
            if (f == Vec2.Zero) f = _s[i].Facing;
            _s[i].Position = position;
            _s[i].Facing = f;
            if (_ext[i]) { _ext[i] = false; _external--; _extVel[i] = Vec2.Zero; }
            Freeze(i);
        }

        void Freeze(int i)
        {
            _s[i].Velocity = Vec2.Zero;
            _s[i].Pivoting = true;
            _s[i].Active = SteeringCommand.Idle();
            _queue.Clear(i);
            _targetIdx[i] = -1;
            _contact[i] = false;
            _fault[i] = false;
        }

        /// <summary>상태 직렬화 (specs/006 FR-60): 서브스텝, 선수별 능력치·활성·상태·접촉·결함·대기 명령·기록. 프로필은 능력치에서 다시 계산한다.</summary>
        public void WriteState(StateWriter w)
        {
            w.Write(Substep);
            w.Write(_n);
            for (int i = 0; i < _n; i++)
            {
                w.Write(_ids[i]);
                ref readonly var a = ref _attrs[i];
                w.Write(a.Pace); w.Write(a.Acceleration); w.Write(a.Agility); w.Write(a.Balance);
                w.Write(a.Stamina); w.Write(a.Anticipation); w.Write(a.Concentration);
                w.Write(_active[i]);
                w.Write(_ext[i]);
                w.Write(_extVel[i]);
                ref readonly var s = ref _s[i];
                w.Write(s.Position); w.Write(s.Velocity); w.Write(s.Facing);
                w.Write(s.Pivoting); w.Write(s.W); w.Write(s.C);
                s.Active.Write(w);
                w.Write(_contact[i]);
                w.Write(_fault[i]);
                w.Write(_faultVel[i]);
                _queue.Write(i, w);
                _stats[i].Write(w);
            }
        }

        public void ReadState(StateReader r)
        {
            var substep = r.ReadLong();
            var n = r.ReadInt();
            if (n != _n) throw new FormatException($"선수 수 불일치: 스냅샷 {n}, 시스템 {_n}");
            var inactive = 0;
            var external = 0;
            for (int i = 0; i < _n; i++)
            {
                var id = r.ReadInt();
                if (id != _ids[i]) throw new FormatException($"선수 id 불일치: 스냅샷 {id}, 시스템 {_ids[i]}");
                var a = new KinematicAttributes(r.ReadFloat(), r.ReadFloat(), r.ReadFloat(), r.ReadFloat(), r.ReadFloat(), r.ReadFloat(), r.ReadFloat());
                _attrs[i] = a;
                _prof[i] = PlayerMotionProfile.Create(in a, _p); // 같은 능력치 → 같은 프로필 (순수 함수)
                _active[i] = r.ReadBool();
                if (!_active[i]) inactive++;
                _ext[i] = r.ReadBool();
                _extVel[i] = r.ReadVec2();
                if (_ext[i]) external++;
                var s = new PlayerMotionState { Id = id };
                s.Position = r.ReadVec2(); s.Velocity = r.ReadVec2(); s.Facing = r.ReadVec2();
                s.Pivoting = r.ReadBool(); s.W = r.ReadFloat(); s.C = r.ReadFloat();
                s.Active = SteeringCommand.Read(r);
                _s[i] = s;
                _contact[i] = r.ReadBool();
                _fault[i] = r.ReadBool();
                _faultVel[i] = r.ReadVec2();
                _queue.Read(i, r);
                _stats[i] = MotionStats.Read(r);
            }
            for (int i = 0; i < _n; i++) _targetIdx[i] = IndexOf(_s[i].Active.TargetId);
            _inactive = inactive;
            _external = external;
            Substep = substep;
        }

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
            if (_inactive > 0)
                for (int i = 0; i < n; i++)
                    if (!_active[i]) _snapPos[i] = new Vec2(-1e4f - 10f * i, -1e4f); // 서로·활성 선수와 멀리: Separation 후보에서 빠진다

            // ③ Separation (스냅숏)
            _sep.Push(_snapPos, n, _push, p);

            var monitor = Monitor;
            var diag = Diagnostics;
            var vA = p.VmaxAcute; var vC = p.VmaxChronic; var aA = p.AccAcute; var aC = p.AccChronic;
            var anyInactive = _inactive > 0;
            for (int i = 0; i < n; i++)
            {
                if (anyInactive && !_active[i]) continue;
                ref var s = ref _s[i];
                if (_external > 0 && _ext[i])
                {
                    // 011 FR-17: 외부 구동 — 조향·한계 풀이·모니터 없이 정해진 속도로 적분
                    s.Velocity = _extVel[i];
                    s.Position += s.Velocity * p.Dt;
                    _desired[i] = s.Velocity;
                    _info[i] = default;
                    _contact[i] = false;
                    continue;
                }
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
                if (cmd.Kind == SteeringKind.OffsetArrive && _targetIdx[i] >= 0 && _targetIdx[i] != i && _active[_targetIdx[i]])
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
                if (anyInactive && !_active[i]) continue;
                var pos = _s[i].Position;
                var x = pos.X < p.MinX ? p.MinX : (pos.X > p.MaxX ? p.MaxX : pos.X);
                var y = pos.Y < p.MinY ? p.MinY : (pos.Y > p.MaxY ? p.MaxY : pos.Y);
                _s[i].Position = new Vec2(x, y);
            }

            // ⑧ 피로 ⑨ 기록·진단 ⑩ 수치 검사
            for (int i = 0; i < n; i++)
            {
                if (anyInactive && !_active[i]) continue;
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
            if (t < 0 || t == i || !_active[t]) return Vec2.Zero;
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
                if (!_active[i]) h.Add((byte)0xA5); // 비활성 표시 (전원 활성이면 005 해시 그대로)
                if (_ext[i]) { h.Add((byte)0xE7); h.Add(_extVel[i].X); h.Add(_extVel[i].Y); } // 외부 구동 표시 (안 쓰면 해시 그대로)
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
