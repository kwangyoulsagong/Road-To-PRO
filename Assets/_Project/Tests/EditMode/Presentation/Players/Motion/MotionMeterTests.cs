using System.Collections.Generic;
using NUnit.Framework;
using RoadToPro.Core.Math;
using RoadToPro.Presentation.Players.Logic.Motion;
using RoadToPro.Presentation.Players.Motion;

namespace RoadToPro.Tests.EditMode.Presentation.Players.Motion
{
    /// <summary>지표 계산식 (specs/054 § 검증: 미끄러짐·접촉 거리 계산식은 EditMode로 검증).</summary>
    public class MotionMeterTests
    {
        [Test]
        public void SlideTracker_PlantedFoot_ZeroSlide()
        {
            var s = new SlideTracker(0.03f, 1f);
            for (int i = 0; i < 10; i++) s.Add(i / 60f, 1f, 0.01f, 2f, out _);
            Assert.IsTrue(s.Add(11f / 60f, 1f, 0.2f, 2f, out var slide));
            Assert.AreEqual(0f, slide, 1e-6f);
        }

        [Test]
        public void SlideTracker_MovingFoot_MaxFromStanceStart()
        {
            var s = new SlideTracker(0.03f, 1f);
            for (int i = 0; i <= 6; i++) s.Add(i / 60f, i * 0.005f, 0.01f, 0f, out _);
            Assert.IsTrue(s.Add(7f / 60f, 0.03f, 0.5f, 0f, out var slide));
            Assert.AreEqual(0.025f, slide, 1e-4f, "접지 시작 프레임은 속도를 알 수 있는 둘째 샘플부터");
        }

        [Test]
        public void SlideTracker_FastLowFoot_NotStance()
        {
            var s = new SlideTracker(0.03f, 1f);
            for (int i = 0; i <= 5; i++) s.Add(i / 60f, i * 0.2f, 0.01f, 0f, out _);
            Assert.IsFalse(s.InStance, "낮게 지나가는 스윙 발은 접지가 아니다");
        }

        [Test]
        public void SlideTracker_Finish_ClosesOpenStance()
        {
            var s = new SlideTracker(0.03f, 1f);
            s.Add(0f, 0f, 0f, 0f, out _);
            s.Add(0.1f, 0.01f, 0f, 0f, out _);
            Assert.IsTrue(s.Finish(out _));
            Assert.IsFalse(s.Finish(out _));
        }

        [Test]
        public void SegmentDistance_KnownCases()
        {
            Assert.AreEqual(1f, BodyCapsules.SegmentDistance(new Vec3(0, 0, 0), new Vec3(1, 0, 0), new Vec3(0, 1, 0), new Vec3(1, 1, 0)), 1e-5f, "평행");
            Assert.AreEqual(0f, BodyCapsules.SegmentDistance(new Vec3(-1, 0, 0), new Vec3(1, 0, 0), new Vec3(0, -1, 0), new Vec3(0, 1, 0)), 1e-5f, "교차");
            Assert.AreEqual(2f, BodyCapsules.SegmentDistance(new Vec3(0, 0, 0), new Vec3(0, 0, 0), new Vec3(2, 0, 0), new Vec3(2, 0, 0)), 1e-5f, "점 두 개");
            Assert.AreEqual(1f, BodyCapsules.SegmentDistance(new Vec3(0, 0, 0), new Vec3(1, 0, 0), new Vec3(2, 0, 0), new Vec3(3, 0, 0)), 1e-5f, "같은 선");
        }

        [Test]
        public void Percentile_Interpolates_EmptyZero()
        {
            Assert.AreEqual(0f, MotionStats.Percentile(new List<float>(), 95));
            Assert.AreEqual(2f, MotionStats.Percentile(new List<float> { 3, 1, 2 }, 50));
            Assert.AreEqual(3f, MotionStats.Percentile(new List<float> { 3, 1, 2 }, 100));
        }

        [Test]
        public void Report_Violations_FlagsOverLimit()
        {
            var r = new MotionReport { Scene = "x", Rate = 1f };
            r.Add("slide", 1f, 0, 0.5f);
            r.Add("yawDelta", 1f, 0, 5f);
            var v = r.Violations(MotionStandardsTests.Load());
            Assert.IsTrue(v.Exists(a => a.Metric == "slide"));
            Assert.IsFalse(v.Exists(a => a.Metric == "yawDelta"));
        }
    }
}
