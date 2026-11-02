using System.Collections.Generic;
using NUnit.Framework;
using RoadToPro.Core.Hashing;

namespace RoadToPro.Tests.EditMode.Core
{
    public class CheckpointTests
    {
        static CheckpointLog Build(int count, int divergeFrom)
        {
            var log = new CheckpointLog();
            var events = StableHasher.Create();
            for (long tick = 1; log.Items.Count < count; tick++)
            {
                events.Add(tick);
                if (log.Items.Count + 1 >= divergeFrom && divergeFrom > 0 && tick % 100 == 50) events.Add(999); // 갈라짐
                if (log.IsDue(tick)) log.Record(tick, events.Value, (ulong)tick * 31UL);
            }
            return log;
        }

        [Test, Category("Determinism")]
        public void FirstDivergence_At7th_Returns60to70s()
        {
            var a = Build(12, 0);
            var b = Build(12, 7);
            Assert.That(a.Items[5].Equals(b.Items[5]), "6번째까지는 같아야 한다");
            var d = CheckpointLog.FindFirstDivergence(a.Items, b.Items);
            Assert.That(d.HasValue);
            Assert.That(d.Value.Index, Is.EqualTo(6)); // 7번째
            Assert.That(d.Value.StartSeconds, Is.EqualTo(60f));
            Assert.That(d.Value.EndSeconds, Is.EqualTo(70f));
        }

        [Test, Category("Determinism")]
        public void Identical_ReturnsNull_And_LengthMismatch()
        {
            Assert.That(CheckpointLog.FindFirstDivergence(Build(5, 0).Items, Build(5, 0).Items).HasValue, Is.False);
            var d = CheckpointLog.FindFirstDivergence(Build(3, 0).Items, Build(5, 0).Items);
            Assert.That(d.Value.Index, Is.EqualTo(3));
            Assert.That(d.Value.StartSeconds, Is.EqualTo(30f));
            var e = CheckpointLog.FindFirstDivergence(new List<Checkpoint>(), Build(1, 0).Items);
            Assert.That(e.Value.Index, Is.EqualTo(0));
        }

        [Test]
        public void IsDue_Every100Ticks()
        {
            var log = new CheckpointLog();
            Assert.That(log.IsDue(0), Is.False);
            Assert.That(log.IsDue(100), Is.True);
            Assert.That(log.IsDue(150), Is.False);
        }
    }
}
