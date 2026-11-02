using System;
using NUnit.Framework;
using RoadToPro.Core.Calendar;

namespace RoadToPro.Tests.EditMode.Core
{
    public class GameDateTests
    {
        static GameDate D(int y, int m, int d) => GameDate.FromYmd(y, m, d);

        [Test]
        public void Feb29_PlusYear_IsMar1()
        {
            Assert.That(D(2028, 2, 29).AddYears(1), Is.EqualTo(D(2029, 3, 1)));
            Assert.That(D(2028, 2, 29).AddYears(4), Is.EqualTo(D(2032, 2, 29)));
            Assert.That(D(2027, 6, 15).AddYears(-1), Is.EqualTo(D(2026, 6, 15)));
        }

        [Test]
        public void Age_Feb29Born()
        {
            var birth = D(2000, 2, 29);
            Assert.That(GameDate.AgeOn(birth, D(2001, 2, 28)), Is.EqualTo(0));
            Assert.That(GameDate.AgeOn(birth, D(2001, 3, 1)), Is.EqualTo(1));
            Assert.That(GameDate.AgeOn(birth, D(2004, 2, 29)), Is.EqualTo(4));
            Assert.That(GameDate.AgeOn(D(2010, 5, 20), D(2026, 5, 19)), Is.EqualTo(15));
            Assert.That(GameDate.AgeOn(D(2010, 5, 20), D(2026, 5, 20)), Is.EqualTo(16));
            Assert.Throws<ArgumentException>(() => GameDate.AgeOn(D(2010, 5, 20), D(2010, 5, 19)));
        }

        [Test]
        public void Dec31_PlusDay()
        {
            Assert.That(D(2026, 12, 31).AddDays(1), Is.EqualTo(D(2027, 1, 1)));
            Assert.That(D(2027, 1, 1).AddDays(-1), Is.EqualTo(D(2026, 12, 31)));
            Assert.That(D(2028, 2, 28).AddDays(1), Is.EqualTo(D(2028, 2, 29)));
            Assert.That(D(2027, 1, 1) - D(2026, 1, 1), Is.EqualTo(365));
        }

        [Test]
        public void Weekday_And_Leap()
        {
            Assert.That(D(1970, 1, 1).Weekday, Is.EqualTo(Weekday.Thursday));
            Assert.That(D(2026, 11, 2).Weekday, Is.EqualTo(Weekday.Monday));
            Assert.That(D(1, 1, 1).Weekday, Is.EqualTo(Weekday.Monday));
            Assert.That(GameDate.IsLeapYear(2000), Is.True);
            Assert.That(GameDate.IsLeapYear(1900), Is.False);
            Assert.That(GameDate.IsLeapYear(2028), Is.True);
            Assert.Throws<ArgumentOutOfRangeException>(() => D(2027, 2, 29));
        }

        [Test]
        public void IsoWeek_YearBoundaries()
        {
            Assert.That(D(2026, 1, 1).IsoWeek(out var y1), Is.EqualTo(1));
            Assert.That(y1, Is.EqualTo(2026));
            Assert.That(D(2027, 1, 1).IsoWeek(out var y2), Is.EqualTo(53)); // 2027-01-01 금요일 → 2026년 53주
            Assert.That(y2, Is.EqualTo(2026));
            Assert.That(D(2024, 12, 30).IsoWeek(out var y3), Is.EqualTo(1));
            Assert.That(y3, Is.EqualTo(2025));
        }

        [Test]
        public void AllDays_MatchSystemCalendar_1900_2100()
        {
            // 연월일 변환을 .NET 달력으로 대조 (시스템 시계는 읽지 않는다)
            var sys = new DateTime(1900, 1, 1);
            var g = D(1900, 1, 1);
            for (int i = 0; i < 73_000; i++)
            {
                g.Deconstruct(out var y, out var m, out var d);
                if (y != sys.Year || m != sys.Month || d != sys.Day) Assert.Fail($"{g} ≠ {sys:yyyy-MM-dd}");
                if ((int)g.Weekday % 7 != (int)sys.DayOfWeek) Assert.Fail($"요일 {g}");
                g = g.AddDays(1);
                sys = sys.AddDays(1);
            }
        }

        [Test]
        public void FarFuture_And_Bounds()
        {
            Assert.That(D(2026, 7, 1).AddYears(45).Year, Is.EqualTo(2071));
            Assert.That(D(9999, 12, 31).ToString(), Is.EqualTo("9999-12-31"));
            Assert.Throws<ArgumentOutOfRangeException>(() => D(9999, 12, 31).AddDays(1));
            Assert.That(GameDate.Parse("2026-11-02"), Is.EqualTo(D(2026, 11, 2)));
            Assert.Throws<FormatException>(() => GameDate.Parse("2026/11/02"));
        }
    }
}
