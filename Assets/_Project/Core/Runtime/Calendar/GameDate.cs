using System;
using System.Globalization;

namespace RoadToPro.Core.Calendar
{
    /// <summary>ISO 요일 (월=1 … 일=7).</summary>
    public enum Weekday
    {
        Monday = 1,
        Tuesday = 2,
        Wednesday = 3,
        Thursday = 4,
        Friday = 5,
        Saturday = 6,
        Sunday = 7,
    }

    /// <summary>
    /// 게임 날짜 (specs/001 FR-50·51). 1970-01-01 기준 일 번호 하나를 담는 값 타입. 시스템 시계를 읽지 않는다.
    /// 범위 0001-01-01 ~ 9999-12-31 (그레고리력 연장). 연월일 계산은 Hinnant의 civil 알고리즘.
    /// </summary>
    public readonly struct GameDate : IEquatable<GameDate>, IComparable<GameDate>
    {
        public const int MinYear = 1;
        public const int MaxYear = 9999;

        public readonly int DayNumber;

        GameDate(int dayNumber)
        {
            DayNumber = dayNumber;
        }

        static readonly int MinDay = DaysFromCivil(MinYear, 1, 1);
        static readonly int MaxDay = DaysFromCivil(MaxYear, 12, 31);

        public static GameDate FromDayNumber(int dayNumber)
        {
            if (dayNumber < MinDay || dayNumber > MaxDay) throw new ArgumentOutOfRangeException(nameof(dayNumber), "날짜 범위 밖 (0001~9999년)");
            return new GameDate(dayNumber);
        }

        public static GameDate FromYmd(int year, int month, int day)
        {
            if (year < MinYear || year > MaxYear) throw new ArgumentOutOfRangeException(nameof(year), $"연도 {year}: {MinYear}~{MaxYear}");
            if (month < 1 || month > 12) throw new ArgumentOutOfRangeException(nameof(month), $"월 {month}");
            if (day < 1 || day > DaysInMonth(year, month)) throw new ArgumentOutOfRangeException(nameof(day), $"{year}-{month}의 일 {day}");
            return new GameDate(DaysFromCivil(year, month, day));
        }

        public static bool IsLeapYear(int y) => (y % 4 == 0 && y % 100 != 0) || y % 400 == 0;

        public static int DaysInMonth(int y, int m) => m switch
        {
            2 => IsLeapYear(y) ? 29 : 28,
            4 or 6 or 9 or 11 => 30,
            _ => 31,
        };

        public int Year
        {
            get
            {
                CivilFromDays(DayNumber, out var y, out _, out _);
                return y;
            }
        }

        public int Month
        {
            get
            {
                CivilFromDays(DayNumber, out _, out var m, out _);
                return m;
            }
        }

        public int Day
        {
            get
            {
                CivilFromDays(DayNumber, out _, out _, out var d);
                return d;
            }
        }

        public void Deconstruct(out int year, out int month, out int day) => CivilFromDays(DayNumber, out year, out month, out day);

        /// <summary>1970-01-01은 목요일.</summary>
        public Weekday Weekday => (Weekday)(((DayNumber % 7) + 7 + 3) % 7 + 1);

        public int DayOfYear => DayNumber - DaysFromCivil(Year, 1, 1) + 1;

        /// <summary>ISO 8601 주차 (주는 월요일 시작, 그 주의 목요일이 속한 해가 주차 연도).</summary>
        public int IsoWeek(out int isoYear)
        {
            var thursday = new GameDate(DayNumber - ((int)Weekday - 1) + 3);
            isoYear = thursday.Year;
            return (thursday.DayOfYear - 1) / 7 + 1;
        }

        public GameDate AddDays(int days) => FromDayNumber(checked(DayNumber + days));

        /// <summary>연 더하기. 2월 29일이 평년에 떨어지면 3월 1일 (승인 규칙, FR-51).</summary>
        public GameDate AddYears(int years)
        {
            Deconstruct(out var y, out var m, out var d);
            var ny = y + years;
            if (m == 2 && d == 29 && !IsLeapYear(ny)) return FromYmd(ny, 3, 1);
            return FromYmd(ny, m, d);
        }

        /// <summary>그 날짜의 만 나이. 생일 당일에 오른다. 2월 29일생은 평년에 3월 1일에 오른다.</summary>
        public static int AgeOn(GameDate birth, GameDate date)
        {
            if (date < birth) throw new ArgumentException("기준일이 생일보다 앞이다");
            var years = date.Year - birth.Year;
            if (years > 0 && date < birth.AddYears(years)) years--;
            return years;
        }

        public static int operator -(GameDate a, GameDate b) => a.DayNumber - b.DayNumber;
        public static bool operator ==(GameDate a, GameDate b) => a.DayNumber == b.DayNumber;
        public static bool operator !=(GameDate a, GameDate b) => a.DayNumber != b.DayNumber;
        public static bool operator <(GameDate a, GameDate b) => a.DayNumber < b.DayNumber;
        public static bool operator >(GameDate a, GameDate b) => a.DayNumber > b.DayNumber;
        public static bool operator <=(GameDate a, GameDate b) => a.DayNumber <= b.DayNumber;
        public static bool operator >=(GameDate a, GameDate b) => a.DayNumber >= b.DayNumber;

        public int CompareTo(GameDate other) => DayNumber.CompareTo(other.DayNumber);
        public bool Equals(GameDate other) => DayNumber == other.DayNumber;
        public override bool Equals(object obj) => obj is GameDate g && Equals(g);
        public override int GetHashCode() => DayNumber;

        /// <summary>yyyy-MM-dd.</summary>
        public override string ToString()
        {
            Deconstruct(out var y, out var m, out var d);
            return string.Format(CultureInfo.InvariantCulture, "{0:0000}-{1:00}-{2:00}", y, m, d);
        }

        public static GameDate Parse(string s)
        {
            if (s == null || s.Length != 10 || s[4] != '-' || s[7] != '-') throw new FormatException($"날짜 형식은 yyyy-MM-dd: '{s}'");
            var ci = CultureInfo.InvariantCulture;
            if (!int.TryParse(s.Substring(0, 4), NumberStyles.None, ci, out var y) ||
                !int.TryParse(s.Substring(5, 2), NumberStyles.None, ci, out var m) ||
                !int.TryParse(s.Substring(8, 2), NumberStyles.None, ci, out var d))
                throw new FormatException($"날짜 형식은 yyyy-MM-dd: '{s}'");
            return FromYmd(y, m, d);
        }

        static int DaysFromCivil(int y, int m, int d)
        {
            y -= m <= 2 ? 1 : 0;
            var era = (y >= 0 ? y : y - 399) / 400;
            var yoe = y - era * 400;
            var doy = (153 * (m + (m > 2 ? -3 : 9)) + 2) / 5 + d - 1;
            var doe = yoe * 365 + yoe / 4 - yoe / 100 + doy;
            return era * 146097 + doe - 719468;
        }

        static void CivilFromDays(int z, out int y, out int m, out int d)
        {
            z += 719468;
            var era = (z >= 0 ? z : z - 146096) / 146097;
            var doe = z - era * 146097;
            var yoe = (doe - doe / 1460 + doe / 36524 - doe / 146096) / 365;
            y = yoe + era * 400;
            var doy = doe - (365 * yoe + yoe / 4 - yoe / 100);
            var mp = (5 * doy + 2) / 153;
            d = doy - (153 * mp + 2) / 5 + 1;
            m = mp < 10 ? mp + 3 : mp - 9;
            if (m <= 2) y++;
        }
    }
}
