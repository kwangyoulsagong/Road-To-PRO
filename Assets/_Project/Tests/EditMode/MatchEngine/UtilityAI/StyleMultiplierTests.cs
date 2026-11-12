using System;
using System.Reflection;
using NUnit.Framework;
using RoadToPro.MatchEngine.UtilityAI;
using RoadToPro.Tests.EditMode.MatchEngine.Match;

namespace RoadToPro.Tests.EditMode.MatchEngine.UtilityAI
{
    /// <summary>specs/010 T01·T02: 튜닝 기본값 일치, 결합 배수(AC-02), 버릇 15종(AC-03), 대체 입력 범위.</summary>
    public class StyleMultiplierTests
    {
        [Test]
        public void FileDefaults_EqualCodeDefaults()
        {
            var bundle = MatchTestKit.DecisionTuning();
            var fromFile = DecisionParams.Create(bundle);
            var n = 0;
            foreach (var f in typeof(DecisionParams).GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                if (f.FieldType != typeof(float)) continue;
                Assert.AreEqual((float)f.GetValue(DecisionParams.Defaults), (float)f.GetValue(fromFile), f.Name);
                n++;
            }
            Assert.AreEqual(bundle.Registry.Count, n, "튜닝 정의 수 = 코드 필드 수");
        }

        [Test]
        public void Combine_ProductOfTerms_AC02()
        {
            var m = StyleMath.Combine(1.2f, 1.1f, 1.8f, 1.0f);
            Assert.AreEqual(0.10f * 1.2f * 1.1f * 1.8f * 1.0f, 0.10f * m, 1e-6f);
        }

        [Test]
        public void ForbiddenInstruction_FullCompliance_IsZero_AC02()
        {
            Assert.AreEqual(0f, StyleMath.EffectiveInstruction(0f, 1f));
            Assert.AreEqual(0f, StyleMath.Combine(1.2f, 1.1f, 2.0f, StyleMath.EffectiveInstruction(0f, 1f)));
            Assert.Greater(StyleMath.EffectiveInstruction(0f, 0.5f), 0f, "준수율 1 미만이면 금지 지시라도 0이 아니다");
        }

        [Test]
        public void StandInSource_ForbiddenInstruction_ZeroMultiplier()
        {
            var src = new StandInStyleSource(22, DecisionParams.Defaults.TendencyBase);
            src.SetInstructionWeight(0, Consideration.RunInBehind, 0f);
            Assert.AreEqual(0f, src.Multiplier(3, Consideration.RunInBehind));
            Assert.AreEqual(1f, src.Multiplier(14, Consideration.RunInBehind), 1e-6f, "다른 팀은 영향 없음");
        }

        [Test]
        public void StandInSource_Neutral_IsOne()
        {
            var src = new StandInStyleSource(22, DecisionParams.Defaults.TendencyBase);
            for (int k = 0; k < Considerations.OffBallCount; k++) Assert.AreEqual(1f, src.Multiplier(5, (Consideration)k), 1e-6f, ((Consideration)k).ToString());
        }

        [Test]
        public void StandInSource_Breakdown_MatchesProduct()
        {
            var src = new StandInStyleSource(22, DecisionParams.Defaults.TendencyBase);
            var st = new PlayStyle { Runs = 60f }.WithHabit(Habit.LineBreaker, 1).WithRole(Consideration.RunInBehind, 1.2f);
            src.SetStyle(9, st);
            src.Breakdown(9, Consideration.RunInBehind, out var b);
            Assert.AreEqual(1.2f, b.Role, 1e-6f);
            Assert.AreEqual(1.1f, b.Tendency, 1e-6f);
            Assert.AreEqual(2.0f, b.Habit, 1e-6f);
            Assert.AreEqual(1.2f * 1.1f * 2.0f, src.Multiplier(9, Consideration.RunInBehind), 1e-5f);
        }

        // 기획서 4.2.1 버릇 표 (AC-03): 버릇 → (항목, 배수)
        static readonly object[] HabitRows =
        {
            new object[] { Habit.DribbleAddict, Consideration.Dribble, 1.8f },
            new object[] { Habit.CrossMachine, Consideration.Cross, 2.0f },
            new object[] { Habit.LongShotSpammer, Consideration.LongShot, 2.0f },
            new object[] { Habit.CutInMaster, Consideration.CutIn, 1.7f },
            new object[] { Habit.LineBreaker, Consideration.RunInBehind, 2.0f },
            new object[] { Habit.TempoDictator, Consideration.HoldBall, 1.6f },
            new object[] { Habit.OneTouchMaster, Consideration.OneTouchPass, 1.8f },
            new object[] { Habit.KillerPass, Consideration.ThroughBall, 2.0f },
            new object[] { Habit.SlideMaster, Consideration.SlideTackle, 2.0f },
            new object[] { Habit.BoxCrasher, Consideration.BoxCrash, 1.8f },
            new object[] { Habit.Altruist, Consideration.Shot, 0.7f },
            new object[] { Habit.Egoist, Consideration.Shot, 1.5f },
            new object[] { Habit.Showman, Consideration.Trick, 2.0f },
            new object[] { Habit.Diver, Consideration.DiveForFoul, 1.5f },
            new object[] { Habit.BigMatch, Consideration.Shot, 1.0f }, // 점수 배수 없음(002 유효 능력치 경로)
        };

        [TestCaseSource(nameof(HabitRows))]
        public void Habit_AppliesTableMultiplier_AC03(Habit h, Consideration k, float expected)
        {
            var src = new StandInStyleSource(22, DecisionParams.Defaults.TendencyBase);
            src.SetStyle(7, PlayStyle.Neutral().WithHabit(h, 1));
            Assert.AreEqual(expected, src.Multiplier(7, k), 1e-6f);
            Assert.AreEqual(1f, src.Multiplier(8, k), 1e-6f, "버릇 없는 선수");
        }

        [Test]
        public void Habit_AllFifteenRowsCovered()
        {
            Assert.AreEqual(HabitTable.Count, HabitRows.Length);
            Assert.AreEqual(HabitTable.Count, Enum.GetValues(typeof(Habit)).Length);
        }

        [Test]
        public void Habit_SecondaryEffects()
        {
            Assert.AreEqual(0.8f, HabitTable.Multiplier(Habit.DribbleAddict, 1, Consideration.Pass), 1e-6f);
            Assert.AreEqual(0.5f, HabitTable.Multiplier(Habit.CrossMachine, 1, Consideration.CutIn), 1e-6f);
            Assert.AreEqual(1.4f, HabitTable.Multiplier(Habit.Altruist, 1, Consideration.AssistPass), 1e-6f);
            Assert.AreEqual(1f, HabitTable.Multiplier(Habit.LineBreaker, 0, Consideration.RunInBehind), "레벨 0 = 배수 없음");
            Assert.AreEqual(2.5f, HabitTable.Multiplier(Habit.LineBreaker, 2, Consideration.RunInBehind), 1e-6f, "레벨 2 = 1 + (m − 1)·1.5");
        }

        [Test]
        public void Attributes_RangeChecked_AndClamped()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new DecisionAttributes(101f, 50, 50, 50, 50, 50, 50, 50, 50, 50));
            Assert.Throws<ArgumentOutOfRangeException>(() => new DecisionAttributes(float.NaN, 50, 50, 50, 50, 50, 50, 50, 50, 50));
            Assert.AreEqual(1f, DecisionAttributes.N(100f));
            Assert.AreEqual(0f, DecisionAttributes.N(0f));
            Assert.IsFalse(default(DecisionAttributes).IsSet);
            Assert.AreEqual(50f, default(DecisionAttributes).OrAverage().Decisions);
        }

        [Test]
        public void Combine_ClampedToRange()
        {
            Assert.AreEqual(StyleMath.MaxCombined, StyleMath.Combine(3f, 2f, 2.5f, 1f));
            Assert.AreEqual(0f, StyleMath.Combine(-1f, 1f, 1f, 1f));
        }
    }
}
