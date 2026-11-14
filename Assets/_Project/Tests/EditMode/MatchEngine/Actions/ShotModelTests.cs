using NUnit.Framework;
using RoadToPro.Core.Math;
using RoadToPro.MatchEngine.Actions;

namespace RoadToPro.Tests.EditMode.MatchEngine.Actions
{
    /// <summary>specs/071 FR-10·11 · AC-02: xG 평가 모델.</summary>
    public class ShotModelTests
    {
        static ActionParams P => ActionTestKit.P;
        static readonly Vec2 Goal = new Vec2(105f, 34f);

        static float Xg(Vec2 from, float header = 0f, int blockers = 0)
        {
            var c = new ShotContext { Header = header, Blockers = blockers };
            ShotModel.Geometry(from, Goal, 1, ref c);
            return ShotModel.Xg(P, in c);
        }

        [Test]
        public void Angle_MatchesFormula_PlusPi()
        {
            foreach (var (x, y) in new[] { (11f, 0f), (16.5f, 20.16f), (2f, 1f), (30f, -5f), (1f, 0.5f) })
            {
                var raw = System.MathF.Atan(7.32f * x / (x * x + y * y - 3.66f * 3.66f));
                if (raw < 0f) raw += System.MathF.PI;
                Assert.AreEqual(raw, ShotModel.Angle(x, y), 1e-4f, $"x={x} y={y}");
            }
            Assert.AreEqual(System.MathF.PI * 0.5f, ShotModel.Angle(1f, System.MathF.Sqrt(3.66f * 3.66f - 1f)), 1e-3f, "분모 0 → π/2");
            Assert.AreEqual(System.MathF.PI, ShotModel.Angle(0f, 0f), 1e-5f, "골라인 위 골문 안 = π");
            Assert.AreEqual(0f, ShotModel.Angle(0f, 5f), "골라인 위 골문 밖 = 0");
            Assert.AreEqual(0f, ShotModel.Angle(-1f, 0f), "골라인 밖 = 0");
        }

        [Test]
        public void Order_Penalty_BoxEdge_30m()
        {
            var spot = Xg(new Vec2(94f, 34f));
            var edge = Xg(new Vec2(88.5f, 34f + 20.16f));
            var far = Xg(new Vec2(75f, 34f));
            Assert.Greater(spot, edge, "페널티 지점 정면 > 박스 모서리");
            Assert.Greater(edge, far, "박스 모서리 > 30 m");
            var pen = new ShotContext { Penalty = true };
            Assert.AreEqual(0.78f, ShotModel.Xg(P, in pen), 1e-6f, "FR-11");
            Assert.Greater(ShotModel.Xg(P, in pen), spot);
        }

        [Test]
        public void Header_0p5to0p6_OfFoot()
        {
            foreach (var from in new[] { new Vec2(94f, 34f), new Vec2(93f, 36f), new Vec2(95f, 31f) })
            {
                var r = Xg(from, 1f) / Xg(from);
                Assert.That(r, Is.InRange(0.5f, 0.6f), $"{from.X},{from.Y}: {r}");
            }
        }

        [Test]
        public void Blockers_Decrease()
        {
            var from = new Vec2(90f, 30f);
            var prev = Xg(from);
            for (int b = 1; b <= 4; b++)
            {
                var v = Xg(from, 0f, b);
                Assert.Less(v, prev, $"블로커 {b}");
                prev = v;
            }
        }

        [Test]
        public void AllInUnit_NoNaN_GoalLine_BehindGoal()
        {
            for (float x = 100f; x <= 106f; x += 0.25f)
                for (float y = 20f; y <= 48f; y += 0.5f)
                {
                    var v = Xg(new Vec2(x, y));
                    Assert.IsFalse(float.IsNaN(v), $"{x},{y}");
                    Assert.That(v, Is.InRange(0f, 1f));
                    if (x > 105f) Assert.AreEqual(0f, v, $"골대 뒤 {x},{y}");
                }
            Assert.That(Xg(new Vec2(105f, 34f)), Is.InRange(0.001f, 0.999f), "골라인 위 골문 안");
            Assert.That(Xg(new Vec2(105f, 50f)), Is.InRange(0.001f, 0.999f), "골라인 위 골문 밖");
        }
    }
}
