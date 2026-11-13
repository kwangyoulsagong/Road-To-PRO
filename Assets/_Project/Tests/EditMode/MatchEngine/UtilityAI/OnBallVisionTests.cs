using NUnit.Framework;
using RoadToPro.Core.Math;
using RoadToPro.MatchEngine.UtilityAI;

namespace RoadToPro.Tests.EditMode.MatchEngine.UtilityAI
{
    /// <summary>specs/072 FR-02 시야 집합 (AC-01).</summary>
    public class OnBallVisionTests
    {
        /// <summary>CM(슬롯 6, 45·26, +x 공격) + 동료 6명: 8·15·19·25·35·48 m.</summary>
        static OnBallFixture Scene(float vision)
        {
            var f = new OnBallFixture();
            f.OnlyOwnerOf(6);
            f.Place(6, new Vec2(45f, 26f));
            f.Place(7, new Vec2(53f, 26f));
            f.Place(8, new Vec2(45f, 41f));
            f.Place(9, new Vec2(64f, 26f));
            f.Place(5, new Vec2(20f, 26f));
            f.Place(10, new Vec2(80f, 26f));
            f.Place(4, new Vec2(93f, 26f));
            f.GiveBall(6);
            f.W.Mind[6] = DecisionAttributes.Average.With(vision: vision);
            f.Sync();
            return f;
        }

        [Test]
        public void Vision1_Radius20_AtMost3_AC01()
        {
            var f = Scene(0f);
            var v = new VisionSet();
            v.Build(f.P, f.W, 6);
            Assert.AreEqual(20f, v.Radius, 1e-5f);
            Assert.AreEqual(3, v.K);
            Assert.AreEqual(3, v.Count);
            for (int i = 0; i < v.Count; i++) Assert.LessOrEqual((f.W.Pos[v.Slot(i)] - f.W.Pos[6]).Length(), 20f);
            Assert.AreEqual(7, v.Slot(0), "거리순");
            Assert.AreEqual(8, v.Slot(1));
            Assert.AreEqual(9, v.Slot(2));
        }

        [Test]
        public void Vision20_Radius50_AtMost8_AC01()
        {
            var f = Scene(100f);
            var v = new VisionSet();
            v.Build(f.P, f.W, 6);
            Assert.AreEqual(50f, v.Radius, 1e-5f);
            Assert.AreEqual(8, v.K);
            Assert.AreEqual(6, v.Count, "50 m 안 동료 6명 전부 (k = 8 이하)");
            Assert.IsTrue(v.Contains(4), "48 m 침투 주자를 본다");
            for (int i = 0; i < v.Count; i++) Assert.LessOrEqual((f.W.Pos[v.Slot(i)] - f.W.Pos[6]).Length(), 50f);
        }

        [Test]
        public void Vision_KCapsNearest_WhenMoreInRadius()
        {
            var f = Scene(0f);
            f.Place(3, new Vec2(40f, 30f)); // 6.4 m — 가장 가까움
            f.Sync();
            var v = new VisionSet();
            v.Build(f.P, f.W, 6);
            Assert.AreEqual(3, v.Count);
            Assert.AreEqual(3, v.Slot(0));
            Assert.IsFalse(v.Contains(9), "k = 3이면 네 번째(19 m)는 빠진다");
        }

        [Test]
        public void Vision_OutsideAngle_SeenAtDelayedPosition()
        {
            var f = Scene(100f); // 시야각 220° → 바로 뒤(180°)는 밖
            f.W.Vel[5] = new Vec2(5f, 0f);
            f.Sync();
            var v = new VisionSet();
            v.Build(f.P, f.W, 6);
            for (int i = 0; i < v.Count; i++)
            {
                if (v.Slot(i) == 5)
                {
                    Assert.IsFalse(v.InAngle(i));
                    var expect = f.W.Pos[5] - f.W.Vel[5] * f.P.OnBallVisionDelay;
                    Assert.AreEqual(expect.X, v.Seen(i).X, 1e-5f);
                }
                if (v.Slot(i) == 7)
                {
                    Assert.IsTrue(v.InAngle(i));
                    Assert.AreEqual(f.W.Pos[7].X, v.Seen(i).X, 1e-6f);
                }
            }
        }

        [Test]
        public void Vision_FewerTeammates_Ok()
        {
            var f = new OnBallFixture();
            f.OnlyOwnerOf(6);
            f.Place(7, new Vec2(55f, 26f));
            f.GiveBall(6);
            f.Sync();
            var v = new VisionSet();
            v.Build(f.P, f.W, 6);
            Assert.AreEqual(1, v.Count);
            f.W.Active[7] = false;
            v.Build(f.P, f.W, 6);
            Assert.AreEqual(0, v.Count);
        }

        [Test]
        public void IntentTargetOutsideVision_NotAddedAsCandidate_FR02()
        {
            var f = Scene(0f);
            f.Generate();
            var c = f.Decider.Candidates;
            for (int i = 0; i < c.Count; i++)
                if (OnBallItems.IsPass(c[i].Item)) Assert.AreNotEqual(4, c[i].Target, "48 m 동료는 Vision 1 집합 밖 → 패스 후보 없음");
            f.W.Intent[6] = new IntentPacket { Has = true, StepKey = 1, Kind = Consideration.Pass, NextPassTarget = 4, InWindow = true, TargetSlot = -1, Role = 0 };
            f.W.Mind[6] = f.W.Mind[6].With(teamwork: 100f);
            var r = f.Decide(new RoadToPro.Core.Random.SeedRng(3));
            Assert.AreNotEqual(4, r.Target, "의도 대상이 시야 밖이면 시야가 우선");
        }
    }
}
