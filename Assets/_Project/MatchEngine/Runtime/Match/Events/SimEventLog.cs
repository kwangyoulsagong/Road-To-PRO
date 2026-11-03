using System;
using RoadToPro.Core.Hashing;

namespace RoadToPro.MatchEngine.Match.Events
{
    /// <summary>
    /// 경기 이벤트 로그 (specs/006 FR-32~34). 일련번호는 1부터 1씩 증가(64비트). 누적 해시는 추가할 때마다 갱신한다.
    /// 배열은 미리 잡고, 넘치면 2배로 늘린다 (150분 경기도 기본 용량이면 1회 이내).
    /// </summary>
    public sealed class SimEventLog
    {
        /// <summary>FR-33 스키마 버전. 필드 추가는 하위 호환, 의미 변경이면 올린다.</summary>
        public const int SchemaVersion = 1;
        public const int DefaultCapacity = 16 * 1024;

        SimEvent[] _items;
        int _count;
        StableHasher _hash = StableHasher.Create();

        public SimEventLog(int capacity = DefaultCapacity)
        {
            _items = new SimEvent[capacity < 16 ? 16 : capacity];
        }

        public int Count => _count;
        public int Capacity => _items.Length;
        public long LastSeq => _count == 0 ? 0 : _items[_count - 1].Seq;
        public ulong Hash => _hash.Value;
        public ref readonly SimEvent this[int index]
        {
            get
            {
                if ((uint)index >= (uint)_count) throw new ArgumentOutOfRangeException(nameof(index));
                return ref _items[index];
            }
        }

        /// <summary>메모리 추정 (배열 바이트, 성능 예산 "이벤트 로그 ≤ 2 MB").</summary>
        public long ApproxBytes => (long)_items.Length * EventSizeBytes;

        /// <summary>SimEvent 1건 메모리 크기 (필드 합 기준 추정, 정렬 패딩 포함 상한).</summary>
        public const int EventSizeBytes = 88;

        /// <summary>일련번호를 붙여 추가한다. 정렬 키가 직전보다 작으면 순서 위반 (발행기가 정렬해 넣는다).</summary>
        public long Append(ref SimEvent e)
        {
            if (_count > 0 && SimEvent.CompareKey(in e, in _items[_count - 1]) < 0)
                throw new InvalidOperationException($"이벤트 순서 위반: {e} 가 {_items[_count - 1]} 뒤에 옴");
            if (_count == _items.Length) Array.Resize(ref _items, _items.Length * 2);
            e.Seq = LastSeq + 1;
            _items[_count++] = e;
            e.AddTo(ref _hash);
            return e.Seq;
        }

        /// <summary>일련번호 seq까지의 누적 해시 (상태를 건드리기 전 검증용).</summary>
        public ulong HashOfPrefix(long seq)
        {
            var h = StableHasher.Create();
            for (int i = 0; i < _count && _items[i].Seq <= seq; i++) _items[i].AddTo(ref h);
            return h.Value;
        }

        /// <summary>source 로그의 seq 이하 앞부분으로 이 로그를 채운다 (재시뮬, FR-63). 누적 해시도 다시 계산한다.</summary>
        public void CopyPrefix(SimEventLog source, long lastSeq)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            _count = 0;
            _hash = StableHasher.Create();
            for (int i = 0; i < source._count && source._items[i].Seq <= lastSeq; i++)
            {
                if (_count == _items.Length) Array.Resize(ref _items, _items.Length * 2);
                _items[_count++] = source._items[i];
                source._items[i].AddTo(ref _hash);
            }
            if (LastSeq != lastSeq) throw new InvalidOperationException($"원본 로그에 seq {lastSeq}까지가 없다 (마지막 {LastSeq})");
        }

        public SimEvent[] ToArray()
        {
            var a = new SimEvent[_count];
            Array.Copy(_items, a, _count);
            return a;
        }

        /// <summary>[fromTick, toTick] 구간 이벤트 (클립용, FR-53).</summary>
        public SimEvent[] Range(long fromTick, long toTick)
        {
            int lo = 0;
            while (lo < _count && _items[lo].Tick < fromTick) lo++;
            int hi = lo;
            while (hi < _count && _items[hi].Tick <= toTick) hi++;
            var a = new SimEvent[hi - lo];
            Array.Copy(_items, lo, a, 0, hi - lo);
            return a;
        }
    }
}
