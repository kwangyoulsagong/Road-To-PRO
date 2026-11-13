using NUnit.Framework;
using RoadToPro.Core.Math;
using RoadToPro.Core.Random;
using RoadToPro.MatchEngine.UtilityAI;

namespace RoadToPro.Tests.EditMode.MatchEngine.UtilityAI
{
    /// <summary>specs/072 FR-01·03·04·15·20 후보 생성 (AC-03), 경계 상황.</summary>
    public class OnBallCandidateTests
    {
        static bool Finite(float v) => !float.IsNaN(v) && !float.IsInfinity(v);

        [Test]
        public void DefaultScene_HasPassDribbleHold_NoNaN()
        {
            var f = new OnBallFixture();
            f.Generate();
            var c = f.Decider.Candidates;
            Assert.Greater(c.Count, 0);
            bool pass = false, move = false, hold = false;
            for (int i = 0; i < c.Count; i++)
            {
                pass |= OnBallItems.IsPass(c[i].Item);
                move |= c[i].Item == OnBallItem.Dribble || c[i].Item == OnBallItem.Carry;
                hold |= c[i].Item == OnBallItem.Hold;
                Assert.IsTrue(Finite(c[i].P) && Finite(c[i].V) && Finite(c[i].LossXt), c[i].Item.ToString());
                Assert.That(c[i].P, Is.InRange(0f, 1f));
            }
            Assert.IsTrue(pass && move && hold);
        }

        [Test]
        public void Cap_PrunesByPreScore_KeepsGenerationOrder_FR03()
        {
            var f = new OnBallFixture(DecisionFixture.With(("dec.onball.cand.max", 6f)));
            f.Generate();
            var c = f.Decider.Candidates;
            Assert.Greater(c.RawCount, 6);
            Assert.AreEqual(6, c.Count);
            // 남은 6개는 사전 점수 상위 — 버린 것 중 남은 것의 최솟값보다 큰 것은 없다 (동점 제외)
            var f2 = new OnBallFixture();
            f2.Generate();
            var all = f2.Decider.Candidates;
            float minKept = float.MaxValue;
            for (int i = 0; i < c.Count; i++) if (c[i].Pre < minKept) minKept = c[i].Pre;
            var higher = 0;
            for (int i = 0; i < all.Count; i++) if (all[i].Pre > minKept) higher++;
            Assert.LessOrEqual(higher, 6);
        }

        [Test]
        public void DefaultCap_AtMost24()
        {
            var f = new OnBallFixture();
            f.W.Mind[6] = DecisionAttributes.Average.With(vision: 100f);
            for (int o = 1; o < 11; o++) if (o != 6) f.W.Vel[o] = new Vec2(5f, 0f); // 전원 앞으로 → 스루볼 후보
            f.Generate();
            Assert.LessOrEqual(f.Decider.Candidates.Count, 24);
        }

        /// <summary>상대 코너 (103.5, 1.5): 드리블 가능한 3방향을 동료가 막고, 패스 최소 거리 10 m로 그 동료들은 패스 후보가 아니며, 슈팅 사거리 밖, 홀드 금지.</summary>
        static OnBallFixture Surrounded()
        {
            var f = new OnBallFixture(DecisionFixture.With(("dec.onball.pass.minDist", 10f), ("dec.onball.hold.maxPressure", 0f)));
            f.OnlyOwnerOf(6);
            f.Place(6, new Vec2(103.5f, 1.5f));
            f.Place(7, new Vec2(103.5f, 6.5f));
            f.Place(8, new Vec2(103.5f - 3.5355f, 1.5f + 3.5355f));
            f.Place(9, new Vec2(98.5f, 1.5f));
            f.Place(20, new Vec2(102.5f, 3.0f));
            f.Place(19, new Vec2(101.5f, 2.0f));
            f.GiveBall(6);
            f.Sync();
            return f;
        }

        [Test]
        public void Surrounded_ZeroCandidates_ForcedHoldOrClear_AC03()
        {
            var f = Surrounded();
            var r = f.Decide(new SeedRng(11));
            Assert.IsTrue(r.Fallback, "후보 0 → 강제 폴백");
            Assert.That(r.Item == OnBallItem.Hold || r.Item == OnBallItem.Clear, r.Item.ToString());
            Assert.IsTrue(Finite(r.Point.X) && Finite(r.Point.Y));
            Assert.IsTrue(Finite(f.Decider.Chooser.Score(0)));
        }

        [Test]
        public void AllNegative_StillValidChoice_AC03()
        {
            // 소유자를 동료 8명이 5 m 원으로 둘러싸 드리블을 모두 막고(패스 최소 거리 10 m), 나머지 동료 2명은 뒤쪽 → 백패스뿐, 홀드 이득 음수
            var f = new OnBallFixture(DecisionFixture.With(("dec.onball.pass.minDist", 10f), ("dec.onball.hold.value", -0.1f)));
            f.OnlyOwnerOf(6);
            var me = new Vec2(70f, 34f);
            f.Place(6, me);
            int[] ring = { 1, 2, 3, 4, 5, 7, 8, 9 };
            for (int j = 0; j < 8; j++) f.Place(ring[j], me + RoadToPro.MatchEngine.Actions.ActionMath.Rotate(new Vec2(1f, 0f), DMath.TwoPi * j / 8f) * 5f);
            f.Place(10, new Vec2(40f, 20f));
            f.Place(0, new Vec2(10f, 34f));
            for (int o = 11; o < 22; o++) f.W.Active[o] = o == 11; // 상대는 GK만
            f.GiveBall(6);
            f.Sync();
            var r = f.Decide(new SeedRng(5));
            var c = f.Decider.Candidates;
            Assert.Greater(c.Count, 0);
            for (int i = 0; i < c.Count; i++) Assert.Less(c[i].Utility, 0f, c[i].Item.ToString());
            Assert.That(f.Decider.LastPick, Is.InRange(0, c.Count - 1));
            Assert.IsTrue(Finite(f.Decider.Chooser.Score(f.Decider.LastPick)));
            Assert.IsFalse(r.Fallback);
        }

        [Test]
        public void OffsideMate_CandidateKept_RiskApplied_FR15()
        {
            var f = new OnBallFixture();
            f.OnlyOwnerOf(6);
            f.Place(6, new Vec2(70f, 34f));
            f.Place(10, new Vec2(84f, 40f)); // 라인 80 너머 (15 m — 시야 안)
            f.GiveBall(6);
            f.Generate();
            var c = f.Decider.Candidates;
            var found = false;
            for (int i = 0; i < c.Count; i++)
                if (c[i].Target == 10 && c[i].Item != OnBallItem.ThroughBall)
                {
                    found = true;
                    Assert.Less(c[i].P, c[i].Pass.XPass, "오프사이드 위험이 성공 확률을 깎는다");
                }
            Assert.IsTrue(found, "오프사이드 위치 동료도 후보에서 빼지 않는다");
        }

        [Test]
        public void Dribble_OutOfPitchDirections_Excluded()
        {
            var f = new OnBallFixture();
            f.Place(6, new Vec2(60f, 2f));
            f.GiveBall(6);
            f.Generate();
            var c = f.Decider.Candidates;
            for (int i = 0; i < c.Count; i++)
                if (c[i].Item == OnBallItem.Dribble || c[i].Item == OnBallItem.Carry)
                    Assert.That(c[i].Point.Y, Is.GreaterThanOrEqualTo(1f));
        }

        [Test]
        public void Dribble_DefenderInCone_IsTakeOn_ElseCarry()
        {
            var f = new OnBallFixture();
            f.Place(20, new Vec2(48f, 26f)); // 소유자(45·26) 정면 3 m
            f.Generate();
            var c = f.Decider.Candidates;
            bool takeOn = false;
            for (int i = 0; i < c.Count; i++)
                if (c[i].Item == OnBallItem.Dribble && c[i].Defender == 20) takeOn = true;
            Assert.IsTrue(takeOn);
        }

        [Test]
        public void ShotBeforeDive_GenerationOrder_Tie()
        {
            var f = new OnBallFixture();
            f.Place(6, new Vec2(95f, 34f));
            f.Place(15, new Vec2(96.2f, 34f), new Vec2(-2f, 0f)); // 1.2 m, 다가오는 중
            f.GiveBall(6);
            f.Generate();
            var c = f.Decider.Candidates;
            int shot = -1, dive = -1;
            for (int i = 0; i < c.Count; i++)
            {
                if (c[i].Item == OnBallItem.Shoot) shot = i;
                if (c[i].Item == OnBallItem.Dive) dive = i;
            }
            Assert.GreaterOrEqual(shot, 0);
            Assert.GreaterOrEqual(dive, 0);
            Assert.Less(shot, dive);
        }

        [Test]
        public void Cross_WideZone_BoxMate_LoftedCandidate()
        {
            var f = new OnBallFixture();
            f.Place(6, new Vec2(88f, 62f));
            f.Place(9, new Vec2(95f, 34f));
            f.GiveBall(6);
            f.W.Mind[6] = DecisionAttributes.Average.With(vision: 100f);
            f.Generate();
            var c = f.Decider.Candidates;
            var found = false;
            for (int i = 0; i < c.Count; i++)
                if (c[i].Item == OnBallItem.Cross) { found = true; Assert.IsTrue(c[i].Pass.Lofted); }
            Assert.IsTrue(found);
        }
    }
}
