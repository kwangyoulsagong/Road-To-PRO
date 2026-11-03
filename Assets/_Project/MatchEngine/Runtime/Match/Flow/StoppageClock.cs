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

        public void ResetPeriod()
        {
            System.Array.Clear(_byKind, 0, _byKind.Length);
            _total = 0;
        }

        /// <summary>발표 분 (올림).</summary>
        public int AnnouncedMinutes => (int)((_total + SimClock.TicksPerMinute - 1) / SimClock.TicksPerMinute);

        public void Write(StateWriter w)
        {
            for (int i = 0; i < _byKind.Length; i++) w.Write(_byKind[i]);
            w.Write(_total);
        }

        public void Read(StateReader r)
        {
            for (int i = 0; i < _byKind.Length; i++) _byKind[i] = r.ReadLong();
            _total = r.ReadLong();
        }

        public void AddTo(ref StableHasher h)
        {
            for (int i = 0; i < _byKind.Length; i++) h.Add(_byKind[i]);
            h.Add(_total);
        }
    }
}
