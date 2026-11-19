using NUnit.Framework;
using RoadToPro.Core.Math;
using RoadToPro.Core.Random;
using RoadToPro.MatchEngine.Goalkeeper;
using RoadToPro.Tests.EditMode.MatchEngine.Actions;
using static RoadToPro.Tests.EditMode.MatchEngine.Goalkeeper.KeeperLeaveKit;

namespace RoadToPro.Tests.EditMode.MatchEngine.Goalkeeper
{
    /// <summary>specs/073 크로스 FR-10~12 (AC-02).</summary>
    public class CrossClaimModelTests
    {
        static readonly Vec2 Landing = new Vec2(7f, 34f);
        const float TLand = 1.8f, ContactZ = 2.5f, Slack = 0.3f;

        static RoadToPro.MatchEngine.Actions.ActionWorld Scene(int crowd)
        {
            var w = World(new Vec2(2.5f, 34f));
            ActionTestKit.Put(w, 20, new Vec2(22f, 30f)); // 멀리 있는 상대 (Δt > 0)
            // 혼잡도: 착지점 반경 2 m 안에 crowd명 (상대 3 · 동료 2 순)
            var slots = new[] { 12, 13, 14, 3, 4 };
            for (int i = 0; i < crowd; i++)
            {
                var a = i * 1.2566f;
                ActionTestKit.Put(w, slots[i], Landing + new Vec2(1.2f * DMath.Cos(a), 1.2f * DMath.Sin(a)));
            }
            return w;
        }

        [Test]
        public void Crowd0_Command20_ClaimHighest()
        {
            var m = new CrossClaimModel(P);
            var k = Profile(command: 100f);
            var e = m.Evaluate(Scene(0), 0, in k, 1.88f, Goal, 1f, Landing, TLand, ContactZ, Slack);
            Assert.IsTrue(e.Has);
            Assert.AreEqual(0, e.Crowd);
            TestContext.WriteLine($"Δt {e.Dt:F2} p_claim {e.PClaim:F2} U {e.UClaim:F2} / 펀칭 {e.UPunch:F2}");
            Assert.Greater(e.UClaim, e.UPunch, "잡기 > 펀칭");
            Assert.Greater(e.UClaim, P.ChoicePositionUtility, "잡기 > 포기(위치 선정)");
        }

        [Test]
        public void Crowd5_Command1_PunchOrStay_Delay02()
        {
            var m = new CrossClaimModel(P);
            var k = Profile(command: 0f);
            var e = m.Evaluate(Scene(5), 0, in k, 1.88f, Goal, 1f, Landing, TLand, ContactZ, Slack);
            Assert.IsTrue(e.Has);
            Assert.AreEqual(5, e.Crowd);
            TestContext.WriteLine($"Δt {e.Dt:F2} p_claim {e.PClaim:F2} U {e.UClaim:F2} / 펀칭 {e.UPunch:F2}");
            Assert.Greater(System.Math.Max(e.UPunch, P.ChoicePositionUtility), e.UClaim, "펀칭 또는 포기가 잡기보다 높다");
            Assert.AreEqual(0.2f, k.CommandDelay, 1e-6f, "결단 지연 0.2·(1 − n)");
            var rng = new SeedRng(7303UL);
            double sum = 0, sq = 0;
            const int N = 4000;
            for (int i = 0; i < N; i++) { var d = m.Delay(in k, rng); sum += d; sq += d * d; }
            var mean = sum / N;
            var sd = System.Math.Sqrt(sq / N - mean * mean);
            TestContext.WriteLine($"지연 평균 {mean:F4} s, σ {sd:F4}");
            Assert.AreEqual(0.2, mean, 3 * P.CrossDelayNoise / System.Math.Sqrt(N) + 1e-3, "0.2 s ± 노이즈");
            Assert.Greater(sd, 0.0, "노이즈 있음");
            var top = Profile(command: 100f);
            Assert.AreEqual(0f, m.Delay(in top, rng), 1e-6f, "CommandArea 20은 지연·노이즈 0");
        }

        [Test]
        public void Crowd0_NoPenaltyTerm()
        {
            var m = new CrossClaimModel(P);
            var k = Profile();
            var e = m.Evaluate(Scene(0), 0, in k, 1.88f, Goal, 1f, Landing, TLand, ContactZ, Slack);
            Assert.AreEqual(CrossClaimModel.Sigmoid(P.CrossK1 * e.Dt + P.CrossK2 * k.CommandN), e.PClaim, 1e-6f);
        }

        [Test]
        public void Cross_LandingInGoal_NoCandidate()
        {
            var m = new CrossClaimModel(P);
            var e = m.Evaluate(Scene(0), 0, Profile(), 1.88f, Goal, 1f, new Vec2(-0.4f, 34f), TLand, ContactZ, Slack);
            Assert.IsTrue(e.InGoal);
            Assert.IsFalse(e.Has, "골문 안 착지 → 011 선방 판정");
            var outside = m.Evaluate(Scene(0), 0, Profile(), 1.88f, Goal, 1f, new Vec2(25f, 34f), TLand, ContactZ, Slack);
            Assert.IsFalse(outside.Has, "박스 밖 착지는 후보 없음");
        }

        [Test]
        public void JumpReach_WithJumpingBonus()
        {
            var m = new CrossClaimModel(P);
            var k = Profile(jumping: 100f);
            Assert.AreEqual(P.CrossJumpReach + P.MapJumpSlope, m.JumpReach(1.88f, in k), 1e-5f);
            var e = m.Evaluate(Scene(0), 0, Profile(jumping: 0f), 1.88f, Goal, 1f, Landing, TLand, 3.5f, Slack);
            Assert.IsFalse(e.Has, "손 도달 높이보다 높은 접촉은 후보 없음");
        }
    }
}
