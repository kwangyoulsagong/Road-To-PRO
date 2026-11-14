using NUnit.Framework;
using RoadToPro.Core.Math;
using RoadToPro.MatchEngine.Actions;
using RoadToPro.MatchEngine.Kinematics;

namespace RoadToPro.Tests.EditMode.MatchEngine.Actions
{
    /// <summary>specs/070 FR-20: 몸싸움 · 볼 지키기.</summary>
    public class ShieldModelTests
    {
        static ActionParams P => ActionTestKit.P;

        [Test]
        public void Keep_Average73pct_StrengthMonotonic()
        {
            var w = ActionTestKit.World();
            ActionTestKit.Put(w, 9, new Vec2(80f, 30f));
            ActionTestKit.Put(w, 13, new Vec2(80.8f, 30f));
            Assert.AreEqual(ActionMath.Sigmoid(P.ShieldK0), ShieldModel.KeepProbability(P, w, 9, 13), 1e-5f);
            var strong = TechnicalAttributes.Average.WithDuel(strength: 95f);
            w.SetDuel(9, strong, KinematicAttributes.Uniform(10), 50f, 0);
            var hi = ShieldModel.KeepProbability(P, w, 9, 13);
            w.SetDuel(9, TechnicalAttributes.Average.WithDuel(strength: 5f), KinematicAttributes.Uniform(10), 50f, 0);
            var lo = ShieldModel.KeepProbability(P, w, 9, 13);
            Assert.Greater(hi, lo);
        }
    }
}
