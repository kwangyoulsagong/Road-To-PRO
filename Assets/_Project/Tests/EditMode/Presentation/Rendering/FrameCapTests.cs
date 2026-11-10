using NUnit.Framework;
using RoadToPro.Presentation.Rendering.Logic;

namespace RoadToPro.Tests.EditMode.Presentation.Rendering
{
    /// <summary>상태별 프레임 상한 (specs/048 FR-12·13·14·17, AC-11·17 로직).</summary>
    public class FrameCapTests
    {
        [Test]
        public void FrameCap_Highlight_60_Battery_30()
        {
            Assert.AreEqual(60, FrameCap.Decide(RenderState.Highlight, false).TargetFps);
            Assert.AreEqual(30, FrameCap.Decide(RenderState.Highlight, true).TargetFps);
            Assert.IsTrue(FrameCap.Decide(RenderState.Highlight, false).Render3D);
        }

        [Test]
        public void FrameCap_BatteryToggle_Immediate()
        {
            // 상태 없는 순수 함수: 토글한 다음 호출에서 바로 새 상한
            bool saver = false;
            Assert.AreEqual(60, FrameCap.Decide(RenderState.Highlight, saver).TargetFps);
            saver = true;
            Assert.AreEqual(30, FrameCap.Decide(RenderState.Highlight, saver).TargetFps);
            saver = false;
            Assert.AreEqual(60, FrameCap.Decide(RenderState.Highlight, saver).TargetFps);
        }

        [Test]
        public void FrameCap_Timeline_No3D()
        {
            foreach (var saver in new[] { false, true })
            {
                var d = FrameCap.Decide(RenderState.Timeline, saver);
                Assert.IsFalse(d.Render3D);
                Assert.IsTrue(d.FreezeLastFrame);
                Assert.AreEqual(FrameCap.Decide(RenderState.Highlight, saver).TargetFps, d.TargetFps, "재개 신호가 늦지 않게 하이라이트 상한 유지");
            }
        }

        [Test]
        public void FrameCap_Menu_30_No3D()
        {
            var d = FrameCap.Decide(RenderState.Menu, true);
            Assert.AreEqual(30, d.TargetFps);
            Assert.IsFalse(d.Render3D);
            Assert.IsFalse(d.FreezeLastFrame);
        }
    }
}
