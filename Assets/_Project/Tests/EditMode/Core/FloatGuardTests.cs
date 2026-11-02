using NUnit.Framework;
using RoadToPro.Core.Logging;
using RoadToPro.Core.Math;

namespace RoadToPro.Tests.EditMode.Core
{
    public class FloatGuardTests
    {
        [Test]
        public void Strict_NaN_Throws()
        {
            var log = new MemoryLogger(LogLevel.Trace);
            Assert.Throws<NumericException>(() => FloatGuard.Check(float.NaN, "test", GuardMode.Strict, log));
            Assert.Throws<NumericException>(() => FloatGuard.Check(float.PositiveInfinity, "test", GuardMode.Strict, log));
        }

        [Test]
        public void Sanitize_NaN_SafeValue_OneWarning()
        {
            var log = new MemoryLogger(LogLevel.Trace);
            var v = FloatGuard.Check(float.NaN, "ball.x", GuardMode.Sanitize, log, 7f);
            Assert.That(v, Is.EqualTo(7f));
            Assert.That(log.Entries.Count, Is.EqualTo(1));
            Assert.That(log.Entries[0].Level, Is.EqualTo(LogLevel.Warning));
            StringAssert.Contains("ball.x", log.Entries[0].Message);
        }

        [Test]
        public void Finite_PassesThrough_NoLog()
        {
            var log = new MemoryLogger(LogLevel.Trace);
            Assert.That(FloatGuard.Check(1.5f, "x", GuardMode.Strict, log), Is.EqualTo(1.5f));
            Assert.That(FloatGuard.Check(-0f, "x", GuardMode.Sanitize, log), Is.EqualTo(0f));
            Assert.That(log.Entries.Count, Is.EqualTo(0));
        }

        [Test]
        public void Vec2_Check_SanitizesEachAxis()
        {
            var log = new MemoryLogger(LogLevel.Trace);
            var v = FloatGuard.Check(new Vec2(float.NaN, 2f), "p", GuardMode.Sanitize, log);
            Assert.That(v, Is.EqualTo(new Vec2(0f, 2f)));
            Assert.That(log.Entries.Count, Is.EqualTo(1));
        }
    }
}
