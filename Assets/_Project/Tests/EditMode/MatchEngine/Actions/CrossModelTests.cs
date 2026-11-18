using NUnit.Framework;
using RoadToPro.Core.Math;
using RoadToPro.Core.Random;
using RoadToPro.MatchEngine.Actions;

namespace RoadToPro.Tests.EditMode.MatchEngine.Actions
{
    /// <summary>specs/071 FR-01·02·20 · AC-04: 크로스 타입·착지 오차.</summary>
    public class CrossModelTests
    {
        static ActionParams P => ActionTestKit.P;

        [Test]
        public void TypeParams_FR01()
        {
            CrossModel.Speed(P, CrossType.Early, out var lo, out var hi); Assert.AreEqual((20f, 24f), (lo, hi));
            CrossModel.Speed(P, CrossType.Driven, out lo, out hi); Assert.AreEqual((22f, 28f), (lo, hi));
            CrossModel.Speed(P, CrossType.Lofted, out lo, out hi); Assert.AreEqual((15f, 19f), (lo, hi));
            CrossModel.Speed(P, CrossType.Cutback, out lo, out hi); Assert.AreEqual((12f, 16f), (lo, hi));
            CrossModel.Apex(P, CrossType.Early, out lo, out hi); Assert.AreEqual((1.5f, 3f), (lo, hi));
            CrossModel.Apex(P, CrossType.Driven, out lo, out hi); Assert.AreEqual((0.5f, 1.5f), (lo, hi));
            CrossModel.Apex(P, CrossType.Lofted, out lo, out hi); Assert.AreEqual((4f, 8f), (lo, hi));
            Assert.AreEqual(0f, CrossModel.Elevation(P, CrossType.Cutback, 20f));
            Assert.Greater(CrossModel.Elevation(P, CrossType.Lofted, 25f), CrossModel.Elevation(P, CrossType.Early, 25f));
            Assert.Greater(CrossModel.Elevation(P, CrossType.Early, 25f), CrossModel.Elevation(P, CrossType.Driven, 25f));
            Assert.That(CrossModel.Target(P, CrossType.Cutback), Is.InRange(0.35f, 0.45f));
        }

        [Test]
        public void LandingSigma_100k_Within3pct()
        {
            var sigma = CrossModel.SigmaLanding(P, 0.55f, 0.4f);
            Assert.AreEqual((4.5f + (1.2f - 4.5f) * 0.55f) * (1f + 0.6f * 0.4f), sigma, 1e-4f);
            var rng = new SeedRng(7101);
            var mx = new ShotTestKit.Moments();
            var my = new ShotTestKit.Moments();
            for (int i = 0; i < 100_000; i++)
            {
                CrossModel.ApplyLanding(new Vec2(95f, 34f), sigma, rng, out var e);
                mx.Add(e.X);
                my.Add(e.Y);
            }
            Assert.AreEqual(sigma, mx.Std, sigma * 0.03);
            Assert.AreEqual(sigma, my.Std, sigma * 0.03);
        }

        [Test]
        public void Landing_ConsumesExactly4()
        {
            var a = new SeedRng(2);
            var b = new SeedRng(2);
            CrossModel.ApplyLanding(default, 2f, a, out _);
            for (int i = 0; i < CrossModel.RngPerLanding; i++) b.NextFloat();
            Assert.AreEqual(b.NextU64(), a.NextU64());
        }

        [Test]
        public void ChooseType_Situations()
        {
            var w = new float[CrossModel.Types];
            CrossModel.Weights(P, 3f, 10f, 0, w);
            Assert.Greater(w[(int)CrossType.Cutback], 0f, "골라인 근처 + 뒤쪽 동료 → 컷백 후보");
            CrossModel.Weights(P, 20f, 8f, 0, w);
            Assert.AreEqual(0f, w[(int)CrossType.Cutback]);
            Assert.AreEqual(1f, w[(int)CrossType.Early]);
            var lofted0 = w[(int)CrossType.Lofted];
            CrossModel.Weights(P, 20f, 8f, 3, w);
            Assert.Greater(w[(int)CrossType.Lofted], lofted0, "크로스 머신 대체 가중");
            Assert.AreEqual(CrossType.Lofted, CrossModel.Pick(new float[4], 0.3f), "합 0 → Lofted");
            var only = new float[4]; only[(int)CrossType.Driven] = 1f;
            Assert.AreEqual(CrossType.Driven, CrossModel.Pick(only, 0.999f));
        }

        /// <summary>specs/087 FR-04 · AC-03: 컷백 타입 가중치는 튜닝 값(`act.cross.cutbackW`), 기본 3(071 [대체] 상수와 같음), 올리면 컷백 선택 비율이 오른다.</summary>
        [Test]
        public void CutbackWeight_Tunable_DefaultSame_HigherPicksMore()
        {
            var w = new float[CrossModel.Types];
            CrossModel.Weights(P, 3f, 10f, 0, w);
            Assert.AreEqual(3f, w[(int)CrossType.Cutback], "기본값 = 이전 코드 상수 3");
            Assert.AreEqual(3f, P.CrossCutbackW);
            var hi = ActionParams.Create(DuelFixture.TuningWithAction("{\"act.cross.cutbackW\": 9}").Action);
            Assert.AreEqual(9f, hi.CrossCutbackW);
            double Share(ActionParams p)
            {
                var ww = new float[CrossModel.Types];
                CrossModel.Weights(p, 3f, 10f, 0, ww);
                int n = 0;
                for (int i = 0; i < 1000; i++) if (CrossModel.Pick(ww, (i + 0.5f) / 1000f) == CrossType.Cutback) n++;
                return n / 1000.0;
            }
            var s0 = Share(P);
            var s1 = Share(hi);
            TestContext.WriteLine($"컷백 선택 비율 기본 {s0:P1} → 9 {s1:P1}");
            Assert.Greater(s1, s0);
            CrossModel.Weights(hi, 20f, 8f, 0, w);
            Assert.AreEqual(0f, w[(int)CrossType.Cutback], "조건(골라인 거리) 밖이면 가중치와 무관하게 0");
        }
    }
}
