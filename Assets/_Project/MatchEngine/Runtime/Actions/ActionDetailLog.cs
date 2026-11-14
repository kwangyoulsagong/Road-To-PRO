using System;
using RoadToPro.Core.Hashing;
using RoadToPro.Core.Serialization;

namespace RoadToPro.MatchEngine.Actions
{
    /// <summary>
    /// 접촉 상세 기록 (specs/008 FR-72·15). SimEvent 종류별 데이터에 다 담지 못하는 상세(지금은 패스 순간 오프사이드 스냅샷: 공 x, 수비 팀 오프사이드 라인 x,
    /// 22 슬롯 x)를 고정 배열에 둔다. 이벤트의 B = 상세 번호 + 1, 상세 기록에는 발행 뒤 이벤트 Seq를 채운다(일련번호 연결).
    /// 체크포인트 해시: 연결된 모든 기록의 누적 해시 + 최근 <see cref="Window"/>건 내용. 스냅샷: 누적 해시 + 최근 창만(경기 내내 커지는 기록이
    /// 006 스냅샷 크기 예산 16 KB를 넘지 않게 — 이벤트 로그처럼 과거분은 원본 실행에 남는다). 복원한 실행은 창 이전 기록을 읽을 수 없다(<see cref="IsAvailable"/>).
    /// 006 스키마·세이브 버전은 바꾸지 않는다. 070·071 상세(접촉 이벤트·공중볼 동작)는 같은 틀에 종류를 추가한다.
    /// </summary>
    public sealed class ActionDetailLog
    {
        public const int Capacity = 4096;
        public const int Slots = 22;
        public const byte KindOffsideSnapshot = 1;
        /// <summary>070 FR-13 접촉 상세 — 저장은 <see cref="Contacts"/>(종류별 고정 배열, 같은 누적 해시·창 규칙).</summary>
        public const byte KindContact = 2;
        public readonly ContactLog Contacts = new ContactLog();
        public const byte KindAerial = 3;
        /// <summary>071 FR-06 공중볼 동작 정보 — 저장은 <see cref="Aerials"/>(같은 규칙, 기록 0건이면 해시 불변).</summary>
        public readonly AerialLog Aerials = new AerialLog();
        /// <summary>스냅샷·해시에 내용을 넣는 최근 기록 수 (012 오프사이드 판정은 직전 패스만 읽는다).</summary>
        public const int Window = 16;

        readonly long[] _seq = new long[Capacity];
        readonly long[] _tick = new long[Capacity];
        readonly byte[] _kind = new byte[Capacity];
        readonly int[] _passer = new int[Capacity];
        readonly float[] _ballX = new float[Capacity];
        readonly float[] _lineX = new float[Capacity];
        readonly float[] _runnerX = new float[Capacity * Slots];
        int _count;
        int _availFrom;
        ulong _roll = Core.Hashing.StableHasher.OffsetBasis;

        public int Count => _count;
        /// <summary>용량을 넘어 기록하지 못한 수 (경기당 패스 ≈ 1,000이라 0이어야 한다).</summary>
        public int Overflow { get; private set; }

        public bool IsAvailable(int i) => i >= _availFrom && i < _count;
        /// <summary>연결된 모든 기록의 누적 해시.</summary>
        public ulong RollingHash => _roll;
        public long EventSeq(int i) => _seq[i];
        public long TickOf(int i) => _tick[i];
        public byte KindOf(int i) => _kind[i];
        public int PasserOf(int i) => _passer[i];
        public float BallX(int i) => _ballX[i];
        public float LineX(int i) => _lineX[i];
        public float RunnerX(int i, int slot) => _runnerX[i * Slots + slot];

        /// <summary>오프사이드 스냅샷 추가. 번호(0부터)를 돌려준다. 가득 차면 −1.</summary>
        public int AddOffside(long tick, int passer, float ballX, float lineX, ReadOnlySpan<float> runnerX)
        {
            if (_count == Capacity) { Overflow++; return -1; }
            var i = _count++;
            _seq[i] = -1;
            _tick[i] = tick;
            _kind[i] = KindOffsideSnapshot;
            _passer[i] = passer;
            _ballX[i] = ballX;
            _lineX[i] = lineX;
            for (int s = 0; s < Slots; s++) _runnerX[i * Slots + s] = s < runnerX.Length ? runnerX[s] : 0f;
            return i;
        }

        /// <summary>발행 뒤 이벤트 Seq 연결. 이미 연결된 번호면 무시하지 않고 예외 — 중복 연결 금지(AC-25).</summary>
        public void Bind(int i, long seq)
        {
            if ((uint)i >= (uint)_count) throw new ArgumentOutOfRangeException(nameof(i));
            if (_seq[i] >= 0) throw new InvalidOperationException($"상세 기록 {i}가 이미 이벤트 {_seq[i]}에 연결됨");
            _seq[i] = seq;
            var h = StableHasher.Create();
            h.Add(_roll);
            AddRecord(ref h, i);
            _roll = h.Value;
        }

        void AddRecord(ref StableHasher h, int i)
        {
            h.Add(_seq[i]); h.Add(_tick[i]); h.Add(_kind[i]); h.Add(_passer[i]); h.Add(_ballX[i]); h.Add(_lineX[i]);
            for (int s = 0; s < Slots; s++) h.Add(_runnerX[i * Slots + s]);
        }

        int WindowStart => System.Math.Max(_availFrom, _count - Window);

        /// <summary>테스트 전용: 기록 하나를 바꾼다 (해시가 바뀌는지 확인, AC-25).</summary>
        public void TamperForTest(int i, float ballX) => _ballX[i] = ballX;

        public void Clear()
        {
            _count = 0;
            _availFrom = 0;
            _roll = StableHasher.OffsetBasis;
            Overflow = 0;
            Contacts.Clear();
            Aerials.Clear();
        }

        public void AddTo(ref StableHasher h)
        {
            h.Add(_count);
            h.Add(Overflow);
            h.Add(_roll);
            for (int i = WindowStart; i < _count; i++) AddRecord(ref h, i);
            Contacts.AddTo(ref h);
            Aerials.AddTo(ref h);
        }

        public void Write(StateWriter w)
        {
            w.Write(_count);
            w.Write(Overflow);
            w.Write(_roll);
            var from = WindowStart;
            w.Write(from);
            for (int i = from; i < _count; i++)
            {
                w.Write(_seq[i]); w.Write(_tick[i]); w.Write(_kind[i]); w.Write(_passer[i]); w.Write(_ballX[i]); w.Write(_lineX[i]);
                for (int s = 0; s < Slots; s++) w.Write(_runnerX[i * Slots + s]);
            }
            Contacts.Write(w);
            Aerials.Write(w);
        }

        public void Read(StateReader r)
        {
            var n = r.ReadInt();
            if (n < 0 || n > Capacity) throw new FormatException($"상세 기록 수 {n}");
            _count = n;
            Overflow = r.ReadInt();
            _roll = r.ReadULong();
            var from = r.ReadInt();
            if (from < 0 || from > n) throw new FormatException($"상세 기록 창 시작 {from}");
            _availFrom = from;
            for (int i = from; i < n; i++)
            {
                _seq[i] = r.ReadLong(); _tick[i] = r.ReadLong(); _kind[i] = r.ReadByte(); _passer[i] = r.ReadInt(); _ballX[i] = r.ReadFloat(); _lineX[i] = r.ReadFloat();
                for (int s = 0; s < Slots; s++) _runnerX[i * Slots + s] = r.ReadFloat();
            }
            Contacts.Read(r);
            Aerials.Read(r);
        }
    }
}
