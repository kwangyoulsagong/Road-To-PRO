using NUnit.Framework;
using RoadToPro.Core.Math;
using RoadToPro.MatchEngine.Match.Flow;
using RoadToPro.MatchEngine.Tactics;

namespace RoadToPro.Tests.EditMode.MatchEngine.Tactics
{
    public class SetPieceLayoutTests
    {
        const float L = ShapeTestKit.PitchL, W = ShapeTestKit.PitchW;

        static Vec2 N(float x, float y) => new Vec2(x / L, y / W);

        static (SetPieceCounts counts, Vec2[] targets) Run(RestartKind kind, Vec2 spot, bool attacking, MarkingScheme scheme = MarkingScheme.Zonal)
        {
            var own = new Vec2[11]; var opp = new Vec2[11]; var active = new bool[11]; var oppA = new bool[11]; var t = new Vec2[11];
            for (int i = 0; i < 11; i++)
            {
                active[i] = oppA[i] = true;
                own[i] = N(10f + 3f * i, 15f + 3.5f * i);
                opp[i] = N(8f + 2.5f * i, 20f + 2.8f * i);
                t[i] = own[i];
            }
            var layout = new SetPieceLayout(ShapeTestKit.Params());
            return (layout.Apply(kind, attacking, spot, scheme, -1, active, own, opp, oppA, t), t);
        }

        static void AssertAway(Vec2[] t, Vec2 spot, float dist)
        {
            for (int i = 1; i < t.Length; i++)
            {
                var d = Vec2.Distance(new Vec2(t[i].X * L, t[i].Y * W), new Vec2(spot.X * L, spot.Y * W));
                Assert.That(d, Is.GreaterThanOrEqualTo(dist), $"선수 {i}");
            }
        }

        [Test]
        public void WallCounts()
        {
            var central = Run(RestartKind.FreeKick, N(18f, 34f), false);
            Assert.That(central.counts.Wall, Is.InRange(4, 5), "중앙 18 m");
            var side = Run(RestartKind.FreeKick, N(25f, 5f), false);
            Assert.That(side.counts.Wall, Is.InRange(2, 3), "측면");
        }

        [Test]
        public void DistanceRule()
        {
            foreach (var spot in new[] { N(18f, 34f), N(25f, 5f), N(40f, 30f) })
                AssertAway(Run(RestartKind.FreeKick, spot, false).targets, spot, 9.15f);
        }

        [Test]
        public void DistanceRule_NearCornerStaysInside()
        {
            var spot = N(1f, 1f);
            var t = Run(RestartKind.FreeKick, spot, false).targets;
            AssertAway(t, spot, 9.15f);
            for (int i = 1; i < 11; i++)
            {
                Assert.That(t[i].X, Is.InRange(0f, 1f));
                Assert.That(t[i].Y, Is.InRange(0f, 1f));
            }
        }

        [TestCase(MarkingScheme.Zonal, 6, 0)]
        [TestCase(MarkingScheme.Hybrid, 6, 2)]
        [TestCase(MarkingScheme.Man, 6, 4)]
        public void CornerSchemes(MarkingScheme scheme, int zones, int markers)
        {
            var r = Run(RestartKind.Corner, N(0f, 0f), false, scheme);
            Assert.That(r.counts.Zones, Is.EqualTo(zones));
            Assert.That(r.counts.Markers, Is.EqualTo(markers));
        }

        [Test]
        public void Kickoff_OwnHalfAndCircle()
        {
            var spot = new Vec2(.5f, .5f);
            var t = Run(RestartKind.Kickoff, spot, false).targets;
            AssertAway(t, spot, 9.15f);
            for (int i = 1; i < 11; i++) Assert.That(t[i].X, Is.LessThanOrEqualTo(.5f + 1e-5f));
        }

        [Test]
        public void Penalty_OutOfBox()
        {
            var spot = N(11f, 34f);
            var t = Run(RestartKind.Penalty, spot, false).targets;
            for (int i = 1; i < 11; i++)
                Assert.That(t[i].X * L, Is.GreaterThanOrEqualTo(16.5f), $"선수 {i}");
        }
    }
}
