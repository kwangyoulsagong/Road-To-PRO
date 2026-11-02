using System;
using RoadToPro.Core.Hashing;

namespace RoadToPro.MatchEngine.Ball
{
    /// <summary>
    /// 이벤트 버퍼 — 호출자가 만들어 재사용한다 (할당 0). 용량을 넘으면 조용히 버리지 않고 예외.
    /// 계약: 서브스텝마다 Clear한다. Step 한 번 최대 7개(바운스 전이 2 + 바운스 1 + 골대 2 + 경계 2) + 수치 보정 1,
    /// 킥 2(클램프 + 전이), 컨트롤 1. 기본 용량 16. 결정 틱 단위로 모으려면 호출자가 별도 로그로 옮긴다 (006).
    /// </summary>
    public sealed class BallEventBuffer
    {
        public const int DefaultCapacity = 16;

        readonly BallEvent[] _items;

        public BallEventBuffer(int capacity = DefaultCapacity)
        {
            if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
            _items = new BallEvent[capacity];
        }

        public int Count { get; private set; }
        public int Capacity => _items.Length;

        public ref readonly BallEvent this[int index]
        {
            get
            {
                if ((uint)index >= (uint)Count) throw new ArgumentOutOfRangeException(nameof(index));
                return ref _items[index];
            }
        }

        public void Clear() => Count = 0;

        internal void TruncateTo(int count)
        {
            if (count < 0 || count > Count) throw new ArgumentOutOfRangeException(nameof(count));
            Count = count;
        }

        internal void Swap(int i, int j)
        {
            var t = _items[i];
            _items[i] = _items[j];
            _items[j] = t;
        }

        public void Add(in BallEvent e)
        {
            if (Count >= _items.Length) throw new InvalidOperationException("BallEventBuffer 용량 초과");
            _items[Count++] = e;
        }

        /// <summary>이벤트 해시 (결정론 테스트·체크포인트용). 필드 순서 고정.</summary>
        public void AddTo(ref StableHasher h)
        {
            h.Add(Count);
            for (int i = 0; i < Count; i++)
            {
                ref readonly var e = ref _items[i];
                h.Add((byte)e.Kind);
                h.Add(e.Fraction);
                h.Add(e.Position.X); h.Add(e.Position.Y); h.Add(e.Position.Z);
                h.Add(e.VelocityIn.X); h.Add(e.VelocityIn.Y); h.Add(e.VelocityIn.Z);
                h.Add(e.VelocityOut.X); h.Add(e.VelocityOut.Y); h.Add(e.VelocityOut.Z);
                h.Add((byte)e.From); h.Add((byte)e.To); h.Add((byte)e.Reason);
                h.Add((byte)e.Boundary); h.Add(e.InGoalMouth); h.Add((byte)e.Part);
                h.Add(e.OwnerFrom); h.Add(e.OwnerTo);
            }
        }
    }
}
