using NUnit.Framework;
using RoadToPro.Verification;

namespace RoadToPro.Tests.EditMode.Verification
{
    // specs/000 T19: 하네스 검증용 결정론 샘플 시뮬 (FR-50, AC-11)
    public class SampleSimTests
    {
        [Test]
        public void SameSeed_TwoRuns_SameCheckpoints()
        {
            var a = new SampleSim(42);
            var b = new SampleSim(42);
            a.Run();
            b.Run();
            Assert.AreEqual(10, a.Checkpoints.Count);
            for (int i = 0; i < a.Checkpoints.Count; i++)
            {
                Assert.AreEqual(a.Checkpoints[i].Name, b.Checkpoints[i].Name);
                Assert.AreEqual(a.Checkpoints[i].Tick, b.Checkpoints[i].Tick);
                Assert.AreEqual(a.Checkpoints[i].Hash, b.Checkpoints[i].Hash);
            }
            Assert.AreEqual("t100", a.Checkpoints[0].Name);
            Assert.AreEqual(1000, a.Checkpoints[9].Tick);
            StringAssert.IsMatch("^[0-9a-f]{16}$", a.Checkpoints[0].Hash);
        }

        [Test]
        public void ExtraRngCall_DivergesFromThatTick()
        {
            var a = new SampleSim(42);
            var b = new SampleSim(42, extraRngAtTick: 450);
            a.Run();
            b.Run();
            for (int i = 0; i < 4; i++) Assert.AreEqual(a.Checkpoints[i].Hash, b.Checkpoints[i].Hash, $"t{(i + 1) * 100}");
            Assert.AreNotEqual(a.Checkpoints[4].Hash, b.Checkpoints[4].Hash, "t500에서 갈라져야 한다");
        }

        [Test]
        public void DifferentSeed_DifferentHash()
        {
            var a = new SampleSim(1);
            var b = new SampleSim(2);
            a.Run();
            b.Run();
            Assert.AreNotEqual(a.Checkpoints[0].Hash, b.Checkpoints[0].Hash);
        }

        [Test]
        public void CheckpointsJson_HasNameTickHash()
        {
            var a = new SampleSim(42);
            a.Run(ticks: 200);
            var json = FarmCheckpoint.ToJson(a.Checkpoints, "start/");
            StringAssert.StartsWith("[{\"name\":\"start/t100\",\"tick\":100,\"hash\":\"", json);
        }

        [Test]
        public void Fnv1a64_KnownVector()
        {
            // FNV-1a 64 공개 테스트 벡터: "a" → af63dc4c8601ec8c
            var h = new Fnv1a64();
            h.Add((byte)'a');
            Assert.AreEqual("af63dc4c8601ec8c", h.Hex);
        }
    }
}
