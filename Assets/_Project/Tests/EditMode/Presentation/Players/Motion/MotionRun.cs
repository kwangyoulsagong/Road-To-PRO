using System;
using System.Text;
using NUnit.Framework;
using RoadToPro.Core.Logging;
using RoadToPro.Presentation.Players.Logic;
using RoadToPro.Presentation.Players.Motion;
using RoadToPro.Verification.Samples.Motion;
using UnityEngine;

namespace RoadToPro.Tests.EditMode.Presentation.Players.Motion
{
    /// <summary>테스트 공용: 장면 하나를 실제 리그로 재생하며 측정.</summary>
    internal static class MotionRun
    {
        /// <summary>장면 측정은 무겁지 않지만 여러 테스트가 같은 값을 본다 — 장면·배속·fps별로 한 번만 잰다(결정론 테스트는 Measure 직접 호출).</summary>
        static readonly System.Collections.Generic.Dictionary<(string, float, float), MotionReport> Cache = new System.Collections.Generic.Dictionary<(string, float, float), MotionReport>();

        public static MotionReport Measure(string id, float rate = 1.5f, float fps = 60f)
        {
            var k = (id, rate, fps);
            if (Cache.TryGetValue(k, out var r)) return r;
            return Cache[k] = Measure(MotionSceneKit.Scene(id), rate, fps);
        }

        public static readonly float[] Rates = { 1f, 1.5f, 2f, 3f, 0.5f };
        public static readonly RoadToPro.Core.Math.Vec3[] GoalPosts =
        {
            new RoadToPro.Core.Math.Vec3(-52.5f, 0f, -3.66f), new RoadToPro.Core.Math.Vec3(-52.5f, 2.44f, -3.66f),
            new RoadToPro.Core.Math.Vec3(-52.5f, 0f, 3.66f), new RoadToPro.Core.Math.Vec3(-52.5f, 2.44f, 3.66f),
        };

        public static MotionReport Measure(MotionScene m, float rate = 1.5f, float fps = 60f, float[] scale = null)
        {
            using var kit = new AnimRigKit(m.Data, scale, m.Actors);
            var meter = new MotionMeter(kit.Plan, kit.Rigs, m.Actors, MotionStandardsTests.Load(), m.Id, rate, fps) { Posts = GoalPosts };
            kit.System.Diagnostics = meter;
            var d = m.Data;
            long n = (long)Math.Floor((d.EndTime - d.StartTime) * fps / rate);
            for (long k = 0; k <= n; k++) kit.System.ApplyAt(d.StartTime + (float)(k * (double)rate / fps));
            meter.Finish();
            kit.System.Diagnostics = null;
            meter.MeasureContacts(kit.System.ApplyAt);
            return meter.Report;
        }

        public static string Summary(MotionReport r)
        {
            var sb = new StringBuilder();
            sb.Append($"[{r.Scene} x{r.Rate}] seq={string.Join(",", r.ClipSequence)} ");
            foreach (var k in new[] { "rootError", "rootSpeedStep", "yawError", "yawDelta", "slide", "footHeight", "torsoOverlap", "limbPenetration", "ballAfterContact", "ballPull" })
                if (r.Metrics.ContainsKey(k)) sb.Append($"{k} p95 {r.Stat(k, 95):0.000} max {r.Max(k):0.000} | ");
            foreach (var c in r.Contacts)
                sb.Append($"\n   접촉 e{c.Entity} {c.Kind} {c.Clip} dt {c.TimeError * 1000:0}ms dist {c.Distance * 100:0.0}cm root {c.RootError * 100:0.0}cm part {c.ClipPart}/{c.SimPart} miss {c.Missed} plant {c.PlantSide * 100:0}/{c.PlantBack * 100:0}");
            return sb.ToString();
        }
    }
}
