using RoadToPro.Core.Hashing;
using RoadToPro.Core.Serialization;
using RoadToPro.Core.Time;
using RoadToPro.MatchEngine.Match.Events;

namespace RoadToPro.MatchEngine.Match.Flow
{
    /// <summary>
    /// 추가시간 집계 (specs/006 FR-20·21). 이번 기간의 정지 틱을 요인별로 누적한다.
    /// 기본 산정 = 이벤트가 준 실제 정지 시간의 합 + 요인별 가산 계수(기본 0, [미정]) (2026-11-03 결정).
    /// 발표값 = ⌈누적 / 1분⌉분. 실제 추가 = max(발표값, 누적) — 발표 뒤 정지가 늘면 늘어난다, 줄지는 않는다.
    /// </summary>
    public sealed class StoppageClock
    {
        readonly long[] _byKind = new long[EventCodes.StopKinds];
        readonly long[] _coef;
        long _total;
        int _runKind = -1;
        long _runStart;

        public StoppageClock(long[] coefTicks) => _coef = coefTicks;

        public long TotalTicks => _total;
        public long KindTicks(int kind) => _byKind[kind];

        public void Add(int kind, long ticks)
        {
            if (ticks < 0) ticks = 0;
            var add = ticks + _coef[kind];
            _byKind[kind] += add;
            _total += add;
        }

        /// <summary>진행 중 정지 (058 FR-05: 골 순간부터 킥오프까지). 끝날 때 실제 길이를 더한다.</summary>
        public bool Running => _runKind >= 0;

        public void BeginRunning(int kind, long tick)
        {
            if (_runKind >= 0) EndRunning(tick);
            _runKind = kind;
            _runStart = tick;
        }

        public void EndRunning(long tick)
        {
            if (_runKind < 0) return;
            var kind = _runKind;
            _runKind = -1;
            Add(kind, tick - _runStart);
        }

        /// <summary>진행 중 정지를 지금 틱까지 포함한 누적 (계수는 끝날 때 더한다).</summary>
        public long TotalTicksAt(long tick) => _runKind < 0 ? _total : _total + (tick - _runStart) + _coef[_runKind];

        /// <summary>진행 중 정지를 포함한 발표 분 (올림).</summary>
        public int AnnouncedMinutesAt(long tick) => (int)((TotalTicksAt(tick) + SimClock.TicksPerMinute - 1) / SimClock.TicksPerMinute);

        public void ResetPeriod()
        {
            _runKind = -1;
            _runStart = 0;
            System.Array.Clear(_byKind, 0, _byKind.Length);
            _total = 0;
        }

        /// <summary>발표 분 (올림).</summary>
        public int AnnouncedMinutes => (int)((_total + SimClock.TicksPerMinute - 1) / SimClock.TicksPerMinute);

        public void Write(StateWriter w)
        {
            for (int i = 0; i < _byKind.Length; i++) w.Write(_byKind[i]);
            w.Write(_total);
            w.Write(_runKind);
            w.Write(_runStart);
        }

        public void Read(StateReader r)
        {
            for (int i = 0; i < _byKind.Length; i++) _byKind[i] = r.ReadLong();
            _total = r.ReadLong();
            _runKind = r.ReadInt();
            _runStart = r.ReadLong();
        }

        public void AddTo(ref StableHasher h)
        {
            for (int i = 0; i < _byKind.Length; i++) h.Add(_byKind[i]);
            h.Add(_total);
            h.Add(_runKind);
            h.Add(_runStart);
        }
    }
}
