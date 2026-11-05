using System;
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

        // ---- 055: run 단계 · 원형 블렌드 (FR-01·50~52) ----

        [Test]
        public void RunLevel_Forward_UsesRunClip()
        {
            var lib = AnimTestKit.Lib;
            var s = LocomotionBlend.Evaluate(lib, 5.4f, 0f, 0.3f);
            Assert.That(WeightOf(s, "run_f"), Is.EqualTo(1f).Within(1e-5f));
        }

        /// <summary>run 클립이 없는 방향: run 단계 몫을 jog·sprint로 속도 비례 대체 — 쓰이는 클립은 그 방향 jog·sprint뿐.</summary>
        [Test]
        public void RunSubstitution_SideDir_OnlyJogAndSprint()
        {
            var lib = AnimTestKit.Lib;
            for (float v = 3.4f; v <= 7.6f; v += 0.2f)
            {
                var s = LocomotionBlend.Evaluate(lib, v * 0.6f, 90f, 0.2f);
                Assert.That(Sum(s), Is.EqualTo(1f).Within(1e-4f));
                for (int i = 0; i < s.Count; i++)
                {
                    var id = lib.Get(s.Clip(i)).Id;
                    Assert.IsTrue(id == "jog_r" || id == "sprint_r", $"v {v}: {id}");
                }
            }
        }

        /// <summary>위상 속도는 속도·방향에 대해 연속(run 단계가 생겨도 45°·90°에서 점프 없음).</summary>
        [Test]
        public void RunSubstitution_PhaseRate_Continuous()
        {
            var lib = AnimTestKit.Lib;
            foreach (var dir in new[] { 0f, 22.5f, 45f, 67.5f, 90f })
            {
                float prev = LocomotionBlend.PhaseRate(lib, 2f, dir);
                for (float v = 2.01f; v <= 9f; v += 0.01f)
                {
                    float r = LocomotionBlend.PhaseRate(lib, v, dir);
                    Assert.That(Math.Abs(r - prev), Is.LessThan(0.02f), $"dir {dir} v {v}");
                    prev = r;
                }
            }
            for (float d = 0; d < 90; d += 0.5f)
                Assert.That(Math.Abs(LocomotionBlend.PhaseRate(lib, 5.4f, d + 0.5f) - LocomotionBlend.PhaseRate(lib, 5.4f, d)), Is.LessThan(0.02f), $"d {d}");
        }

        [Test]
        public void StyleBlend_ForwardSprint_UsesStyleClipsByJitter()
        {
            var lib = AnimTestKit.Lib;
            var st = lib.Style(new RunStyleChoice(RunArchetype.R2, KickArchetype.None, 0.25f));
            Assert.IsTrue(st.Valid);
            lib.TryGet("sprint_f_r2_lo", out var lo);
            lib.TryGet("sprint_f_r2_hi", out var hi);
            float v = lo.Speed * 0.75f + hi.Speed * 0.25f;
            var s = LocomotionBlend.Evaluate(lib, v, 0f, 0.4f, 0f, st);
            Assert.That(WeightOf(s, "sprint_f_r2_lo"), Is.EqualTo(0.75f).Within(1e-3f));
            Assert.That(WeightOf(s, "sprint_f_r2_hi"), Is.EqualTo(0.25f).Within(1e-3f));
            s = LocomotionBlend.Evaluate(lib, 5.4f, 0f, 0.4f, 0f, st);
            Assert.That(WeightOf(s, "run_f_r2"), Is.EqualTo(1f).Within(1e-3f));
            // 옆 방향은 공유 클립
            s = LocomotionBlend.Evaluate(lib, 4f, 90f, 0.4f, 0f, st);
            for (int i = 0; i < s.Count; i++) StringAssert.DoesNotContain("_r2", lib.Get(s.Clip(i)).Id);
        }

        [Test]
        public void StyleBlend_SlotsAtMost5_WeightsSumOne_AllStyles()
        {
            var lib = AnimTestKit.Lib;
            for (var r = RunArchetype.R1; r <= RunArchetype.GK; r++)
            {
                var st = lib.Style(new RunStyleChoice(r, KickArchetype.None, 0.6f));
                Assert.IsTrue(st.Valid, r.ToString());
                for (float v = 0; v <= 10f; v += 0.25f)
                    for (float d = 0; d < 360; d += 15f)
                    {
                        var s = LocomotionBlend.Evaluate(lib, v, d, 0.5f, 0f, st);
                        Assert.LessOrEqual(s.Count, LocoSample.Capacity);
                        Assert.That(Sum(s), Is.EqualTo(1f).Within(1e-4f), $"{r} v {v} d {d}");
                        Assert.That(LocomotionBlend.PhaseRate(lib, v, d, st), Is.GreaterThanOrEqualTo(0f));
                    }
            }
        }

        [Test]
        public void StyleBlend_NoGcAlloc()
        {
            var lib = AnimTestKit.Lib;
            var st = lib.Style(new RunStyleChoice(RunArchetype.R5, KickArchetype.None, 0.3f));
            LocomotionBlend.Evaluate(lib, 6f, 10f, 0.1f, 0f, st);
            long b0 = GC.GetAllocatedBytesForCurrentThread();
            float acc = 0;
            for (int i = 0; i < 2000; i++)
            {
                var s = LocomotionBlend.Evaluate(lib, i * 0.005f, i * 0.37f, i * 0.01f, 0f, st);
                acc += s.Weight(0) + LocomotionBlend.PhaseRate(lib, i * 0.005f, i * 0.37f, st);
            }
            Assert.AreEqual(0, GC.GetAllocatedBytesForCurrentThread() - b0, "할당 " + acc);
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
