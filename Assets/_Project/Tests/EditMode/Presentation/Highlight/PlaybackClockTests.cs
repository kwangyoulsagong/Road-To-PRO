using NUnit.Framework;
using RoadToPro.Presentation.Highlight.Logic;

namespace RoadToPro.Tests.EditMode.Presentation.Highlight
{
    /// <summary>재생 시계 (specs/041 FR-10~13, AC-03·04·05).</summary>
    public class PlaybackClockTests
    {
        const double Frame = 1d / 60d;

        [TestCase(1f)]
        [TestCase(1.5f)]
        [TestCase(2f)]
        [TestCase(3f)]
        public void Advance_EachRate_ProgressesByRate(float rate)
        {
            var c = new PlaybackClock();
            c.BeginScene(100d, false, rate);
            for (int i = 0; i < 600; i++) c.Advance(Frame, double.MaxValue);
            Assert.AreEqual(100d + 10d * rate, c.Position, Frame * rate);
        }

        [Test]
        public void BeginScene_Replay_StaysHalfSpeedThenRestores()
        {
            var c = new PlaybackClock();
            c.BeginScene(0d, true, 3f);
            for (int i = 0; i < 600; i++) c.Advance(Frame, double.MaxValue);
            Assert.AreEqual(5d, c.Position, Frame);

            c.BeginScene(20d, false, 3f);
            Assert.AreEqual(3f, c.EffectiveRate);
        }

        [Test]
        public void SetUserRate_MidScene_NoPositionJump()
        {
            var c = new PlaybackClock();
            c.BeginScene(10d, false, 1.5f);
            c.Advance(Frame, double.MaxValue);
            double before = c.Position;
            c.SetUserRate(3f);
            Assert.AreEqual(before, c.Position, "속도만 바꿨을 때 위치는 그대로다");
            c.Advance(Frame, double.MaxValue);
            Assert.LessOrEqual(c.Position - before, Frame * 3f + 1e-9);
        }

        [Test]
        public void Advance_PastLimit_StopsAtLimit()
        {
            var c = new PlaybackClock();
            c.BeginScene(0d, false, 3f);
            c.Advance(100d, 5d);
            Assert.AreEqual(5d, c.Position);
        }
    }
}
