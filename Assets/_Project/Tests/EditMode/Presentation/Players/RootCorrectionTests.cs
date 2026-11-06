using System;
using NUnit.Framework;
using RoadToPro.Core.Logging;
using RoadToPro.Core.Math;
using RoadToPro.MatchEngine.Match.Events;
using RoadToPro.Presentation.Players.Logic;

namespace RoadToPro.Tests.EditMode.Presentation.Players
{
    /// <summary>화면 루트·공 보정 (specs/043 FR-44·46, AC-10).</summary>
    public class RootCorrectionTests
    {
        [Test]
        public void Ramp_Shape()
        {
            var a = new ActionChoice(0, 2f, 1f, 2.4f, 2.8f, 1f, new Vec3(0.2f, 0, 0), false);
            float a0 = 2f - ActionClipSelector.ApproachWindow;
            Assert.AreEqual(0f, RootCorrection.Ramp(a, a0 - 0.01f));
            Assert.AreEqual(1f, RootCorrection.Ramp(a, 2.4f), 1e-5f);
            Assert.AreEqual(0f, RootCorrection.Ramp(a, 2.8f + RootCorrection.Release + 0.01f));
            float prev = 0f;
            for (float t = a0; t <= 2.4f; t += 0.01f)
            {
                float r = RootCorrection.Ramp(a, t);
                Assert.GreaterOrEqual(r, prev - 1e-6f, "들어올 때 단조 증가");
                prev = r;
            }
        }

        [Test]
        public void Clamp_Max03()
        {
            Assert.That(RootCorrection.Clamp(new Vec3(1, 0, 1)).Length(), Is.EqualTo(0.3f).Within(1e-5f));
            Assert.AreEqual(new Vec3(0.1f, 0, 0), RootCorrection.Clamp(new Vec3(0.1f, 0, 0)));
        }

        [Test]
        public void SyntheticHighlight_NoFrameOver03m()
        {
            var lib = AnimTestKit.Lib;
            var kinds = new[] { SimEventKind.Pass, SimEventKind.Shot, SimEventKind.Cross };
            var s = new PoseSample();
            for (int i = 0; i < 30; i++)
            {
                var d = SyntheticScenes.Kick(100 + i, kinds[i % 3], out _);
                var plan = AnimPlan.Build(lib, AnimTestKit.Map, d, NullLogger.Instance);
                for (int e = 0; e < HighlightData.Entities; e++)
                {
                    Vec3 prev = default;
                    for (int k = 0; k <= (int)(SyntheticScenes.Length * 60); k++)
                    {
                        float t = SyntheticScenes.Start + k / 60f;
                        plan.Evaluate(e, t, ref s);
                        float err = (s.Root - d.Pos(e, t)).Length();
                        Assert.That(err, Is.LessThanOrEqualTo(RootCorrection.MaxOffset + 1e-4f), $"장면 {i} 엔티티 {e} t {t}");
                        if (k > 0) Assert.That((s.RootOffset - prev).Length(), Is.LessThanOrEqualTo(0.03f), "보정은 프레임마다 조금씩(순간 이동 없음)");
                        prev = s.RootOffset;
                    }
                }
            }
        }

        [Test]
        public void BallVisual_EqualsSim_AtAndAfterContact()
        {
            var d = SyntheticScenes.Kick(7, SimEventKind.Shot, out var contact);
            var plan = AnimPlan.Build(AnimTestKit.Lib, AnimTestKit.Map, d, NullLogger.Instance);
            for (float t = contact; t < d.EndTime; t += 1f / 60f)
                Assert.That((plan.BallVisual(t) - plan.BallKeyed(t)).Length(), Is.LessThanOrEqualTo(1e-5f));
            var off = BallVisualCorrection.Offset(new Vec3(0.5f, 0, 0), 1f, 0.99f);
            Assert.That(off.Length(), Is.LessThanOrEqualTo(BallVisualCorrection.BallPullMax + 1e-6f));
        }
    }
}
