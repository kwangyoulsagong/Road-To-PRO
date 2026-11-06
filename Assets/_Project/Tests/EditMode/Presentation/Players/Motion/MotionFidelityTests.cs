using System;
using System.Collections.Generic;
using NUnit.Framework;
using RoadToPro.MatchEngine.Match.Events;
using RoadToPro.Presentation.Players.Logic;
using RoadToPro.Presentation.Players.Logic.Motion;
using RoadToPro.Presentation.Players.Motion;

namespace RoadToPro.Tests.EditMode.Presentation.Players.Motion
{
    /// <summary>궤적·방향 충실도 (specs/054 AC-01·03, FR-01~05·25).</summary>
    public class MotionFidelityTests
    {
        public static readonly string[] AllIds = MotionScenesTests.AllIds;

        internal static string Dump(List<MotionViolation> v)
        {
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < v.Count && i < 8; i++) sb.Append($"{v[i].Metric} t={v[i].Time:0.00} e{v[i].Entity} {v[i].Value:0.000} > {v[i].Limit:0.000}; ");
            return sb.ToString();
        }

        [TestCaseSource(nameof(AllIds))]
        public void Scene_NoViolations_At1_5x(string id)
        {
            var r = MotionRun.Measure(id);
            var v = r.Violations(MotionStandardsTests.Load());
            Assert.AreEqual(0, v.Count, $"{id}: {Dump(v)}");
        }

        [TestCaseSource(nameof(AllIds))]
        public void RootError_WithinLimits(string id)
        {
            var s = MotionStandardsTests.Load();
            var r = MotionRun.Measure(id);
            Assert.LessOrEqual(r.Stat("rootError", 95), s.Max("rootErrorP95"));
            Assert.LessOrEqual(r.Max("rootError"), s.Max("rootErrorMax"));
            foreach (var c in r.Contacts)
                if (!c.Missed) Assert.LessOrEqual(c.RootError, s.Max("rootErrorContact"), $"{id} {c.Clip}");
        }

        [TestCaseSource(nameof(AllIds))]
        public void Yaw_Delta_ErrorWithinLimits(string id)
        {
            var s = MotionStandardsTests.Load();
            var r = MotionRun.Measure(id);
            Assert.LessOrEqual(r.Max("yawDelta"), s.Max("yawFrameDelta"), "AC-03");
            Assert.LessOrEqual(r.Stat("yawError", 95), s.Max("yawErrorP95"), "FR-05");
        }

        [Test]
        public void YawTrack_InstantFlip_LimitedRate()
        {
            var d = new HighlightData(0f, 31);
            for (int f = 0; f < 31; f++) d.Set(f, 0, 0f, 0f, f < 5 ? 0f : (float)Math.PI);
            var y = new YawTrack(d);
            float prev = y.At(0, 0f);
            float maxPerFrame = 0f;
            for (int k = 1; k <= 60; k++)
            {
                float cur = y.At(0, k / 60f);
                maxPerFrame = Math.Max(maxPerFrame, Math.Abs((float)Math.IEEERemainder(cur - prev, 2 * Math.PI)) * 180f / (float)Math.PI);
                prev = cur;
            }
            Assert.LessOrEqual(maxPerFrame, 20f + 1e-3f);
            Assert.AreEqual(Math.PI, Math.Abs(Math.IEEERemainder(y.At(0, d.EndTime), 2 * Math.PI)), 1e-3, "결국 시뮬 방향에 도달");
        }

        [Test]
        public void YawTrack_Across2Pi_ShortestWay()
        {
            var d = new HighlightData(0f, 11);
            for (int f = 0; f < 11; f++) d.Set(f, 0, 0f, 0f, f < 3 ? 3.1f : -3.1f);
            var y = new YawTrack(d);
            float a = y.At(0, 2f / 30f), b = y.At(0, 4f / 30f);
            Assert.Less(Math.Abs((float)Math.IEEERemainder(b - a, 2 * Math.PI)), 0.2f, "0.08 rad만 도는 짧은 길");
        }

        [Test]
        public void SimPlayback_DoesNotChangeRecord()
        {
            var m = MotionSceneKit.Scene("tackle");
            ulong before = m.Data.Hash();
            MotionRun.Measure(m);
            Assert.AreEqual(before, m.Data.Hash(), "FR-04");
        }

        [Test]
        public void MotionTrack_StraightRun_SpeedAndHeading()
        {
            var d = new HighlightData(0f, 61);
            for (int f = 0; f < 61; f++) d.Set(f, 0, f * 0.2f, 0f, (float)(Math.PI / 2));
            var t = new MotionTrack(d);
            Assert.AreEqual(6f, t.Speed(30, 0), 1e-2f);
            Assert.AreEqual(Math.PI / 2, t.Heading(30, 0), 1e-3);
            Assert.AreEqual(0f, t.AccelAlong(30, 0), 1e-3f);
            Assert.AreEqual(0f, t.AccelLat(30, 0), 1e-3f);
            Assert.IsFalse(t.HasFault(0));
        }

        [Test]
        public void MotionTrack_Teleport_ClassifiedAsSimFault()
        {
            var r = MotionRun.Measure("simFault");
            Assert.Contains(0, new List<int>(r.SimFaultEntities), "FR-80");
            Assert.IsFalse(r.Metrics.ContainsKey("rootError"), "시뮬 오류 엔티티는 054 지표에서 뺀다");
            Assert.Greater(r.Max("simFaultError"), 0.3f, "최대 오차를 넘겨도 부드럽게 따라간다");
        }

        [Test]
        public void FaultFollower_RenderSpeedWithinLimit()
        {
            var m = MotionSceneKit.Scene("simFault");
            var plan = AnimPlan.Build(AnimTestKit.Lib, AnimTestKit.Map, m.Data, RoadToPro.Core.Logging.NullLogger.Instance);
            var s = new PoseSample();
            plan.Evaluate(0, m.Data.StartTime, ref s);
            var prev = s.Root;
            float worst = 0f;
            for (int k = 1; k < 200; k++)
            {
                plan.Evaluate(0, m.Data.StartTime + k / 60f, ref s);
                worst = Math.Max(worst, (s.Root - prev).Length() * 60f);
                prev = s.Root;
            }
            Assert.LessOrEqual(worst, MotionDefaults.MaxSimSpeed + 0.5f);
        }
    }
}
