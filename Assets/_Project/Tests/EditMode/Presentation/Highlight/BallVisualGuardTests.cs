using NUnit.Framework;
using RoadToPro.Core.Math;
using RoadToPro.Core.Pitch;
using RoadToPro.Presentation.Highlight.Logic;

namespace RoadToPro.Tests.EditMode.Presentation.Highlight
{
    /// <summary>공 표시 보호 (specs/041 FR-04).</summary>
    public class BallVisualGuardTests
    {
        static readonly GoalBox Box = new GoalBox(PitchSpec.Default, 7.32f, 2.44f, 2f, 0.11f);

        [Test]
        public void Apply_BelowGround_LiftsToRadius()
        {
            var v = BallVisualGuard.Apply(new Vec3(0f, 0.02f, 0f), new Vec3(0f, 0.11f, 0f), new Vec3(1f, 0.11f, 0f), 0.5f, Box);
            Assert.AreEqual(0.11f, v.Y, 1e-5f);
        }

        [Test]
        public void Apply_SimSamplesLowerThanRadius_DoesNotRaiseAboveSamples()
        {
            var v = BallVisualGuard.Apply(new Vec3(0f, 0.05f, 0f), new Vec3(0f, 0.05f, 0f), new Vec3(1f, 0.05f, 0f), 0.5f, Box);
            Assert.AreEqual(0.05f, v.Y, 1e-5f);
        }

        [Test]
        public void Apply_ExactSample_LeavesSimValue()
        {
            var a = new Vec3(0f, 0.02f, 0f);
            Assert.AreEqual(a, BallVisualGuard.Apply(a, a, new Vec3(1f, 0.11f, 0f), 0f, Box));
        }

        [Test]
        public void Apply_CrossingOutsideMouth_HoldsPreviousSample()
        {
            var a = new Vec3(52.4f, 1f, 5f);
            var b = new Vec3(52.8f, 1f, 5f);
            var v = BallVisualGuard.Apply(new Vec3(52.6f, 1f, 5f), a, b, 0.5f, Box);
            Assert.AreEqual(a, v);
        }

        [Test]
        public void Apply_CrossingOverBar_HoldsPreviousSample()
        {
            var a = new Vec3(-52.4f, 3f, 0f);
            var b = new Vec3(-52.8f, 3f, 0f);
            Assert.AreEqual(a, BallVisualGuard.Apply(new Vec3(-52.6f, 3f, 0f), a, b, 0.5f, Box));
        }

        [Test]
        public void Apply_CrossingInsideMouth_KeepsInterpolation()
        {
            var a = new Vec3(52.4f, 1f, 0f);
            var b = new Vec3(52.8f, 1f, 0f);
            var value = new Vec3(52.6f, 1f, 0f);
            Assert.AreEqual(value, BallVisualGuard.Apply(value, a, b, 0.5f, Box));
        }

        [Test]
        public void Apply_InsideNetPastBack_ClampsToNet()
        {
            var a = new Vec3(54.3f, 1f, 0f);
            var b = new Vec3(54.4f, 1f, 0f);
            var v = BallVisualGuard.Apply(new Vec3(55f, 1f, 0f), a, b, 0.5f, Box);
            Assert.AreEqual(52.5f + 2f - 0.11f, v.X, 1e-4f);
        }
    }
}
