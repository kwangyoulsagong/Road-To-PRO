using NUnit.Framework;
using RoadToPro.Core.Math;
using RoadToPro.MatchEngine.Actions;

namespace RoadToPro.Tests.EditMode.MatchEngine.Actions
{
    /// <summary>specs/070 FR-14: 압박 트리거 입력 플래그 5종.</summary>
    public class PressTriggerTests
    {
        [Test]
        public void Reception_FiveFlags()
        {
            // 홈(+x 공격), 자기 진영 중앙, 공격 방향을 보고 받음 → 플래그 없음
            Assert.AreEqual(0, PressTriggers.OnReception(new Vec2(40f, 34f), new Vec2(1f, 0f), 1, 105f, 68f, false, false));
            Assert.AreEqual(PressTriggers.BackToGoal, PressTriggers.OnReception(new Vec2(40f, 34f), new Vec2(-1f, 0f), 1, 105f, 68f, false, false));
            Assert.AreEqual(PressTriggers.HeavyTouch, PressTriggers.OnReception(new Vec2(40f, 34f), new Vec2(1f, 0f), 1, 105f, 68f, true, false));
            Assert.AreEqual(PressTriggers.BackPass, PressTriggers.OnReception(new Vec2(40f, 34f), new Vec2(1f, 0f), 1, 105f, 68f, false, true));
            Assert.AreEqual(PressTriggers.NearTouchline, PressTriggers.OnReception(new Vec2(40f, 1f), new Vec2(1f, 0f), 1, 105f, 68f, false, false));
            Assert.AreEqual(PressTriggers.OppHalf, PressTriggers.OnReception(new Vec2(70f, 34f), new Vec2(1f, 0f), 1, 105f, 68f, false, false));
            // 원정(−x 공격): x 30은 상대 진영
            Assert.AreEqual(PressTriggers.OppHalf, PressTriggers.OnReception(new Vec2(30f, 34f), new Vec2(-1f, 0f), -1, 105f, 68f, false, false));
        }

        [Test]
        public void BackPass_Direction()
        {
            Assert.IsTrue(PressTriggers.IsBackPass(new Vec2(50f, 30f), new Vec2(40f, 30f), 1));
            Assert.IsFalse(PressTriggers.IsBackPass(new Vec2(50f, 30f), new Vec2(60f, 30f), 1));
            Assert.IsTrue(PressTriggers.IsBackPass(new Vec2(50f, 30f), new Vec2(60f, 30f), -1));
        }
    }
}
