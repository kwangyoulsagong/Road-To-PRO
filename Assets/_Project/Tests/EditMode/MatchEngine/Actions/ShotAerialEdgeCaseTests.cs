using NUnit.Framework;
using RoadToPro.Core.Math;
using RoadToPro.Core.Random;
using RoadToPro.MatchEngine.Actions;
using RoadToPro.MatchEngine.Goalkeeper;

namespace RoadToPro.Tests.EditMode.MatchEngine.Actions
{
    /// <summary>specs/071 경계 · 예외 상황 · AC-06 (모델 단위. 경기 안 경합 0명 → 루즈볼은 <c>AerialSystemTests</c>).</summary>
    public class ShotAerialEdgeCaseTests
    {
        static ActionParams P => ActionTestKit.P;

        sealed class ZeroKeeper : IShotKeeperModel
        {
            public float BeatProbability(float d, float y, float z, float s, float g) => 0f;
        }

        [Test]
        public void ZeroParticipants_PickMinusOne()
        {
            var pr = new float[6];
            AerialModel.Softmax(new float[6], 0, pr);
            Assert.AreEqual(-1, AerialModel.Pick(pr, 0, 0.3f));
        }

        [Test]
        public void HugeSoftmax_NoNaN()
        {
            var sc = new[] { float.MaxValue / 4f, 0f, -float.MaxValue / 4f };
            var pr = new float[3];
            AerialModel.Softmax(sc, 3, pr);
            foreach (var v in pr) Assert.IsFalse(float.IsNaN(v));
            Assert.AreEqual(0, AerialModel.Pick(pr, 3, 0.5f));
        }

        [Test]
        public void GoalLine_And_BehindGoal_NoNaN()
        {
            var goal = new Vec2(105f, 34f);
            foreach (var from in new[] { new Vec2(105f, 34f), new Vec2(105f, 30.34f), new Vec2(105f, 10f), new Vec2(106f, 34f), new Vec2(110f, 50f) })
            {
                var c = new ShotContext();
                ShotModel.Geometry(from, goal, 1, ref c);
                var xg = ShotModel.Xg(P, in c);
                Assert.IsFalse(float.IsNaN(xg));
                Assert.That(xg, Is.InRange(0f, 1f));
                if (from.X > 105f) Assert.AreEqual(0f, xg);
                var aim = new ShotAim();
                var cell = aim.Choose(from, goal, 25f, 3f, 0f, new StandInKeeperBeat(P));
                Assert.That(cell, Is.InRange(0, ShotAim.Cells - 1));
            }
        }

        [Test]
        public void AllCellsZero_DefaultFarLow()
        {
            var aim = new ShotAim();
            Assert.AreEqual(GoalGrid.Cell(0, 0), aim.Choose(new Vec2(90f, 34f), new Vec2(105f, 34f), 25f, 3f, 1.5f, new ZeroKeeper()));
            Assert.AreEqual(GoalGrid.Cell(0, 6), aim.Choose(new Vec2(90f, 34f), new Vec2(105f, 34f), 25f, 3f, -1.5f, new ZeroKeeper()));
        }

        [Test]
        public void KeeperHeader_FieldRules_BonusOnlyWithHands()
        {
            var jump = AerialModel.JumpHeight(P, 0.5f, 2f, 0.5f);
            Assert.AreEqual(AerialModel.Reach(P, 190f, jump, false) + 0.6f, AerialModel.Reach(P, 190f, jump, true), 1e-5f);
        }

        [Test]
        public void ExtremeAttributes_ProbClampedAndFinite()
        {
            var big = new ShotContext { Dist = 0f, Angle = 3.1f, GkOut = 1f, Assist = 1f };
            var small = new ShotContext { Dist = 100f, Angle = 0f, Pressure = 1f, Blockers = 11, Header = 1f, Bouncing = 1f };
            Assert.That(ShotModel.Xg(P, in big), Is.InRange(0.99f, 0.999f), "최대 입력도 0.999 이하");
            var huge = new ShotContext { Dist = 0f, Angle = 3.1f, GkOut = 10f, Assist = 10f };
            Assert.AreEqual(0.999f, ShotModel.Xg(P, in huge), 1e-6f, "범위 밖 입력은 0.999로 자름");
            Assert.AreEqual(0.001f, ShotModel.Xg(P, in small), 1e-6f);
            foreach (var a in new[] { 0f, 1f })
            {
                var s = ShotExecution.SigmaAimDeg(P, a, 32f, 1f, 1f - a, 2f, true);
                Assert.IsTrue(s > 0f && s < 100f, $"σ {s}");
                var l = CrossModel.SigmaLanding(P, a, 1f);
                Assert.IsTrue(l > 0f && l < 20f);
                var j = AerialModel.JumpHeight(P, a, 50f, a);
                Assert.IsTrue(j > 0f && j < 1.5f);
            }
            var rng = new SeedRng(9);
            var v = ShotExecution.Apply(new Vec3(30f, 0f, 1f), 1e-6f, rng, out _, out _);
            Assert.AreEqual(30f, v.X, 0.1f);
        }
    }
}
