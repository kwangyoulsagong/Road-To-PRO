using System;
using NUnit.Framework;
using RoadToPro.MatchEngine.Kinematics;
using static RoadToPro.Tests.EditMode.MatchEngine.Kinematics.KinematicsTestKit;

namespace RoadToPro.Tests.EditMode.MatchEngine.Kinematics
{
    /// <summary>AC-01: 능력치 → 운동 파라미터 매핑 (FR-01~07). AC-11 일부: 범위 밖 거부.</summary>
    public class KinematicsMappingTests
    {
        [Test]
        public void DefinitionsFile_Loads()
        {
            Assert.That(Registry.Count, Is.GreaterThanOrEqualTo(60));
            Assert.That(Params, Is.Not.Null);
        }

        [Test]
        public void Ability15_MatchesB7()
        {
            var pr = PlayerMotionProfile.Create(KinematicAttributes.Uniform(15), Params);
            Assert.That(pr.MaxSpeed, Is.EqualTo(9.1f).Within(0.1f), "Vmax");
            Assert.That(pr.MaxAccel, Is.EqualTo(8.3f).Within(0.1f), "a0");
            Assert.That(pr.Tau, Is.EqualTo(1.1f).Within(0.05f), "τ");
        }

        [Test]
        public void Ability1And20_TopSpeedKmh()
        {
            var lo = PlayerMotionProfile.Create(KinematicAttributes.Uniform(1), Params);
            var hi = PlayerMotionProfile.Create(KinematicAttributes.Uniform(20), Params);
            Assert.That(lo.MaxSpeed * 3.6f, Is.EqualTo(25.9f).Within(0.2f));
            Assert.That(hi.MaxSpeed * 3.6f, Is.EqualTo(34.9f).Within(0.2f));
        }

        [Test]
        public void OtherMappings_FollowB7Ranges()
        {
            var lo = PlayerMotionProfile.Create(KinematicAttributes.Uniform(1), Params);
            var hi = PlayerMotionProfile.Create(KinematicAttributes.Uniform(20), Params);
            Assert.That(lo.MaxLateralAccel, Is.EqualTo(4.5f).Within(1e-4f));
            Assert.That(hi.MaxLateralAccel, Is.EqualTo(7.0f).Within(1e-4f));
            Assert.That(hi.MaxDecel, Is.EqualTo(7.0f).Within(1e-4f));
            Assert.That(lo.PivotTurnRate * 180f / Math.PI, Is.EqualTo(450f).Within(0.01f));
            Assert.That(hi.PivotTurnRate * 180f / Math.PI, Is.EqualTo(750f).Within(0.01f));
            Assert.That(lo.ContactLoss, Is.EqualTo(0.40f).Within(1e-5f));
            Assert.That(hi.ContactLoss, Is.EqualTo(0.10f).Within(1e-5f));
            Assert.That(hi.ContactCapScale, Is.EqualTo(1.1f).Within(1e-5f));
            Assert.That(lo.FatigueRateScale, Is.EqualTo(1.4f).Within(1e-5f));
            Assert.That(hi.RecoveryScale, Is.EqualTo(1.3f).Within(1e-5f));
            Assert.That(lo.PerceptionDelay, Is.EqualTo(0.35f).Within(1e-5f));
            Assert.That(hi.PerceptionDelay, Is.EqualTo(0.15f).Within(1e-5f));
            Assert.That(hi.MotorDelay, Is.InRange(0.05f, 0.1f));
        }

        [Test]
        public void MappingIsMonotonic_AndPositive()
        {
            float prevV = 0f, prevTau = float.MaxValue;
            for (int n = 1; n <= 20; n++)
            {
                var pr = PlayerMotionProfile.Create(KinematicAttributes.Uniform(n), Params);
                Assert.That(pr.MaxSpeed, Is.GreaterThan(prevV));
                Assert.That(pr.Tau, Is.GreaterThan(0f));
                prevV = pr.MaxSpeed;
                prevTau = pr.Tau;
            }
            Assert.That(prevTau, Is.InRange(0.9f, 1.3f));
        }

        [TestCase(-0.01f)]
        [TestCase(100.01f)]
        [TestCase(float.NaN)]
        public void InvalidAttributes_Rejected(float v)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new KinematicAttributes(v, 50, 50, 50, 50, 50, 50));
            Assert.Throws<ArgumentOutOfRangeException>(() => new KinematicAttributes(50, 50, 50, 50, 50, 50, v));
        }

        [TestCase(0)]
        [TestCase(21)]
        public void InvalidDisplay_Rejected(int n) =>
            Assert.Throws<ArgumentOutOfRangeException>(() => KinematicAttributes.InternalFromDisplay(n));

        [Test]
        public void InvalidTuningCombination_RejectedAtLoad()
        {
            Assert.Throws<RoadToPro.Core.Tuning.TuningLoadException>(() =>
                WithValues("{\"version\":1,\"values\":[{\"id\":\"kin.pivot.enter\",\"value\":2.4},{\"id\":\"kin.pivot.exit\",\"value\":2.3}]}"));
        }
    }
}
