using NUnit.Framework;
using RoadToPro.Core.Math;
using RoadToPro.Presentation.Highlight.Broadcast.Logic;

namespace RoadToPro.Tests.EditMode.Presentation.Highlight.Broadcast
{
    public class CameraOcclusionTests
    {
        static readonly CameraBox Box = new CameraBox(new Vec3(-1f, 0f, -1f), new Vec3(1f, 2f, 1f));

        [Test]
        public void HitsBox_SegmentThrough_True() =>
            Assert.IsTrue(CameraOcclusion.HitsBox(new Vec3(-5f, 1f, 0f), new Vec3(5f, 1f, 0f), Box));

        [Test]
        public void HitsBox_SegmentAbove_False() =>
            Assert.IsFalse(CameraOcclusion.HitsBox(new Vec3(-5f, 3f, 0f), new Vec3(5f, 3f, 0f), Box));

        [Test]
        public void HitsBox_SegmentStopsShort_False() =>
            Assert.IsFalse(CameraOcclusion.HitsBox(new Vec3(-5f, 1f, 0f), new Vec3(-2f, 1f, 0f), Box));

        [Test]
        public void HitsPost_Crossing_True() =>
            Assert.IsTrue(CameraOcclusion.HitsPost(new Vec3(-5f, 1f, 0f), new Vec3(5f, 1f, 0f), new CameraPost(0f, 0f, 0.1f, 2.44f)));

        [Test]
        public void HitsPost_OverTheTop_False() =>
            Assert.IsFalse(CameraOcclusion.HitsPost(new Vec3(-5f, 3f, 0f), new Vec3(5f, 3f, 0f), new CameraPost(0f, 0f, 0.1f, 2.44f)));

        [Test]
        public void HitsPost_Beside_False() =>
            Assert.IsFalse(CameraOcclusion.HitsPost(new Vec3(-5f, 1f, 1f), new Vec3(5f, 1f, 1f), new CameraPost(0f, 0f, 0.1f, 2.44f)));

        [Test]
        public void IsBlocked_TargetInsideNet_NotCountedAsBlocked()
        {
            var rig = new CameraRigData();
            rig.GoalBlockers.Add(new CameraBox(new Vec3(52.5f, 0f, -3.66f), new Vec3(54.5f, 2.44f, 3.66f)));
            Assert.IsFalse(CameraOcclusion.IsBlocked(rig, new Vec3(30f, 5f, 0f), new Vec3(53f, 0.5f, 0f)));
        }
    }
}
