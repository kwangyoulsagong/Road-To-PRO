using NUnit.Framework;
using RoadToPro.Core.Math;
using RoadToPro.MatchEngine.Kinematics;
using RoadToPro.MatchEngine.UtilityAI;

namespace RoadToPro.Tests.EditMode.MatchEngine.UtilityAI
{
    /// <summary>specs/010 T05: 007 전 대체 공간 평가.</summary>
    public class StandInSpaceTests
    {
        [Test]
        public void Control_FasterPlayerHigher_AndCounted()
        {
            var f = new DecisionFixture();
            var target = new Vec2(88f, 30f);
            f.Space.ResetQueries();
            f.SetKin(9, new KinematicAttributes(20f, 20f, 55f, 55f, 55f, 55f, 55f));
            var slow = f.Space.Control(f.W, 9, target);
            f.SetKin(9, new KinematicAttributes(95f, 95f, 55f, 55f, 55f, 55f, 55f));
            var fast = f.Space.Control(f.W, 9, target);
            Assert.Greater(fast, slow);
            Assert.That(fast, Is.InRange(0f, 1f));
            Assert.AreEqual(2, f.Space.QueryCount);
        }

        [Test]
        public void Threat_HigherNearOpponentGoal()
        {
            var f = new DecisionFixture();
            Assert.Greater(f.Space.Threat(f.W, 0, new Vec2(95f, 34f)), f.Space.Threat(f.W, 0, new Vec2(50f, 34f)));
            Assert.Greater(f.Space.Threat(f.W, 1, new Vec2(10f, 34f)), f.Space.Threat(f.W, 1, new Vec2(95f, 34f)), "원정은 −x 공격");
        }

        [Test]
        public void Openness_LowNearOpponent()
        {
            var f = new DecisionFixture();
            Assert.Less(f.Space.Openness(f.W, 9, f.W.Pos[13], 0f), f.Space.Openness(f.W, 9, new Vec2(30f, 34f), 0f));
        }
    }
}
