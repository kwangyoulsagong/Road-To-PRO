using NUnit.Framework;
using RoadToPro.Core.Random;

namespace RoadToPro.Tests.EditMode.Core
{
    public class RngStreamsTests
    {
        [Test, Category("Determinism")]
        public void ExtraDrawsOnA_DoNotChangeB()
        {
            const ulong seed = 123456789UL;
            var a1 = RngStreams.Create(seed, RngStreamId.PlayerDecision);
            var b1 = RngStreams.Create(seed, RngStreamId.Referee);
            var a2 = RngStreams.Create(seed, RngStreamId.PlayerDecision);
            var b2 = RngStreams.Create(seed, RngStreamId.Referee);
            for (int i = 0; i < 1000; i++) a2.NextU64(); // A만 1,000번 더
            for (int i = 0; i < 1000; i++)
            {
                a1.NextU64();
                Assert.That(b2.NextU64(), Is.EqualTo(b1.NextU64()), $"{i}번째");
            }
        }

        [Test]
        public void Streams_AreDistinct()
        {
            const ulong seed = 42UL;
            var ids = (RngStreamId[])System.Enum.GetValues(typeof(RngStreamId));
            var firsts = new System.Collections.Generic.HashSet<ulong>();
            foreach (var id in ids) Assert.That(firsts.Add(RngStreams.Create(seed, id).NextU64()), $"{id} 중복");
        }

        [Test]
        public void SubStreams_StableAndDistinct()
        {
            var p7 = RngStreams.Create(9UL, RngStreamId.PlayerDecision, 7UL).NextU64();
            Assert.That(RngStreams.Create(9UL, RngStreamId.PlayerDecision, 7UL).NextU64(), Is.EqualTo(p7));
            Assert.That(RngStreams.Create(9UL, RngStreamId.PlayerDecision, 8UL).NextU64(), Is.Not.EqualTo(p7));
            Assert.That(RngStreams.Create(9UL, RngStreamId.PlayerDecision, 0UL).NextU64(),
                Is.Not.EqualTo(RngStreams.Create(9UL, RngStreamId.PlayerDecision).NextU64()));
        }
    }
}
