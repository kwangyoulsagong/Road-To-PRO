using NUnit.Framework;
using RoadToPro.Core.Math;
using RoadToPro.MatchEngine.Kinematics;
using static RoadToPro.Tests.EditMode.MatchEngine.Kinematics.KinematicsTestKit;

namespace RoadToPro.Tests.EditMode.MatchEngine.Kinematics
{
    /// <summary>FR-50 기록.</summary>
    public class MotionStatsTests
    {
        [Test]
        public void Sprint_CountsOnce_AccelAndDecelCounted()
        {
            var k = One(KinematicAttributes.Uniform(15), new Vec2(10f, 34f), default, new Vec2(1f, 0f));
            k.SetCommandImmediate(1, SteeringCommand.Arrive(new Vec2(60f, 34f)));
            Run(k, 1500);
            var st = k.Stats(0);
            Assert.That(st.Sprints, Is.EqualTo(1));
            Assert.That(st.Accelerations, Is.EqualTo(1));
            Assert.That(st.Decelerations, Is.EqualTo(1));
            Assert.That(st.Distance, Is.EqualTo(50.0).Within(0.4));
            Assert.That(st.TopSpeed, Is.GreaterThan(7f).And.LessThanOrEqualTo(k.Profile(0).MaxSpeed));
            Assert.That(st.HighIntensityDistance, Is.GreaterThan(20.0).And.LessThan(50.0));
        }

        [Test]
        public void Jog_NoSprintNoHir()
        {
            var k = One(KinematicAttributes.Uniform(15), new Vec2(10f, 34f), default, new Vec2(1f, 0f));
            k.SetCommandImmediate(1, SteeringCommand.Seek(new Vec2(100f, 34f), 3f));
            Run(k, 1000);
            var st = k.Stats(0);
            Assert.That(st.Sprints, Is.Zero);
            Assert.That(st.HighIntensityDistance, Is.Zero);
            Assert.That(st.Accelerations, Is.Zero, "3 m/s까지는 0.5 s 넘게 3 m/s² 초과 가속하지 않음");
        }
    }
}
