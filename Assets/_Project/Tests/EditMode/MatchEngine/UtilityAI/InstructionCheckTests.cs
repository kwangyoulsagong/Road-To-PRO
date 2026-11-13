using NUnit.Framework;
using RoadToPro.Core.Math;
using RoadToPro.Core.Random;
using RoadToPro.MatchEngine.UtilityAI;

namespace RoadToPro.Tests.EditMode.MatchEngine.UtilityAI
{
    /// <summary>specs/072 FR-30~32 지시 무시 판정 (AC-04): 온볼·오프볼 같은 규칙. 이벤트 1건 기록은 단계 테스트(<c>OnBallStageTests</c>).</summary>
    public class InstructionCheckTests
    {
        const int N = 1000;

        static OnBallFixture Wing(float compliance, float weight = 0.2f)
        {
            var f = new OnBallFixture();
            f.Place(6, new Vec2(70f, 58f));
            f.Place(15, new Vec2(73f, 58f));
            f.GiveBall(6);
            f.Style.SetStyle(6, PlayStyle.Neutral().WithHabit(Habit.DribbleAddict, 2));
            f.Style.SetPlayerInstructionWeight(6, Consideration.Dribble, weight);
            f.Style.SetCompliance(6, compliance);
            return f;
        }

        [Test]
        public void OnBall_LowCompliance_IgnoresJudged_EachIsSuppressedPick_AC04()
        {
            int ignored = 0, wrong = 0;
            Wing(0.2f).Distribution(6, 201, N, r =>
            {
                if (!r.IgnoredInstruction) return;
                ignored++;
                if (r.Item != OnBallItem.Dribble || r.IgnoredKind != Consideration.Dribble || !(r.IgnoredWeight < 1f)) wrong++;
            });
            TestContext.WriteLine($"지시 무시 {ignored}/{N}");
            Assert.Greater(ignored, 0);
            Assert.AreEqual(0, wrong, "무시 판정은 억제된 행동(드리블)을 고른 결정에만");
        }

        [Test]
        public void OnBall_Compliance1_Zero_AC04()
        {
            int ignored = 0;
            Wing(1f).Distribution(6, 201, N, r => { if (r.IgnoredInstruction) ignored++; });
            Assert.AreEqual(0, ignored);
        }

        [Test]
        public void OnBall_NoInstruction_Zero()
        {
            int ignored = 0;
            Wing(0.2f, 1f).Distribution(6, 201, N, r => { if (r.IgnoredInstruction) ignored++; });
            Assert.AreEqual(0, ignored);
        }

        [Test]
        public void OnBall_ForbiddenFullCompliance_Excluded()
        {
            var f = Wing(1f, 0f);
            int dribbles = 0;
            f.Distribution(6, 202, N, r => { if (r.Item == OnBallItem.Dribble) dribbles++; });
            Assert.AreEqual(0, dribbles, "금지 지시 + 준수율 1 → 후보 제외 (003 FR-31)");
        }

        static int CountOffBall(float compliance, out int runs)
        {
            var f = new DecisionFixture();
            const int s = 9; // ST 62·28
            f.Style.SetStyle(s, PlayStyle.Neutral().WithHabit(Habit.LineBreaker, 2));
            f.Style.SetPlayerInstructionWeight(s, Consideration.RunInBehind, 0.2f);
            f.Style.SetCompliance(s, compliance);
            var rng = new SeedRng(203);
            int ignored = 0, run = 0;
            for (int i = 0; i < N; i++)
            {
                f.ResetState(s);
                var r = f.Decide(s, rng, true);
                if (r.Kind == Consideration.RunInBehind) run++;
                if (r.IgnoredInstruction)
                {
                    ignored++;
                    Assert.AreEqual(Consideration.RunInBehind, r.Kind);
                }
            }
            runs = run;
            return ignored;
        }

        [Test]
        public void OffBall_HoldRunsInstruction_SameRule_AC04()
        {
            var low = CountOffBall(0.2f, out var runsLow);
            var full = CountOffBall(1f, out var runsFull);
            TestContext.WriteLine($"오프볼 침투 자제: 준수 0.2 → 침투 {runsLow}·무시 {low}, 준수 1 → 침투 {runsFull}·무시 {full}");
            Assert.Greater(low, 0);
            Assert.AreEqual(0, full);
        }
    }
}
