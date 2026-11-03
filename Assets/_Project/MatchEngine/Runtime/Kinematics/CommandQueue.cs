using RoadToPro.Core.Hashing;
using RoadToPro.Core.Serialization;

namespace RoadToPro.MatchEngine.Kinematics
{
    /// <summary>
    /// 선수별 지연 명령 큐 (plan [제안] ③): 결정 틱 명령은 발행 서브스텝 + 반응 지연(서브스텝 반올림)에 활성화된다.
    /// 평면 배열, 선수당 고정 용량. 넘치면 가장 오래된 미활성 명령을 버린다 (진단 경고).
    /// </summary>
    public sealed class CommandQueue
    {
        public const int CapacityPerPlayer = 8;

        readonly SteeringCommand[] _cmds;
        readonly long[] _at;
        readonly int[] _head;
        readonly int[] _count;

        public CommandQueue(int players)
        {
            _cmds = new SteeringCommand[players * CapacityPerPlayer];
            _at = new long[players * CapacityPerPlayer];
            _head = new int[players];
            _count = new int[players];
        }

        public int Pending(int index) => _count[index];

        /// <summary>
        /// 추가. 넘쳐서 하나 버렸으면 true. 활성화 시각은 직전 대기 명령보다 앞서지 않게 맞춘다 —
        /// 피로로 반응 지연이 줄어도 발행 순서대로 활성화된다 (리뷰 R5).
        /// </summary>
        public bool Enqueue(int index, in SteeringCommand cmd, long activateAt)
        {
            if (_count[index] > 0)
            {
                var last = _at[index * CapacityPerPlayer + (_head[index] + _count[index] - 1) % CapacityPerPlayer];
                if (activateAt < last) activateAt = last;
            }
            var dropped = false;
            if (_count[index] == CapacityPerPlayer)
            {
                _head[index] = (_head[index] + 1) % CapacityPerPlayer;
                _count[index]--;
                dropped = true;
            }
            var slot = index * CapacityPerPlayer + (_head[index] + _count[index]) % CapacityPerPlayer;
            _cmds[slot] = cmd;
            _at[slot] = activateAt;
            _count[index]++;
            return dropped;
        }

        /// <summary>substep까지 활성화 시각이 된 명령을 꺼내 마지막 것을 active에 쓴다. 꺼냈으면 true.</summary>
        public bool Activate(int index, long substep, ref SteeringCommand active)
        {
            var any = false;
            while (_count[index] > 0)
            {
                var slot = index * CapacityPerPlayer + _head[index];
                if (_at[slot] > substep) break;
                active = _cmds[slot];
                _head[index] = (_head[index] + 1) % CapacityPerPlayer;
                _count[index]--;
                any = true;
            }
            return any;
        }

        public void Clear(int index) { _head[index] = 0; _count[index] = 0; }

        /// <summary>선수 index의 대기 명령을 쓴다 (활성화 순서대로, specs/006 FR-60).</summary>
        public void Write(int index, StateWriter w)
        {
            w.Write(_count[index]);
            for (int k = 0; k < _count[index]; k++)
            {
                var slot = index * CapacityPerPlayer + (_head[index] + k) % CapacityPerPlayer;
                w.Write(_at[slot]);
                _cmds[slot].Write(w);
            }
        }

        public void Read(int index, StateReader r)
        {
            var count = r.ReadInt();
            if (count < 0 || count > CapacityPerPlayer) throw new System.FormatException($"대기 명령 수 {count}가 0~{CapacityPerPlayer} 밖");
            _head[index] = 0;
            _count[index] = count;
            for (int k = 0; k < count; k++)
            {
                var slot = index * CapacityPerPlayer + k;
                _at[slot] = r.ReadLong();
                _cmds[slot] = SteeringCommand.Read(r);
            }
        }

        public void AddTo(int index, ref StableHasher h)
        {
            h.Add(_count[index]);
            for (int k = 0; k < _count[index]; k++)
            {
                var slot = index * CapacityPerPlayer + (_head[index] + k) % CapacityPerPlayer;
                h.Add(_at[slot]);
                _cmds[slot].AddTo(ref h);
            }
        }
    }
}
