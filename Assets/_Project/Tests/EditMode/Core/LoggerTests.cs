using NUnit.Framework;
using RoadToPro.Core.Logging;

namespace RoadToPro.Tests.EditMode.Core
{
    public class LoggerTests
    {
        sealed class Probe
        {
            public int Formatted;
            public override string ToString()
            {
                Formatted++;
                return "probe";
            }
        }

        [Test]
        public void LevelFilter_MinWarning()
        {
            var log = new MemoryLogger(LogLevel.Warning);
            log.Debug("d");
            log.Info("i");
            log.Warning("w {0}", 1);
            log.Error("e {0} {1}", 1, 2.5f);
            Assert.That(log.Entries.Count, Is.EqualTo(2));
            Assert.That(log.Entries[0].Message, Is.EqualTo("w 1"));
            Assert.That(log.Entries[1].Message, Is.EqualTo("e 1 2.5"));
        }

        [Test]
        public void DisabledLevel_DoesNotFormatArgs()
        {
            var log = new MemoryLogger(LogLevel.Error);
            var p = new Probe();
            log.Debug("x {0}", p);
            log.Trace("x {0} {1} {2}", p, 1, 2);
            NullLogger.Instance.Error("x {0}", p);
            Assert.That(p.Formatted, Is.EqualTo(0));
            log.Error("x {0}", p);
            Assert.That(p.Formatted, Is.EqualTo(1));
        }

        [Test]
        public void NullLogger_AllDisabled()
        {
            for (var l = LogLevel.Trace; l <= LogLevel.Error; l++)
                Assert.That(NullLogger.Instance.IsEnabled(l), Is.False);
        }
    }
}
