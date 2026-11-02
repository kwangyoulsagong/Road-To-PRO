using System;
using NUnit.Framework;
using RoadToPro.Core.Time;

namespace RoadToPro.Tests.EditMode.Core
{
    public class MatchTimeTests
    {
        [Test]
        public void Constants_10Hz_100Hz()
        {
            Assert.That(SimClock.DecisionHz, Is.EqualTo(10));
            Assert.That(SimClock.PhysicsHz, Is.EqualTo(100));
            Assert.That(SimClock.SubstepsPerDecision, Is.EqualTo(10));
            Assert.That(SimClock.PhysicsStepIndex(3, 4), Is.EqualTo(34));
            Assert.That(SimClock.SecondsAt(27000), Is.EqualTo(2700f));
        }

        [Test]
        public void Tick27000_Is4500()
        {
            Assert.That(new MatchTimeline(0, 0).Format(27000), Is.EqualTo("45:00"));
            Assert.That(new MatchTimeline(1500, 0).Format(27000), Is.EqualTo("45:00"));
            Assert.That(new MatchTimeline(0, 0).Format(0), Is.EqualTo("00:00"));
        }

        [Test]
        public void Stoppage_45plus2_30()
        {
            var tl = new MatchTimeline(1800, 2400);
            Assert.That(tl.Format(27000 + 1500), Is.EqualTo("45+2:30"));
            Assert.That(tl.Format(27000 + 1500, withSeconds: false), Is.EqualTo("45+2"));
            Assert.That(tl.FromTick(27000 + 1500).IsStoppage, Is.True);
        }

        [Test]
        public void SecondHalfStart_Is4500()
        {
            var tl = new MatchTimeline(1800, 2400);
            var t = tl.FromTick(tl.PeriodStartTick(MatchPeriod.SecondHalf));
            Assert.That(t.Period, Is.EqualTo(MatchPeriod.SecondHalf));
            Assert.That(t.Format(), Is.EqualTo("45:00"));
            Assert.That(tl.Format(tl.PeriodStartTick(MatchPeriod.SecondHalf) + 27000 + 600), Is.EqualTo("90+1:00"));
            Assert.That(tl.Format(tl.EndTick), Is.EqualTo("90+4:00"));
        }

        [Test]
        public void FirstHalfLastTick_And_ZeroStoppage()
        {
            var tl = new MatchTimeline(0, 0);
            Assert.That(tl.FromTick(26999).Period, Is.EqualTo(MatchPeriod.FirstHalf));
            Assert.That(tl.Format(26999), Is.EqualTo("44:59"));
            Assert.That(tl.FromTick(27000).Period, Is.EqualTo(MatchPeriod.SecondHalf));
        }

        [Test]
        public void AllTicks_RoundTrip()
        {
            foreach (var tl in new[] { new MatchTimeline(0, 0), new MatchTimeline(1800, 3000), new MatchTimeline(600, 1200, 300, 900) })
            {
                for (long tick = 0; tick <= tl.EndTick; tick++)
                {
                    var mt = tl.FromTick(tick);
                    if (tl.ToTick(mt) != tick) Assert.Fail($"틱 {tick} → {mt.Period}/{mt.TicksIntoPeriod} → {tl.ToTick(mt)}");
                    if (tick % 10 == 0)
                    {
                        var parsed = MatchTime.Parse(mt.Format(), mt.Period);
                        if (!parsed.Equals(mt)) Assert.Fail($"표기 왕복 실패: 틱 {tick} '{mt.Format()}'");
                    }
                }
            }
        }

        [Test]
        public void ExtraTime_Formats()
        {
            var tl = new MatchTimeline(0, 0, 600, 1200);
            Assert.That(tl.Format(tl.PeriodStartTick(MatchPeriod.ExtraFirst)), Is.EqualTo("90:00"));
            Assert.That(tl.Format(tl.PeriodStartTick(MatchPeriod.ExtraFirst) + 9000 + 300), Is.EqualTo("105+0:30"));
            Assert.That(tl.Format(tl.EndTick), Is.EqualTo("120+2:00"));
        }

        [Test]
        public void ExtraTime_RequestedWithoutExtra_Throws()
        {
            var tl = new MatchTimeline(0, 0);
            Assert.Throws<InvalidOperationException>(() => tl.PeriodStartTick(MatchPeriod.ExtraFirst));
            Assert.Throws<InvalidOperationException>(() => tl.ToTick(new MatchTime(MatchPeriod.ExtraSecond, 0)));
            Assert.Throws<ArgumentOutOfRangeException>(() => tl.FromTick(tl.EndTick + 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => tl.FromTick(-1));
            Assert.Throws<FormatException>(() => MatchTime.Parse("45+1", MatchPeriod.SecondHalf));
            Assert.Throws<FormatException>(() => MatchTime.Parse("12:60", MatchPeriod.FirstHalf));
        }

        [Test]
        public void Tick90000_And_10x_NoOverflow()
        {
            // 150분 = 90,000 틱. 그 10배(900,000)까지 표기·변환
            var tl = new MatchTimeline(0, 0, 0, 0);
            Assert.That(tl.EndTick, Is.EqualTo(72000));
            var huge = new MatchTimeline(0, 900_000L - 54000);
            Assert.That(huge.EndTick, Is.EqualTo(900_000L));
            Assert.That(huge.Format(90_000), Is.EqualTo("90+60:00"));
            Assert.That(huge.Format(900_000), Is.EqualTo("90+1410:00"));
            Assert.That(huge.ToTick(huge.FromTick(900_000)), Is.EqualTo(900_000L));
            Assert.That(SimClock.PhysicsStepIndex(900_000, 9), Is.EqualTo(9_000_009L));
            Assert.That(SimClock.MaxMatchTicks, Is.EqualTo(90_000L));
        }
    }
}
