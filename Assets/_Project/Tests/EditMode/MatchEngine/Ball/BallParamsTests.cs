using NUnit.Framework;
using RoadToPro.Core.Tuning;
using RoadToPro.MatchEngine.Ball;

namespace RoadToPro.Tests.EditMode.MatchEngine.Ball
{
    public class BallParamsTests
    {
        [Test]
        public void DefinitionsFile_Loads_AllIdsPresent()
        {
            var reg = BallTestKit.Registry;
            foreach (var f in typeof(BallTuningIds).GetFields())
            {
                var id = (string)f.GetValue(null);
                Assert.IsTrue(reg.TryGetIndex(id, out _), id);
            }
            Assert.AreEqual(typeof(BallTuningIds).GetFields().Length, reg.Count, "정의 파일에 코드가 모르는 id가 있다");
        }

        [Test]
        public void Beta_IsAbout0053()
        {
            var p = BallTestKit.Params();
            Assert.AreEqual(0.053f, p.Beta, 0.0015f);
        }

        [Test]
        public void DefaultType_IsModern()
        {
            Assert.AreEqual(BallType.Modern, BallParams.DefaultType(BallTestKit.Defaults));
            var p = BallParams.Create(BallTestKit.Defaults, SurfaceCondition.Dry, RoadToPro.Core.Pitch.PitchSpec.Default);
            Assert.AreEqual(BallType.Modern, p.Type);
            Assert.AreEqual(0.15f, p.CdHigh, 1e-6f);
            Assert.AreEqual(16f, p.CdCritSpeed, 1e-6f);
        }

        [Test]
        public void Panel32_DragValues()
        {
            var p = BallTestKit.Params(BallType.Panel32);
            Assert.AreEqual(0.47f, p.CdLow, 1e-6f);
            Assert.AreEqual(0.20f, p.CdHigh, 1e-6f);
            Assert.AreEqual(11f, p.CdCritSpeed, 1e-6f);
        }

        [TestCase(SurfaceCondition.Dry, 0.62f, 0.40f, 0.10f)]
        [TestCase(SurfaceCondition.Wet, 0.74f, 0.32f, 0.10f)]
        [TestCase(SurfaceCondition.Artificial, 0.65f, 0.40f, 0.10f)]
        [TestCase(SurfaceCondition.Flooded, 0.74f, 1.00f, 0.20f)]
        [TestCase(SurfaceCondition.LongGrass, 0.62f, 0.52f, 0.10f)]
        public void SurfaceMapping_MatchesPlanTable(SurfaceCondition s, float e, float a, float b)
        {
            var p = BallTestKit.Params(BallType.Panel32, s);
            Assert.AreEqual(e, p.Restitution, 1e-5f);
            Assert.AreEqual(a, p.RollA, 1e-5f);
            Assert.AreEqual(b, p.RollB, 1e-5f);
        }

        [Test]
        public void SpinDecayFactor_IsExpMinusKDt()
        {
            var p = BallTestKit.Params();
            Assert.AreEqual(System.Math.Exp(-0.07 * 0.01), p.SpinDecayFactor, 1e-6);
        }

        [TestCase(@"{""version"":1,""values"":[{""id"":""ball.bounce.e.dry"",""value"":1.2}]}", "ball.bounce.e.dry")]
        [TestCase(@"{""version"":1,""values"":[{""id"":""ball.roll.a"",""value"":-0.1}]}", "ball.roll.a")]
        public void InvalidSurfaceParams_RejectedAtLoad(string json, string id)
        {
            var e = Assert.Throws<TuningLoadException>(() => BallTestKit.WithValues(json));
            StringAssert.Contains(id, e.Message);
        }
    }
}
