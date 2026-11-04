using NUnit.Framework;
using RoadToPro.Core.Logging;
using RoadToPro.Presentation.Players.Logic;

namespace RoadToPro.Tests.EditMode.Presentation.Players
{
    /// <summary>로코모션 블렌드 (specs/043 FR-41·62).</summary>
    public class LocomotionBlendTests
    {
        static float Sum(in LocoSample s)
        {
            float t = 0;
            for (int i = 0; i < s.Count; i++) t += s.Weight(i);
            return t;
        }

        static float WeightOf(in LocoSample s, string id)
        {
            var lib = AnimTestKit.Lib;
            float t = 0;
            for (int i = 0; i < s.Count; i++)
                if (lib.Get(s.Clip(i)).Id == id) t += s.Weight(i);
            return t;
        }

        [Test]
        public void ExactClipSpeed_SingleClip()
        {
            var lib = AnimTestKit.Lib;
            var s = LocomotionBlend.Evaluate(lib, 1.4f, 0f, 0.3f);
            Assert.That(WeightOf(s, "walk_f"), Is.EqualTo(1f).Within(1e-5f));
            s = LocomotionBlend.Evaluate(lib, 7.6f, 0f, 0.3f);
            Assert.That(WeightOf(s, "sprint_f"), Is.EqualTo(1f).Within(1e-5f));
        }

        [Test]
        public void Weights_SumToOne_EverywhereAndNonNegative()
        {
            var lib = AnimTestKit.Lib;
            for (float v = 0; v <= 9.5f; v += 0.25f)
                for (float d = 0; d < 360; d += 7.5f)
                {
                    var s = LocomotionBlend.Evaluate(lib, v, d, 0.5f);
                    Assert.That(Sum(s), Is.EqualTo(1f).Within(1e-4f), $"v {v} dir {d}");
                    for (int i = 0; i < s.Count; i++) Assert.GreaterOrEqual(s.Weight(i), 0f);
                }
        }

        [Test]
        public void Between_Gaits_And_Dirs()
        {
            var lib = AnimTestKit.Lib;
            var s = LocomotionBlend.Evaluate(lib, 2.4f, 0f, 0f);
            Assert.That(WeightOf(s, "walk_f"), Is.EqualTo(0.5f).Within(1e-4f));
            Assert.That(WeightOf(s, "jog_f"), Is.EqualTo(0.5f).Within(1e-4f));
            // 22.5°: 정면·대각 앞 반반 (그 방향의 걷기 속도 기준)
            float vw = LocomotionBlend.GaitSpeed(lib, Gait.Walk, 22.5f);
            s = LocomotionBlend.Evaluate(lib, vw, 22.5f, 0f);
            Assert.That(WeightOf(s, "walk_f"), Is.EqualTo(0.5f).Within(1e-3f));
            Assert.That(WeightOf(s, "walk_fr"), Is.EqualTo(0.5f).Within(1e-3f));
            s = LocomotionBlend.Evaluate(lib, 0f, 0f, 0f);
            Assert.That(WeightOf(s, "idle"), Is.EqualTo(1f).Within(1e-5f));
        }

        [Test]
        public void PhaseRate_MatchesStride()
        {
            var lib = AnimTestKit.Lib;
            lib.TryGet("jog_f", out var j);
            float stride = j.Speed * j.Length;
            Assert.That(LocomotionBlend.PhaseRate(lib, j.Speed, 0f), Is.EqualTo(j.Speed / stride).Within(1e-4f));
            Assert.That(LocomotionBlend.PhaseRate(lib, 0f, 0f), Is.EqualTo(0f));
        }

        [Test]
        public void SpeedLimiter_ClampsJump_AndLogs()
        {
            var log = new MemoryLogger(LogLevel.Warning);
            float v = LocomotionBlend.LimitSpeed(0f, 8f, 1f / 30f, log);
            Assert.That(v, Is.LessThanOrEqualTo(LocomotionBlend.MaxBlendAccel / 30f + 1e-4f));
            Assert.AreEqual(1, log.Entries.Count, "시뮬 오류 진단 로그");
            Assert.AreEqual(3.1f, LocomotionBlend.LimitSpeed(3f, 3.1f, 1f / 30f, log), 1e-5f);
            Assert.AreEqual(1, log.Entries.Count);
        }
    }
}
