using System;
using RoadToPro.Core.Hashing;
using RoadToPro.Core.Serialization;

namespace RoadToPro.MatchEngine.Match.Pipeline
{
    /// <summary>
    /// "이번 틱 결정 대상" 목록 (specs/006 FR-05). 규칙(반경·주기·트리거·상한)은 010 FR-40~41이 소유한다.
    /// 006은 받은 목록을 중복 제거·id 오름차순으로 정렬해 실행만 한다.
    /// </summary>
    public interface IDecisionScheduler
    {
        /// <summary>tick에 결정할 슬롯을 outSlots에 쓰고 개수를 돌려준다.</summary>
        int Select(long tick, IMatchView view, int[] outSlots);
        void WriteState(StateWriter w);
        void ReadState(StateReader r);
        void AddTo(ref StableHasher h);
    }

    /// <summary>스케줄러를 직접 만드는 결정 단계 (specs/010 LOD). 옵션에 스케줄러가 없으면 경기가 이 스케줄러를 쓴다.</summary>
    public interface IDecisionSchedulerProvider
    {
        IDecisionScheduler Scheduler { get; }
    }

    /// <summary>
    /// 대체 스케줄러: 슬롯별 주기 (기본 매 틱). 슬롯 s는 (tick − 기준 틱) % 주기 == 0일 때 결정한다.
    /// 주기를 바꾸면 바꾼 다음 틱을 새 기준으로 삼는다 — 전환 순간 누락·중복 없음 (AC-04).
    /// </summary>
    public sealed class FixedPeriodScheduler : IDecisionScheduler
    {
        readonly int[] _period = new int[TickBoard.MaxSlots];
        readonly long[] _anchor = new long[TickBoard.MaxSlots];

        public FixedPeriodScheduler(int defaultPeriod = 1)
        {
            if (defaultPeriod < 1) throw new ArgumentOutOfRangeException(nameof(defaultPeriod));
            for (int i = 0; i < _period.Length; i++) _period[i] = defaultPeriod;
        }

        public int Period(int slot) => _period[slot];

        /// <summary>fromTick부터 주기 period로 결정한다 (fromTick에 첫 결정).</summary>
        public void SetPeriod(int slot, int period, long fromTick)
        {
            if (period < 1) throw new ArgumentOutOfRangeException(nameof(period));
            _period[slot] = period;
            _anchor[slot] = fromTick;
        }

        public int Select(long tick, IMatchView view, int[] outSlots)
        {
            int n = 0;
            for (int s = 0; s < _period.Length; s++)
            {
                if (!view.IsSlotActive(s)) continue;
                var d = tick - _anchor[s];
                if (d >= 0 && d % _period[s] == 0) outSlots[n++] = s;
            }
            return n;
        }

        public void WriteState(StateWriter w)
        {
            for (int i = 0; i < _period.Length; i++) { w.Write(_period[i]); w.Write(_anchor[i]); }
        }

        public void ReadState(StateReader r)
        {
            for (int i = 0; i < _period.Length; i++) { _period[i] = r.ReadInt(); _anchor[i] = r.ReadLong(); }
        }

        public void AddTo(ref StableHasher h)
        {
            for (int i = 0; i < _period.Length; i++) { h.Add(_period[i]); h.Add(_anchor[i]); }
        }
    }
}
