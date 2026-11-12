using System;
using System.Reflection;
using NUnit.Framework;
using RoadToPro.Core.Random;
using RoadToPro.MatchEngine.Actions;
using RoadToPro.Tests.EditMode.MatchEngine.Match;

namespace RoadToPro.Tests.EditMode.MatchEngine.Actions
{
    /// <summary>specs/008 T01: 튜닝 기본값 일치 · 공통 수학 · 대체 기술 능력치.</summary>
    public class ActionParamsTests
    {
        [Test]
        public void FileDefaults_EqualCodeDefaults()
        {
            var bundle = MatchTestKit.ActionTuning();
            var fromFile = ActionParams.Create(bundle);
            var n = 0;
            foreach (var f in typeof(ActionParams).GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                if (f.FieldType != typeof(float)) continue;
                Assert.AreEqual((float)f.GetValue(ActionParams.Defaults), (float)f.GetValue(fromFile), f.Name);
                n++;
            }
            Assert.AreEqual(bundle.Registry.Count, n, "튜닝 정의 수 = 코드 필드 수");
        }

        [Test]
        public void Sigmoid_StableAtExtremes()
        {
            Assert.AreEqual(0.5f, ActionMath.Sigmoid(0f), 1e-7f);
            Assert.AreEqual(1f, ActionMath.Sigmoid(1e6f));
            Assert.AreEqual(0f, ActionMath.Sigmoid(-1e6f));
            Assert.IsFalse(float.IsNaN(ActionMath.Sigmoid(float.MaxValue)));
            var p = ActionParams.Defaults;
            Assert.AreEqual(0.999f, ActionMath.Prob(1e6f, p), 1e-7f);
            Assert.AreEqual(0.001f, ActionMath.Prob(-1e6f, p), 1e-7f);
            Assert.AreEqual(0.001f, ActionMath.ClampProb(float.NaN, p.ProbMin, p.ProbMax));
        }

        [Test]
        public void LogitAdd_And_Norm()
        {
            Assert.AreEqual(1f, ActionMath.LogitAdd(0.5f, 1f, 1f), 1e-6f);
            Assert.AreEqual(0f, ActionMath.Norm(-5f));
            Assert.AreEqual(1f, ActionMath.Norm(130f));
            Assert.AreEqual(0.5f, ActionMath.Norm(50f), 1e-6f);
        }

        [Test]
        public void StdNormal_ConsumesExactlyTwo()
        {
            var a = new SeedRng(7);
            var b = new SeedRng(7);
            ActionMath.StdNormal(a);
            b.NextFloat(); b.NextFloat();
            Assert.AreEqual(b.SaveState(), a.SaveState());
        }

        [Test]
        public void TechnicalAttributes_RejectOutOfRange_DefaultIsAverage()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new TechnicalAttributes(101f, 50f, 50f));
            Assert.Throws<ArgumentOutOfRangeException>(() => new TechnicalAttributes(float.NaN, 50f, 50f));
            Assert.Throws<ArgumentOutOfRangeException>(() => new TechnicalAttributes(50f, 50f, 50f, Foot.Right, 6));
            var d = default(TechnicalAttributes).OrAverage();
            Assert.AreEqual(50f, d.Passing);
            Assert.AreEqual(3, d.WeakFoot);
            Assert.AreEqual(100f, TechnicalAttributes.Uniform(20).Passing, 1e-4f);
        }
    }
}
