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
            int nonKeeper = 0;
            for (int i = 0; i < lib.Count; i++) if (!lib.Get(i).Id.StartsWith("gk_", System.StringComparison.Ordinal)) nonKeeper++;
            Assert.AreEqual(113, nonKeeper, "055 117 + 054 컷 4 − GK 8 (GK 클립은 080 KeeperClipCoverageTests)");
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

        [TestCase("cut_l45", -45f, false)]
        [TestCase("cut_r45", 45f, true)]
        [TestCase("cut_l90", -90f, false)]
        [TestCase("cut_r90", 90f, true)]
        public void CutClips_TagsAndPlantFoot(string id, float deg, bool plantLeft)
        {
            Assert.IsTrue(AnimTestKit.Lib.TryGet(id, out var c), id);
            Assert.AreEqual(deg, c.Cut);
            Assert.AreEqual(plantLeft, c.PlantLeft);
            var spans = plantLeft ? c.StanceL : c.StanceR;
            Assert.IsTrue(System.Array.Exists(spans, s => s.F0 <= c.PlantFrame && c.PlantFrame <= s.F1), $"{id}: 디딤 프레임이 접지 구간 안");
        }

        [Test]
        public void StancePaths_LoopHasOneSpanPerFoot_MovesBackward()
        {
            Assert.IsTrue(AnimTestKit.Lib.TryGet("jog_f", out var c));
            Assert.AreEqual(1, c.StanceL.Length);
            Assert.AreEqual(1, c.StanceR.Length);
            Assert.Less(c.StanceL[0].Z1, c.StanceL[0].Z0, "접지 발은 몸 기준 뒤로 간다");
            float dist = c.StanceL[0].Z0 - c.StanceL[0].Z1, time = (c.StanceL[0].F1 - c.StanceL[0].F0) / 30f;
            Assert.AreEqual(c.Speed, dist / time, c.Speed * 0.15f, "접지 동안 뒤로 가는 속도 ≈ 클립 속도");
        }

        [Test]
        public void StancePaths_KickPlantFoot_CoversContact()
        {
            Assert.IsTrue(AnimTestKit.Lib.TryGet("pass_inside_r", out var c));
            Assert.IsTrue(System.Array.Exists(c.StanceL, s => s.F0 <= c.ContactFrame && c.ContactFrame <= s.F1), "디딤발(왼발)은 접촉 때 땅에 있다");
        }
        /// <summary>080 FR-04: 손 접촉 클립은 어깨 2점·팔 길이 태그가 있고, 접촉점이 어깨에서 팔 길이 + 0.30 m 안이다.</summary>
        [Test]
        public void HandClip_HasShouldersAndArmLength()
        {
            var lib = AnimTestKit.Lib;
            int n = 0;
            for (int i = 0; i < lib.Count; i++)
            {
                var c = lib.Get(i);
                if (!c.HasContact || c.ContactPart != BodyPart.Hand) continue;
                n++;
                Assert.That(c.ArmLength, Is.InRange(0.45f, 0.70f), c.Id);
                float d = System.Math.Min((c.ContactPoint - c.ShoulderL).Length(), (c.ContactPoint - c.ShoulderR).Length());
                Assert.LessOrEqual(d, c.ArmLength + 0.30f, c.Id);
                Assert.Greater(System.Math.Max(c.ShoulderL.Y, c.ShoulderR.Y), 0.3f, c.Id + " 어깨 높이 (누운 다이빙은 아래쪽 어깨가 바닥 근처)");
            }
            Assert.Greater(n, 0, "손 접촉 클립");
        }

        [Test]
        public void NonHandClip_NoReachTag()
        {
            var lib = AnimTestKit.Lib;
            for (int i = 0; i < lib.Count; i++)
            {
                var c = lib.Get(i);
                if (c.HasContact && c.ContactPart == BodyPart.Hand) continue;
                Assert.AreEqual(0f, c.ArmLength, c.Id);
            }
        }
    }
}
