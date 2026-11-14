using System;
using RoadToPro.Core.Hashing;
using RoadToPro.Core.Serialization;

namespace RoadToPro.MatchEngine.Actions
{
    /// <summary>
    /// 접촉 상세 기록 (specs/070 FR-13, 008 FR-72 틀). <see cref="ActionDetailLog"/>의 접촉 종류(<see cref="ActionDetailLog.KindContact"/>) 저장소.
    /// Contact 이벤트 B = 번호 + 1, 발행 뒤 이벤트 Seq 연결. 해시: 연결된 기록 누적 해시 + 최근 <see cref="ActionDetailLog.Window"/>건. 스냅샷: 누적 해시 + 최근 창.
    /// </summary>
    public sealed class ContactLog
    {
        public const int Capacity = 4096;

        readonly ContactDetail[] _rec = new ContactDetail[Capacity];
        readonly long[] _seq = new long[Capacity];
        int _count;
        int _availFrom;
        ulong _roll = StableHasher.OffsetBasis;

        public int Count => _count;
        public int Overflow { get; private set; }
        public ulong RollingHash => _roll;
        public bool IsAvailable(int i) => i >= _availFrom && i < _count;
        public ref readonly ContactDetail this[int i] => ref _rec[i];
        public long EventSeq(int i) => _seq[i];

        /// <summary>추가하고 번호(0부터)를 돌려준다. 가득 차면 −1.</summary>
        public int Add(in ContactDetail c)
        {
            if (_count == Capacity) { Overflow++; return -1; }
            var i = _count++;
            _rec[i] = c;
            _seq[i] = -1;
            return i;
        }

        public void Bind(int i, long seq)
        {
            if ((uint)i >= (uint)_count) throw new ArgumentOutOfRangeException(nameof(i));
            if (_seq[i] >= 0) throw new InvalidOperationException($"접촉 기록 {i}가 이미 이벤트 {_seq[i]}에 연결됨");
            _seq[i] = seq;
            var h = StableHasher.Create();
            h.Add(_roll);
            h.Add(_seq[i]);
            _rec[i].AddTo(ref h);
            _roll = h.Value;
        }

        int WindowStart => Math.Max(_availFrom, _count - ActionDetailLog.Window);

        public void TamperForTest(int i, float intensity) => _rec[i].Intensity = intensity;

        public void Clear()
        {
            _count = 0;
            _availFrom = 0;
            _roll = StableHasher.OffsetBasis;
            Overflow = 0;
        }

        public void AddTo(ref StableHasher h)
        {
            h.Add(_count);
            h.Add(Overflow);
            h.Add(_roll);
            for (int i = WindowStart; i < _count; i++) { h.Add(_seq[i]); _rec[i].AddTo(ref h); }
        }

        public void Write(StateWriter w)
        {
            w.Write(_count);
            w.Write(Overflow);
            w.Write(_roll);
            var from = WindowStart;
            w.Write(from);
            for (int i = from; i < _count; i++) { w.Write(_seq[i]); _rec[i].Write(w); }
        }

        public void Read(StateReader r)
        {
            var n = r.ReadInt();
            if (n < 0 || n > Capacity) throw new FormatException($"접촉 기록 수 {n}");
            _count = n;
            Overflow = r.ReadInt();
            _roll = r.ReadULong();
            var from = r.ReadInt();
            if (from < 0 || from > n) throw new FormatException($"접촉 기록 창 시작 {from}");
            _availFrom = from;
            for (int i = from; i < n; i++) { _seq[i] = r.ReadLong(); _rec[i] = ContactDetail.Read(r); }
        }
    }
}
