using System;
using NUnit.Framework;
using RoadToPro.MatchEngine.Goalkeeper;

namespace RoadToPro.Tests.EditMode.Presentation.Players
{
    /// <summary>specs/080 AC-01: 011 FR-90 GK 동작 종류마다 매핑된 클립, 다이빙 좌/우 × 3높이 × 2도달.</summary>
    public class KeeperClipCoverageTests
    {
        [Test]
        public void EveryKeeperMotionKind_HasClip()
        {
            var map = AnimTestKit.Map;
            var lib = AnimTestKit.Lib;
            foreach (KeeperMotionKind k in Enum.GetValues(typeof(KeeperMotionKind)))
            {
                if (k == KeeperMotionKind.None) continue;
                int n = 0;
                foreach (DiveHeight b in Enum.GetValues(typeof(DiveHeight)))
                    foreach (var i in map.KeeperCandidates(k, b))
                    {
                        Assert.That(i, Is.InRange(0, lib.Count - 1), k.ToString());
                        n++;
                    }
                Assert.Greater(n, 0, k + " 클립 없음");
            }
        }

        [Test]
        public void DiveClips_TwoSidesThreeHeightsTwoReaches()
        {
            var map = AnimTestKit.Map;
            var lib = AnimTestKit.Lib;
            var names = new[] { (DiveHeight.Low, "low"), (DiveHeight.Mid, "mid"), (DiveHeight.High, "high") };
            foreach (var (band, h) in names)
            {
                var c = map.KeeperCandidates(KeeperMotionKind.Dive, band);
                Assert.AreEqual(4, c.Count, h);
                foreach (var r in new[] { "short", "long" })
                foreach (var s in new[] { "l", "r" })
                {
                    var id = $"gk_dive_{h}_{r}_{s}";
                    int idx = lib.IndexOf(id);
                    Assert.GreaterOrEqual(idx, 0, id);
                    CollectionAssert.Contains(c, idx, id);
                    var info = lib.Get(idx);
                    Assert.IsTrue(info.HasContact, id);
                    Assert.That(info.ContactPoint.X * (s == "l" ? -1f : 1f), Is.GreaterThan(0.2f), id + " 손이 다이빙 쪽");
                    Assert.Greater(info.ArmLength, 0f, id + " IK 태그");
                }
            }
        }
    }
}
