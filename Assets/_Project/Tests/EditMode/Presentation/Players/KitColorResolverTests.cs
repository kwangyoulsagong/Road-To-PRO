using NUnit.Framework;
using RoadToPro.Presentation.Players.Logic;

namespace RoadToPro.Tests.EditMode.Presentation.Players
{
    /// <summary>키트 색 충돌 (specs/043 FR-22, AC-04). ΔE2000은 Sharma·Wu·Dalal(2005) 표 34쌍으로 검증.</summary>
    public class KitColorResolverTests
    {
        // L1 a1 b1 L2 a2 b2 ΔE00
        static readonly double[,] Sharma =
        {
            { 50.0000, 2.6772, -79.7751, 50.0000, 0.0000, -82.7485, 2.0425 },
            { 50.0000, 3.1571, -77.2803, 50.0000, 0.0000, -82.7485, 2.8615 },
            { 50.0000, 2.8361, -74.0200, 50.0000, 0.0000, -82.7485, 3.4412 },
            { 50.0000, -1.3802, -84.2814, 50.0000, 0.0000, -82.7485, 1.0000 },
            { 50.0000, -1.1848, -84.8006, 50.0000, 0.0000, -82.7485, 1.0000 },
            { 50.0000, -0.9009, -85.5211, 50.0000, 0.0000, -82.7485, 1.0000 },
            { 50.0000, 0.0000, 0.0000, 50.0000, -1.0000, 2.0000, 2.3669 },
            { 50.0000, -1.0000, 2.0000, 50.0000, 0.0000, 0.0000, 2.3669 },
            { 50.0000, 2.4900, -0.0010, 50.0000, -2.4900, 0.0009, 7.1792 },
            { 50.0000, 2.4900, -0.0010, 50.0000, -2.4900, 0.0010, 7.1792 },
            { 50.0000, 2.4900, -0.0010, 50.0000, -2.4900, 0.0011, 7.2195 },
            { 50.0000, 2.4900, -0.0010, 50.0000, -2.4900, 0.0012, 7.2195 },
            { 50.0000, -0.0010, 2.4900, 50.0000, 0.0009, -2.4900, 4.8045 },
            { 50.0000, -0.0010, 2.4900, 50.0000, 0.0010, -2.4900, 4.8045 },
            { 50.0000, -0.0010, 2.4900, 50.0000, 0.0011, -2.4900, 4.7461 },
            { 50.0000, 2.5000, 0.0000, 50.0000, 0.0000, -2.5000, 4.3065 },
            { 50.0000, 2.5000, 0.0000, 73.0000, 25.0000, -18.0000, 27.1492 },
            { 50.0000, 2.5000, 0.0000, 61.0000, -5.0000, 29.0000, 22.8977 },
            { 50.0000, 2.5000, 0.0000, 56.0000, -27.0000, -3.0000, 31.9030 },
            { 50.0000, 2.5000, 0.0000, 58.0000, 24.0000, 15.0000, 19.4535 },
            { 50.0000, 2.5000, 0.0000, 50.0000, 3.1736, 0.5854, 1.0000 },
            { 50.0000, 2.5000, 0.0000, 50.0000, 3.2972, 0.0000, 1.0000 },
            { 50.0000, 2.5000, 0.0000, 50.0000, 1.8634, 0.5757, 1.0000 },
            { 50.0000, 2.5000, 0.0000, 50.0000, 3.2592, 0.3350, 1.0000 },
            { 60.2574, -34.0099, 36.2677, 60.4626, -34.1751, 39.4387, 1.2644 },
            { 63.0109, -31.0961, -5.8663, 62.8187, -29.7946, -4.0864, 1.2630 },
            { 61.2901, 3.7196, -5.3901, 61.4292, 2.2480, -4.9620, 1.8731 },
            { 35.0831, -44.1164, 3.7933, 35.0232, -40.0716, 1.5901, 1.8645 },
            { 22.7233, 20.0904, -46.6940, 23.0331, 14.9730, -42.5619, 2.0373 },
            { 36.4612, 47.8580, 18.3852, 36.2715, 50.5065, 21.2231, 1.4146 },
            { 90.8027, -2.0831, 1.4410, 91.1528, -1.6435, 0.0447, 1.4441 },
            { 90.9257, -0.5406, -0.9208, 88.6381, -0.8985, -0.7239, 1.5381 },
            { 6.7747, -0.2908, -2.4247, 5.8714, -0.0985, -2.2286, 0.6377 },
            { 2.0776, 0.0795, -1.1350, 0.9033, -0.0636, -0.5514, 0.9082 },
        };

        [Test]
        public void DeltaE2000_SharmaTable()
        {
            for (int i = 0; i < Sharma.GetLength(0); i++)
            {
                double d = CieDe2000.DeltaE(new Lab(Sharma[i, 0], Sharma[i, 1], Sharma[i, 2]), new Lab(Sharma[i, 3], Sharma[i, 4], Sharma[i, 5]));
                Assert.That(d, Is.EqualTo(Sharma[i, 6]).Within(1e-4), "쌍 " + (i + 1));
                double r = CieDe2000.DeltaE(new Lab(Sharma[i, 3], Sharma[i, 4], Sharma[i, 5]), new Lab(Sharma[i, 0], Sharma[i, 1], Sharma[i, 2]));
                Assert.That(r, Is.EqualTo(d).Within(1e-9), "대칭 " + (i + 1));
            }
        }

        [Test]
        public void Srgb_ToLab_KnownValues()
        {
            var w = CieDe2000.ToLab(Rgb.FromHex(0xFFFFFF));
            Assert.That(w.L, Is.EqualTo(100.0).Within(0.01));
            Assert.That(w.A, Is.EqualTo(0.0).Within(0.01));
            var r = CieDe2000.ToLab(Rgb.FromHex(0xFF0000));
            Assert.That(r.L, Is.EqualTo(53.24).Within(0.05));
            Assert.That(r.A, Is.EqualTo(80.09).Within(0.05));
            Assert.That(r.B, Is.EqualTo(67.20).Within(0.05));
        }

        static TeamKits Team(uint home, uint second, uint third, uint keeper) => new TeamKits(
            new KitColors(Rgb.FromHex(home), Rgb.FromHex(0xFFFFFF), Rgb.FromHex(0x111111)),
            new KitColors(Rgb.FromHex(second), Rgb.FromHex(0x111111), Rgb.FromHex(0xFFFFFF)),
            new KitColors(Rgb.FromHex(third), Rgb.FromHex(0xFFFFFF), Rgb.FromHex(0x111111)),
            new KitColors(Rgb.FromHex(keeper), Rgb.FromHex(0x111111), Rgb.FromHex(0xFFFFFF)));

        [Test]
        public void DistinctPrimaries_AwayKeepsHomeKit()
        {
            var r = KitColorResolver.Resolve(Team(0xC8102E, 0xFFFFFF, 0x222222, 0x00A651), Team(0x1C3F94, 0xFFD100, 0xFFFFFF, 0xFF6F00));
            Assert.AreEqual(0, r.AwayKitIndex);
            Assert.That(CieDe2000.DeltaE(r.Home.Primary, r.Away.Primary), Is.GreaterThanOrEqualTo(KitColorResolver.MinDeltaE));
        }

        [Test]
        public void SimilarPrimaries_AwaySwitches()
        {
            // 빨강 vs 거의 같은 빨강 → 원정 2nd(흰색)
            var r = KitColorResolver.Resolve(Team(0xC8102E, 0xFFFFFF, 0x222222, 0x00A651), Team(0xD01030, 0xF5F5F5, 0x0A2240, 0xFF6F00));
            Assert.AreEqual(1, r.AwayKitIndex);
            Assert.That(CieDe2000.DeltaE(r.Home.Primary, r.Away.Primary), Is.GreaterThanOrEqualTo(KitColorResolver.MinDeltaE));
        }

        [Test]
        public void ThirdKit_WhenSecondAlsoClose()
        {
            var r = KitColorResolver.Resolve(Team(0xC8102E, 0xFFFFFF, 0x222222, 0x00A651), Team(0xD01030, 0xC81530, 0x0A2240, 0xFF6F00));
            Assert.AreEqual(2, r.AwayKitIndex);
        }

        [Test]
        public void GkAndReferee_Distinct()
        {
            // 원정 GK 키트가 홈 필드와 같은 색이면 팔레트에서 다른 색으로 바뀐다
            var r = KitColorResolver.Resolve(Team(0x00A651, 0xFFFFFF, 0x222222, 0x1C3F94), Team(0x1C3F94, 0xFFD100, 0xFFFFFF, 0x00A651));
            var field = new[] { r.Home.Primary, r.Away.Primary };
            foreach (var other in new[] { r.HomeKeeper.Primary, r.AwayKeeper.Primary, r.Referee.Primary })
                foreach (var f in field)
                    Assert.That(CieDe2000.DeltaE(other, f), Is.GreaterThanOrEqualTo(KitColorResolver.MinDeltaE));
            Assert.That(CieDe2000.DeltaE(r.HomeKeeper.Primary, r.Referee.Primary), Is.GreaterThanOrEqualTo(KitColorResolver.MinDeltaE));
            Assert.That(CieDe2000.DeltaE(r.AwayKeeper.Primary, r.Referee.Primary), Is.GreaterThanOrEqualTo(KitColorResolver.MinDeltaE));
            Assert.That(CieDe2000.DeltaE(r.HomeKeeper.Primary, r.AwayKeeper.Primary), Is.GreaterThanOrEqualTo(KitColorResolver.MinDeltaE));
        }

        [Test]
        public void Resolve_IsDeterministic()
        {
            var h = Team(0xC8102E, 0xFFFFFF, 0x222222, 0x00A651);
            var a = Team(0xD01030, 0xC81530, 0x0A2240, 0xFF6F00);
            Assert.AreEqual(KitColorResolver.Resolve(h, a), KitColorResolver.Resolve(h, a));
        }
    }
}
