using System;
using RoadToPro.Core.Math;
using RoadToPro.MatchEngine.Ball;

namespace RoadToPro.MatchEngine.Match.Replay
{
    /// <summary>
    /// 30 Hz 리플레이 기록 (specs/006 FR-50). 프레임 k의 시각은 k/30 s(절대). 절대 서브스텝 s(시각 s/100 → (s+1)/100)를 처리하면
    /// 3s &lt; 10k ≤ 3(s+1)인 k마다 서브스텝 직전·직후 상태를 α = (10k − 3s)/3로 보간한다. 정수 판정이라 어느 체크포인트에서
    /// 다시 돌려도 같은 프레임이 비트 단위로 나온다 (AC-16). 최근 60초는 링 버퍼(1,800프레임)에 남는다.
    /// 동작 힌트는 물리 단계 안에서만 표시하고 틱 끝에 지운다 — 틱을 넘는 상태가 없어 스냅샷에 넣지 않는다 (리뷰 R5). 링은 복원 때 비운다.
    /// </summary>
    public sealed class ReplayRecorder
    {
        public const int FramesPerSecond = 30;
        public const int RingSeconds = 60;
        public const int RingCapacity = FramesPerSecond * RingSeconds;

        readonly MatchContext _ctx;
        readonly ReplayFrame _pre = new ReplayFrame();
        readonly ReplayFrame _post = new ReplayFrame();
        readonly ReplayFrame _tmp = new ReplayFrame();
        readonly MotionHint[] _action = new MotionHint[Squad.Slots];
        readonly bool[] _hasAction = new bool[Squad.Slots];

        readonly float[] _values = new float[RingCapacity * ReplayFrame.Entities * ReplayFrame.Stride];
        readonly MotionHint[] _hints = new MotionHint[RingCapacity * ReplayFrame.Entities];
        readonly int[] _ids = new int[RingCapacity * Squad.Slots];
        readonly long[] _index = new long[RingCapacity];
        readonly BallPhase[] _ballPhase = new BallPhase[RingCapacity];
        readonly int[] _owner = new int[RingCapacity];
        int _head, _count;

        /// <summary>프레임마다 호출 (클립 추출·검증). null이면 호출 0.</summary>
        public IReplayFrameSink Sink;
        public bool RingEnabled = true;

        internal ReplayRecorder(MatchContext ctx) => _ctx = ctx;

        public int RingCount => _count;
        public long NewestIndex => _count == 0 ? -1 : _index[(_head - 1 + RingCapacity) % RingCapacity];
        public long OldestIndex => _count == 0 ? -1 : _index[(_head - _count + RingCapacity) % RingCapacity];

        /// <summary>링 버퍼 메모리 (바이트, 성능 예산 "≤ 4 MB").</summary>
        public long RingBytes => (long)_values.Length * 4 + _hints.Length + (long)_ids.Length * 4 + (long)_index.Length * 8 + _ballPhase.Length + (long)_owner.Length * 4;

        public void ClearRing()
        {
            _head = 0;
            _count = 0;
            EndTick();
        }

        /// <summary>틱 끝: 남은 동작 힌트를 지운다 (프레임 없이 끝난 서브스텝의 표시가 다음 틱으로 넘어가지 않게).</summary>
        internal void EndTick() => System.Array.Clear(_hasAction, 0, _hasAction.Length);

        /// <summary>링에 있는 절대 프레임 index를 dst로 복사. 없으면 false.</summary>
        public bool TryGetFrame(long index, ReplayFrame dst)
        {
            if (_count == 0 || index < OldestIndex || index > NewestIndex) return false;
            var slot = (int)((_head - 1 - (NewestIndex - index) + RingCapacity * 2L) % RingCapacity);
            dst.Index = _index[slot];
            Array.Copy(_values, slot * ReplayFrame.Entities * ReplayFrame.Stride, dst.Values, 0, ReplayFrame.Entities * ReplayFrame.Stride);
            Array.Copy(_hints, slot * ReplayFrame.Entities, dst.Hints, 0, ReplayFrame.Entities);
            Array.Copy(_ids, slot * Squad.Slots, dst.PlayerIds, 0, Squad.Slots);
            dst.BallPhase = _ballPhase[slot];
            dst.BallOwnerSlot = _owner[slot];
            return true;
        }

        /// <summary>이번 서브스텝의 동작 힌트 (킥·태클 등). 다음 프레임에 반영되고 지워진다.</summary>
        public void MarkAction(int slot, MotionHint h)
        {
            if ((uint)slot >= Squad.Slots) return;
            _action[slot] = h;
            _hasAction[slot] = true;
        }

        readonly ReplayFrame _initial = new ReplayFrame();

        /// <summary>프레임 0 (경기 시작 상태, 시각 0 s).</summary>
        public ReplayFrame InitialFrame => _initial;

        /// <summary>시작 상태를 프레임 0으로 기록한다 (경기 생성·틱 0 복원 때).</summary>
        internal void EmitInitialFrame()
        {
            Capture(_initial, 0);
            _initial.Index = 0;
            if (RingEnabled) Store(_initial);
            Sink?.OnFrame(_initial);
        }

        internal void CapturePre(long substep)
        {
            Capture(_pre, substep);
        }

        internal void AfterSubstep(long substep)
        {
            Capture(_post, substep + 1);
            var raw = _ctx.RawSink;
            if (raw != null) raw.OnRawSubstep(substep, _pre, _post);
            var s3 = substep * 3;
            var kFrom = s3 / 10 + 1;
            var kTo = (s3 + 3) / 10;
            for (var k = kFrom; k <= kTo; k++)
            {
                var alpha = (10 * k - s3) / 3f;
                Lerp(_pre, _post, alpha, _tmp);
                _tmp.Index = k;
                for (int i = 0; i < Squad.Slots; i++)
                {
                    if (!_hasAction[i] || _tmp.Hints[i] == MotionHint.Absent) continue;
                    _tmp.Hints[i] = _action[i];
                    _hasAction[i] = false;
                }
                if (RingEnabled) Store(_tmp);
                Sink?.OnFrame(_tmp);
            }
        }

        void Capture(ReplayFrame f, long index)
        {
            f.Index = index;
            var v = f.Values;
            var k = _ctx.Kinematics;
            var p = _ctx.KinParams;
            for (int s = 0; s < Squad.Slots; s++)
            {
                var o = s * ReplayFrame.Stride;
                f.PlayerIds[s] = _ctx.Squad.PlayerAt(s);
                var leaving = _ctx.Flow.Walkers.LeavingIdAt(s); // 058 FR-65: 인원에서 빠졌지만 걸어 나가는 중
                if (leaving >= 0 && k.IsActive(s)) f.PlayerIds[s] = leaving;
                else if (!_ctx.IsSlotActive(s))
                {
                    v[o] = 0f; v[o + 1] = 0f; v[o + 2] = 0f; v[o + 3] = 0f;
                    f.Hints[s] = MotionHint.Absent;
                    continue;
                }
                ref readonly var st = ref k.State(s);
                v[o] = st.Position.X;
                v[o + 1] = st.Position.Y;
                v[o + 2] = 0f;
                v[o + 3] = DMath.Atan2(st.Facing.Y, st.Facing.X);
                var sp = st.Velocity.Length();
                f.Hints[s] = sp < 0.3f ? MotionHint.Idle : sp < 2f ? MotionHint.Walk : sp < 4f ? MotionHint.Jog : sp < p.SprintSpeed ? MotionHint.Run : MotionHint.Sprint;
            }
            ref readonly var b = ref _ctx.Ball;
            var bo = ReplayFrame.BallEntity * ReplayFrame.Stride;
            v[bo] = b.Position.X; v[bo + 1] = b.Position.Y; v[bo + 2] = b.Position.Z; v[bo + 3] = 0f;
            f.Hints[ReplayFrame.BallEntity] = MotionHint.Idle;
            f.BallPhase = b.Phase;
            f.BallOwnerSlot = b.Phase == BallPhase.Controlled ? b.OwnerId : -1;
            for (int i = 0; i < Officials.Count; i++)
            {
                var e = (ReplayFrame.FirstOfficial + i) * ReplayFrame.Stride;
                var pos = _ctx.Officials.Position(i);
                var fc = _ctx.Officials.Facing(i);
                v[e] = pos.X; v[e + 1] = pos.Y; v[e + 2] = 0f; v[e + 3] = DMath.Atan2(fc.Y, fc.X);
                f.Hints[ReplayFrame.FirstOfficial + i] = MotionHint.Idle;
            }
        }

        /// <summary>a→b 보간 (각도는 짧은 쪽). 힌트·id·공 상태는 b.</summary>
        public static void Lerp(ReplayFrame a, ReplayFrame b, float t, ReplayFrame dst)
        {
            var va = a.Values; var vb = b.Values; var vd = dst.Values;
            for (int e = 0; e < ReplayFrame.Entities; e++)
            {
                var o = e * ReplayFrame.Stride;
                vd[o] = va[o] + (vb[o] - va[o]) * t;
                vd[o + 1] = va[o + 1] + (vb[o + 1] - va[o + 1]) * t;
                vd[o + 2] = va[o + 2] + (vb[o + 2] - va[o + 2]) * t;
                var d = vb[o + 3] - va[o + 3];
                if (d > DMath.Pi) d -= 2f * DMath.Pi;
                else if (d < -DMath.Pi) d += 2f * DMath.Pi;
                vd[o + 3] = va[o + 3] + d * t;
            }
            Array.Copy(b.Hints, dst.Hints, ReplayFrame.Entities);
            Array.Copy(b.PlayerIds, dst.PlayerIds, Squad.Slots);
            dst.BallPhase = b.BallPhase;
            dst.BallOwnerSlot = b.BallOwnerSlot;
        }

        void Store(ReplayFrame f)
        {
            var slot = _head;
            _index[slot] = f.Index;
            Array.Copy(f.Values, 0, _values, slot * ReplayFrame.Entities * ReplayFrame.Stride, ReplayFrame.Entities * ReplayFrame.Stride);
            Array.Copy(f.Hints, 0, _hints, slot * ReplayFrame.Entities, ReplayFrame.Entities);
            Array.Copy(f.PlayerIds, 0, _ids, slot * Squad.Slots, Squad.Slots);
            _ballPhase[slot] = f.BallPhase;
            _owner[slot] = f.BallOwnerSlot;
            _head = (_head + 1) % RingCapacity;
            if (_count < RingCapacity) _count++;
        }
    }
}
