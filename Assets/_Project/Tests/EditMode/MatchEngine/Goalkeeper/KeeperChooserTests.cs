using NUnit.Framework;
using RoadToPro.Core.Math;
using RoadToPro.Core.Random;
using RoadToPro.Core.Tuning;
using RoadToPro.MatchEngine.Goalkeeper;
using RoadToPro.Tests.EditMode.MatchEngine.Match;

namespace RoadToPro.Tests.EditMode.MatchEngine.Goalkeeper
{
    /// <summary>specs/011 FR-94 · AC-27: 시나리오 의도는 효용 보너스로만, 음수 기대효용 후보는 의도가 있어도 고르지 않음.</summary>
    public class KeeperChooserTests
    {
        static GoalkeeperParams P => GoalkeeperParams.Defaults;

        static KeeperCandidate C(KeeperActionKind k, float u, float x = 2f) => new KeeperCandidate { Kind = k, Point = new Vec2(x, 34f), Utility = u };

        static int Count(KeeperChooser ch, KeeperCandidate[] c, KeeperIntent intent, int pickIndex, ulong seed, int n = 2000)
        {
            var rng = new SeedRng(seed);
            int hits = 0;
            for (int i = 0; i < n; i++) if (ch.Choose(c, c.Length, in intent, 0.5f, rng) == pickIndex) hits++;
            return hits;
        }

        [Test]
        public void NegativeUtility_NotChosenEvenWithIntent()
        {
            var ch = new KeeperChooser(P);
            var c = new[] { C(KeeperActionKind.Position, 0.5f), C(KeeperActionKind.Contest, -0.2f, 8f) };
            var intent = new KeeperIntent { Has = true, Kind = KeeperActionKind.Contest, Point = new Vec2(8f, 34f), InWindow = true };
            Assert.AreEqual(0, Count(ch, c, intent, 1, 11UL), "기대효용 음수 → 의도 보너스가 있어도 0회");
        }

        [Test]
        public void ZeroBonus_EqualsPlainChoice()
        {
            var bundle = TuningLoader.ParseBundle(TuningLoader.LoadDefinitionsFile(System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), "Data", "tuning", "goalkeeper.json")),
                "{\"version\":1,\"values\":[{\"id\":\"gk.choice.wIntent\",\"value\":0}]}");
            var zero = new KeeperChooser(GoalkeeperParams.Create(bundle));
            var c = new[] { C(KeeperActionKind.Position, 0.5f), C(KeeperActionKind.Contest, 0.4f, 8f), C(KeeperActionKind.MoveTo, 0.45f, 5f) };
            var intent = new KeeperIntent { Has = true, Kind = KeeperActionKind.MoveTo, Point = new Vec2(5f, 34f), InWindow = true };
            var a = new SeedRng(5UL);
            var b = new SeedRng(5UL);
            for (int i = 0; i < 500; i++)
                Assert.AreEqual(zero.Choose(c, 3, in intent, 0.5f, a), zero.Choose(c, 3, default, 0.5f, b), $"draw {i}");
        }

        [Test]
        public void IntentBonus_RaisesShare()
        {
            var ch = new KeeperChooser(P);
            var c = new[] { C(KeeperActionKind.Position, 0.5f), C(KeeperActionKind.MoveTo, 0.45f, 5f) };
            var intent = new KeeperIntent { Has = true, Kind = KeeperActionKind.MoveTo, Point = new Vec2(5f, 34f), InWindow = true };
            var with = Count(ch, c, intent, 1, 21UL);
            var without = Count(ch, c, default, 1, 21UL);
            Assert.Greater(with, without);
        }

        [Test]
        public void OutsideIntent_OnlyBeyondThetaOpp()
        {
            var ch = new KeeperChooser(P);
            var intent = new KeeperIntent { Has = true, Kind = KeeperActionKind.MoveTo, Point = new Vec2(5f, 34f), InWindow = true };
            // 의도 후보 점수 0.9/1 + 0.3 = 1.2, 경합 1.0 → 1.0 < 1.2 + 0.2 → 경합은 집합 밖
            var c = new[] { C(KeeperActionKind.Position, 0.1f), C(KeeperActionKind.MoveTo, 0.9f, 5f), C(KeeperActionKind.Contest, 1.0f, 8f) };
            Assert.AreEqual(0, Count(ch, c, intent, 2, 31UL), "θ_opp 안쪽 의도 밖 후보는 선택 안 됨");
            Assert.AreEqual(0, Count(ch, c, intent, 0, 31UL), "위치 선정(의도 밖)도 마찬가지");
        }

        [Test]
        public void Match_DistanceAndWindow()
        {
            var ch = new KeeperChooser(P);
            var intent = new KeeperIntent { Has = true, Kind = KeeperActionKind.MoveTo, Point = new Vec2(5f, 34f), InWindow = true };
            Assert.AreEqual(1f, ch.Match(C(KeeperActionKind.MoveTo, 0f, 5f), intent), 1e-6f);
            Assert.AreEqual(0.6f, ch.Match(C(KeeperActionKind.MoveTo, 0f, 7f), intent), 1e-5f, "r_intent 5 m, 2 m 떨어짐");
            Assert.AreEqual(0f, ch.Match(C(KeeperActionKind.Contest, 0f, 5f), intent), "종류 다르면 0");
            intent.InWindow = false;
            Assert.AreEqual(0.5f, ch.Match(C(KeeperActionKind.MoveTo, 0f, 5f), intent), 1e-6f, "창 밖 감쇠");
        }
    }
}
