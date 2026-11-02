using System;
using System.Globalization;

namespace RoadToPro.Core.Time
{
    /// <summary>
    /// 경기 시각 = (기간, 기간 시작부터의 결정 틱). 틱과 일대일 (specs/001 FR-41).
    /// 표기: 정규 시간 <c>MM:SS</c>, 추가시간 <c>45+M</c> / <c>45+M:SS</c> (기준 45·90·105·120). 추가시간 0초는 <c>45:00</c>.
    /// </summary>
    public readonly struct MatchTime : IEquatable<MatchTime>
    {
        public readonly MatchPeriod Period;
        public readonly long TicksIntoPeriod;

        public MatchTime(MatchPeriod period, long ticksIntoPeriod)
        {
            if (ticksIntoPeriod < 0) throw new ArgumentOutOfRangeException(nameof(ticksIntoPeriod));
            Period = period;
            TicksIntoPeriod = ticksIntoPeriod;
        }

        public static int StartMinute(MatchPeriod p) => p switch
        {
            MatchPeriod.FirstHalf => 0,
            MatchPeriod.SecondHalf => 45,
            MatchPeriod.ExtraFirst => 90,
            _ => 105,
        };

        public static int NominalMinutes(MatchPeriod p) => p <= MatchPeriod.SecondHalf ? 45 : 15;
        public static long NominalTicks(MatchPeriod p) => NominalMinutes(p) * SimClock.TicksPerMinute;

        public bool IsStoppage => TicksIntoPeriod > NominalTicks(Period);

        /// <summary>추가시간 경과 틱 (정규 시간이면 0).</summary>
        public long StoppageTicks => IsStoppage ? TicksIntoPeriod - NominalTicks(Period) : 0;

        /// <summary>표기 문자열. withSeconds=false면 추가시간은 분만 (<c>45+2</c>), 정규 시간은 항상 MM:SS.</summary>
        public string Format(bool withSeconds = true)
        {
            var ci = CultureInfo.InvariantCulture;
            if (!IsStoppage)
            {
                var totalSec = StartMinute(Period) * 60L + TicksIntoPeriod / SimClock.TicksPerSecond;
                return string.Format(ci, "{0:00}:{1:00}", totalSec / 60, totalSec % 60);
            }
            var endMin = StartMinute(Period) + NominalMinutes(Period);
            var extraSec = StoppageTicks / SimClock.TicksPerSecond;
            return withSeconds
                ? string.Format(ci, "{0}+{1}:{2:00}", endMin, extraSec / 60, extraSec % 60)
                : string.Format(ci, "{0}+{1}", endMin, extraSec / 60);
        }

        /// <summary>
        /// 표기 해석 (초 단위 정밀도). <c>45:00</c>처럼 기간 경계 표기는 모호하므로 기간을 함께 받는다.
        /// </summary>
        public static MatchTime Parse(string text, MatchPeriod period)
        {
            if (string.IsNullOrEmpty(text)) throw new FormatException("빈 경기 시각");
            var plus = text.IndexOf('+');
            long minutes, seconds = 0;
            if (plus >= 0)
            {
                var baseMin = ParseLong(text.Substring(0, plus), text);
                var expectedBase = StartMinute(period) + NominalMinutes(period);
                if (baseMin != expectedBase) throw new FormatException($"'{text}': {period}의 추가시간 기준은 {expectedBase}");
                SplitMinSec(text.Substring(plus + 1), text, out minutes, out seconds);
                return new MatchTime(period, NominalTicks(period) + (minutes * 60 + seconds) * SimClock.TicksPerSecond);
            }
            SplitMinSec(text, text, out minutes, out seconds);
            var into = (minutes * 60 + seconds - StartMinute(period) * 60L) * SimClock.TicksPerSecond;
            if (into < 0 || into > NominalTicks(period)) throw new FormatException($"'{text}'는 {period} 정규 시간 밖");
            return new MatchTime(period, into);
        }

        static void SplitMinSec(string s, string whole, out long min, out long sec)
        {
            var colon = s.IndexOf(':');
            if (colon < 0)
            {
                min = ParseLong(s, whole);
                sec = 0;
                return;
            }
            min = ParseLong(s.Substring(0, colon), whole);
            sec = ParseLong(s.Substring(colon + 1), whole);
            if (sec >= 60) throw new FormatException($"'{whole}': 초는 0~59");
        }

        static long ParseLong(string s, string whole)
        {
            if (!long.TryParse(s, NumberStyles.None, CultureInfo.InvariantCulture, out var v))
                throw new FormatException($"경기 시각 해석 실패: '{whole}'");
            return v;
        }

        public bool Equals(MatchTime o) => Period == o.Period && TicksIntoPeriod == o.TicksIntoPeriod;
        public override bool Equals(object obj) => obj is MatchTime m && Equals(m);
        public override int GetHashCode() => unchecked((int)Period * 397 ^ TicksIntoPeriod.GetHashCode());
        public override string ToString() => Format();
    }
}
