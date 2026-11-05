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
            Assert.GreaterOrEqual(lib.Locomotion(Gait.Run, 0), 0, "run 정면 (055)");
        }

        /// <summary>055 새 태그: 원형(style·variant)·킥 원형·속도 곡선. 클립 117개 (plan 클립 목록).</summary>
        [Test]
        public void Parses_055Tags_StyleVariantKickSpeedCurve()
        {
            var lib = AnimTestKit.Lib;
            Assert.AreEqual(117, lib.Count);
            Assert.IsTrue(lib.TryGet("sprint_f_r1_lo", out var lo));
            Assert.AreEqual("r1", lo.Style);
            Assert.AreEqual("lo", lo.Variant);
            Assert.AreEqual("sprint", lo.Gait);
            Assert.IsTrue(lib.TryGet("run_f_gk", out var gk));
            Assert.AreEqual("gk", gk.Style);
            Assert.IsTrue(lib.TryGet("shot_power_r", out var k1));
            Assert.AreEqual(KickArchetype.K1, k1.KickStyle);
            Assert.IsTrue(lib.TryGet("shot_curl_r", out var k3));
            Assert.AreEqual(KickArchetype.K3, k3.KickStyle);
            Assert.IsTrue(lib.TryGet("pass_inside_r", out var pi));
            Assert.AreEqual(KickArchetype.None, pi.KickStyle);
            Assert.IsTrue(lib.TryGet("start_f", out var st));
            Assert.IsNotNull(st.SpeedCurve);
            Assert.GreaterOrEqual(st.SpeedCurve.Length, 4);
            // 원형 클립은 공유 로코모션 격자에 들어가지 않는다
            Assert.AreEqual("sprint_f", lib.Get(lib.Locomotion(Gait.Sprint, 0)).Id);
            Assert.AreEqual("run_f", lib.Get(lib.Locomotion(Gait.Run, 0)).Id);
            for (var r = RunArchetype.R1; r <= RunArchetype.GK; r++)
                Assert.IsTrue(lib.Style(new RunStyleChoice(r, KickArchetype.None, 0f)).Valid, r.ToString());
        }
    }
}
