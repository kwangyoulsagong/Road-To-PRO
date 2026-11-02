using System;

namespace RoadToPro.Core.Time
{
    /// <summary>
    /// 경기 한 판의 기간 구성 (정규 45분 ×2, 연장 15분 ×2 선택, 기간별 추가시간). 결정 틱 ↔ <see cref="MatchTime"/> 일대일 (specs/001 FR-41·42).
    /// 기간 경계 틱은 다음 기간의 시작이다 (후반 시작 = 45:00). 마지막 기간의 끝 틱(종료 휘슬)은 마지막 기간에 속한다.
    /// 하프타임은 틱을 쓰지 않는다 (시뮬하지 않음).
    /// </summary>
    public sealed class MatchTimeline
    {
        readonly long[] _start = new long[4];
        readonly long[] _length = new long[4];

        public int PeriodCount { get; }
        public bool HasExtraTime => PeriodCount == 4;

        /// <param name="stoppageTicks">기간별 추가시간(결정 틱). 길이 2(정규) 또는 4(연장 포함).</param>
        public MatchTimeline(params long[] stoppageTicks)
        {
            if (stoppageTicks == null || (stoppageTicks.Length != 2 && stoppageTicks.Length != 4))
                throw new ArgumentException("추가시간은 기간 2개(정규) 또는 4개(연장 포함)");
            PeriodCount = stoppageTicks.Length;
            long t = 0;
            for (int i = 0; i < PeriodCount; i++)
            {
                if (stoppageTicks[i] < 0) throw new ArgumentOutOfRangeException(nameof(stoppageTicks), "추가시간은 0 이상");
                _start[i] = t;
                _length[i] = MatchTime.NominalTicks((MatchPeriod)i) + stoppageTicks[i];
                t = checked(t + _length[i]);
            }
            EndTick = t;
        }

        /// <summary>종료 휘슬 틱 (포함).</summary>
        public long EndTick { get; }

        public long PeriodStartTick(MatchPeriod p)
        {
            CheckPeriod(p);
            return _start[(int)p];
        }

        public long PeriodLengthTicks(MatchPeriod p)
        {
            CheckPeriod(p);
            return _length[(int)p];
        }

        public MatchTime FromTick(long tick)
        {
            if (tick < 0 || tick > EndTick) throw new ArgumentOutOfRangeException(nameof(tick), $"틱 {tick}: 0~{EndTick}");
            for (int i = PeriodCount - 1; i >= 0; i--)
                if (tick >= _start[i])
                    return new MatchTime((MatchPeriod)i, tick - _start[i]);
            throw new InvalidOperationException("도달 불가");
        }

        public long ToTick(MatchTime t)
        {
            CheckPeriod(t.Period);
            var i = (int)t.Period;
            var last = i == PeriodCount - 1;
            if (t.TicksIntoPeriod > _length[i] || (!last && t.TicksIntoPeriod == _length[i]))
                throw new ArgumentOutOfRangeException(nameof(t), $"{t.Period}의 길이 {_length[i]} 틱을 넘는다");
            return _start[i] + t.TicksIntoPeriod;
        }

        public string Format(long tick, bool withSeconds = true) => FromTick(tick).Format(withSeconds);

        void CheckPeriod(MatchPeriod p)
        {
            if ((int)p < 0 || (int)p >= PeriodCount)
                throw new InvalidOperationException($"이 경기에는 {p} 기간이 없다 (연장 없음)");
        }
    }
}
