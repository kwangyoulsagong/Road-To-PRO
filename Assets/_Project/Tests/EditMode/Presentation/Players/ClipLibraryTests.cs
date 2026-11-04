using NUnit.Framework;
using RoadToPro.MatchEngine.Match.Events;
using RoadToPro.Presentation.Players.Logic;

namespace RoadToPro.Tests.EditMode.Presentation.Players
{
    /// <summary>클립 태그 로드 (specs/043 FR-32).</summary>
    public class ClipLibraryTests
    {
        [Test]
        public void Parses_AllClips_WithTags()
        {
            var lib = AnimTestKit.Lib;
            Assert.GreaterOrEqual(lib.Count, 75);
            Assert.IsTrue(lib.TryGet("pass_inside_r", out var p));
            Assert.IsTrue(p.HasContact);
            Assert.AreEqual(BodyPart.RightFoot, p.ContactPart);
            Assert.AreEqual(12, p.ContactFrame);
            Assert.That(p.ContactTime, Is.EqualTo(12f / 30f).Within(1e-6f));
            Assert.That(p.ContactPoint.Y, Is.EqualTo(0.11f).Within(0.012f));
            Assert.AreEqual("RightFoot", p.Rule.Bone);
            Assert.IsTrue(lib.TryGet("walk_f", out var w));
            Assert.IsTrue(w.Loop);
            Assert.AreEqual("walk", w.Gait);
            Assert.That(w.Speed, Is.EqualTo(1.4f).Within(1e-4f));
            Assert.AreEqual(0, w.Dir);
            Assert.IsTrue(lib.TryGet("fall_e", out var f));
            Assert.AreEqual(90, f.HitDir);
            Assert.AreEqual(-1, lib.IndexOf("moonwalk"));
        }

        [Test]
        public void LocomotionGrid_Complete()
        {
            var lib = AnimTestKit.Lib;
            foreach (var g in new[] { Gait.Walk, Gait.Jog, Gait.Sprint })
                for (int d = 0; d < 8; d++)
                    Assert.GreaterOrEqual(lib.Locomotion(g, d), 0, $"{g} 방향 {d}");
            Assert.GreaterOrEqual(lib.IdleIndex, 0);
        }
    }
}
