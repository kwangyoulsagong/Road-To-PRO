using System;
using NUnit.Framework;
using RoadToPro.Core.Logging;
using RoadToPro.Core.Math;
using RoadToPro.MatchEngine.Match.Events;
using RoadToPro.Presentation.Players.Logic;

namespace RoadToPro.Tests.EditMode.Presentation.Players
{
    /// <summary>애니메이션 계획 (specs/043 FR-40~48·60, 비기능: 시뮬 불변).</summary>
    public class AnimPlanTests
    {
        [Test]
        public void Weights_SumToOne_AllEntities()
        {
            var d = SyntheticScenes.Kick(3, SimEventKind.Pass, out _);
            var plan = AnimPlan.Build(AnimTestKit.Lib, AnimTestKit.Map, d, NullLogger.Instance);
            var s = new PoseSample();
            for (int e = 0; e < HighlightData.Entities; e++)
                for (float t = d.StartTime; t <= d.EndTime; t += 0.05f)
                {
                    plan.Evaluate(e, t, ref s);
                    float w = 0;
                    for (int i = 0; i < s.Count; i++) w += s.Weight(i);
                    Assert.That(w, Is.EqualTo(1f).Within(1e-3f), $"e {e} t {t}");
                }
        }

        [Test]
        public void KickAction_FullWeight_AtContact_ClipAtContactFrame()
        {
            var lib = AnimTestKit.Lib;
            var d = SyntheticScenes.Kick(5, SimEventKind.Pass, out var contact);
            var plan = AnimPlan.Build(lib, AnimTestKit.Map, d, NullLogger.Instance);
            Assert.AreEqual(1, plan.Timeline(0).Count);
            var a = plan.Timeline(0).Get(0);
            Assert.That(a.ContactAt, Is.EqualTo(contact).Within(1e-4f));
            var s = new PoseSample();
            plan.Evaluate(0, contact, ref s);
            Assert.AreEqual(a.Clip, s.Clip(0));
            Assert.That(s.Weight(0), Is.EqualTo(1f).Within(1e-3f));
            Assert.That(s.Time(0), Is.EqualTo(lib.Get(a.Clip).ContactTime).Within(1e-4f));
        }

        [Test]
        public void SimData_Unchanged()
        {
            var d = SyntheticScenes.Kick(9, SimEventKind.Shot, out _);
            ulong before = d.Hash();
            var plan = AnimPlan.Build(AnimTestKit.Lib, AnimTestKit.Map, d, NullLogger.Instance);
            var s = new PoseSample();
            for (float t = d.StartTime; t <= d.EndTime; t += 1f / 60f)
                for (int e = 0; e < HighlightData.Entities; e++) plan.Evaluate(e, t, ref s);
            plan.BallVisual(d.StartTime + 1f);
            Assert.AreEqual(before, d.Hash());
        }

        [TestCase(0f, 0f, "fall_n")]
        [TestCase(0f, 90f, "fall_e")]
        [TestCase(90f, 90f, "fall_n")]
        [TestCase(0f, 180f, "fall_s")]
        public void Foul_VictimFalls_AwayFromTackler(float victimYawDeg, float pushDeg, string expect)
        {
            float py = pushDeg * (float)Math.PI / 180f;
            var push = new Vec3((float)Math.Sin(py), 0, (float)Math.Cos(py));
            var d = SyntheticScenes.Foul(1, victimYawDeg * (float)Math.PI / 180f, push, out var time);
            var plan = AnimPlan.Build(AnimTestKit.Lib, AnimTestKit.Map, d, NullLogger.Instance);
            Assert.AreEqual(1, plan.Timeline(0).Count, "피해자(Other)가 넘어진다");
            Assert.AreEqual(expect, AnimTestKit.Lib.Get(plan.Timeline(0).Get(0).Clip).Id);
            Assert.That(plan.Timeline(0).Get(0).Start, Is.EqualTo(time).Within(1e-5f), "피격은 즉시");
        }

        [Test]
        public void UnknownEvent_LogsAndUsesDefault()
        {
            var d = SyntheticScenes.Empty(1, out _);
            d.Events.Add(new AnimEvent(SyntheticScenes.Start + 1f, 1, (SimEventKind)9999, 3, -1, BodyPart.None, ActionOutcome.Success, Vec3.Zero, Vec3.Zero));
            var log = new MemoryLogger(LogLevel.Warning);
            var plan = AnimPlan.Build(AnimTestKit.Lib, AnimTestKit.Map, d, log);
            Assert.AreEqual(1, plan.Timeline(3).Count);
            Assert.AreEqual(AnimTestKit.Map.DefaultClip, plan.Timeline(3).Get(0).Clip);
            Assert.GreaterOrEqual(log.Entries.Count, 1);
        }
    }
}
