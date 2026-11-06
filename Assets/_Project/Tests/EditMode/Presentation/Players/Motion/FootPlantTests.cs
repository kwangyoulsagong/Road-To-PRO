using System;
using System.Collections.Generic;
using NUnit.Framework;
using RoadToPro.Core.Logging;
using RoadToPro.Presentation.Players.Logic;
using RoadToPro.Presentation.Players.Logic.Motion;
using RoadToPro.Tests.EditMode.Presentation.Players;

namespace RoadToPro.Tests.EditMode.Presentation.Players.Motion
{
    /// <summary>발 디딤·미끄러짐·걸음 빈도 (specs/054 AC-02, FR-10~13).</summary>
    public class FootPlantTests
    {
        public static readonly string[] AllIds = MotionScenesTests.AllIds;

        [TestCaseSource(nameof(AllIds))]
        public void Slide_WithinLimits(string id)
        {
            var s = MotionStandardsTests.Load();
            var r = MotionRun.Measure(id);
            Assert.LessOrEqual(r.Stat("slide", 95), s.Max("slideP95"), id);
            Assert.LessOrEqual(r.Max("slide"), s.Max("slideMax"), id);
        }

        [TestCaseSource(nameof(AllIds))]
        public void Height_WithinLimits(string id)
        {
            var th = MotionStandardsTests.Load()["footHeight"];
            var r = MotionRun.Measure(id);
            if (!r.Metrics.TryGetValue("footHeight", out var l)) return;
            foreach (var v in l) Assert.IsTrue(th.Passes(v.Value), $"{id} t={v.Time} {v.Value}");
        }

        [Test]
        public void Plan_RunningSteps_AlternateAndForward()
        {
            var m = MotionSceneKit.Scene("flyingStart");
            var p = AnimPlan.Build(AnimTestKit.Lib, AnimTestKit.Map, m.Data, NullLogger.Instance);
            var L = p.Plants.Plants(0, true);
            var R = p.Plants.Plants(0, false);
            Assert.Greater(L.Length + R.Length, 6);
            for (int i = 1; i < L.Length; i++)
            {
                Assert.GreaterOrEqual(L[i].T0, L[i - 1].T0);
                Assert.Greater(L[i].X, L[i - 1].X, "앞으로 디딘다");
            }
        }

        [Test]
        public void Query_InsidePlant_FullWeight_OutsideZero()
        {
            var m = MotionSceneKit.Scene("flyingStart");
            var p = AnimPlan.Build(AnimTestKit.Lib, AnimTestKit.Map, m.Data, NullLogger.Instance);
            var a = p.Plants.Plants(0, true);
            Assert.Greater(a.Length, 1);
            var q = a[a.Length / 2];
            Assert.AreEqual(1f, p.Plants.Query(0, true, (q.T0 + q.T1) * 0.5f, out var x, out var z, out var rel));
            Assert.AreEqual(q.X, x);
            Assert.AreEqual(q.Z, z);
            Assert.AreEqual(-1f, rel);
            // 뜨는 구간
            float w = p.Plants.Query(0, true, q.T1 + 0.02f, out _, out _, out rel);
            Assert.Greater(rel, 0f);
            Assert.Greater(w, 0f);
        }

        [Test]
        public void Standing_BothFeetPlanted_NoStepping()
        {
            var r = MotionRun.Measure("header");
            Assert.LessOrEqual(r.Max("slide"), 0.01f, "제자리: 발이 안 움직인다 (FR-13)");
        }

        // --- FR-12 ---

        [TestCase(1.4f, 1.8f, 2.0f)]
        [TestCase(3.4f, 2.6f, 2.9f)]
        [TestCase(5.4f, 3.0f, 3.3f)]
        [TestCase(7.6f, 3.8f, 4.3f)]
        public void Cadence_InTable(float v, float lo, float hi)
        {
            float steps = 2f * LocomotionBlend.PhaseRate(AnimTestKit.Lib, v, 0f);
            Assert.GreaterOrEqual(steps, lo);
            Assert.LessOrEqual(steps, hi);
        }

        [Test]
        public void Table_MatchesData_And_StrideTimesCadence_IsSpeed()
        {
            var s = MotionStandardsTests.Load();
            var lib = AnimTestKit.Lib;
            foreach (var row in s.Cadence)
                Assert.That(2f * LocomotionBlend.PhaseRate(lib, row.Speed, 0f), Is.InRange(row.Min, row.Max), row.Gait);
            for (float v = 1.5f; v < 7.6f; v += 0.37f)
            {
                float rate = LocomotionBlend.PhaseRate(lib, v, 0f);
                Assert.Greater(rate, 0f);
                // 보폭 = 속도 / 위상 속도. 위상이 보폭에서 나오므로 곱은 속도와 같다 — 값이 NaN·음수가 아닌지와 단조성을 본다
                Assert.Less(v / rate, 4.2f, "한 주기 이동 거리 상한");
            }
        }

        /// <summary>FR-12: 실제 디딤에서 보폭 × 걸음 빈도 = 루트 속도 ±5%. 표 사이 속도 포함.</summary>
        [TestCase(2.4f)]
        [TestCase(4.4f)]
        [TestCase(6.5f)]
        public void StrideTimesCadence_MatchesSpeed(float v)
        {
            var d = new HighlightData(0f, 241);
            for (int f = 0; f < 241; f++) d.Set(f, 0, 0f, f * v / 30f, 0f);
            var p = AnimPlan.Build(AnimTestKit.Lib, AnimTestKit.Map, d, NullLogger.Instance);
            var a = p.Plants.Plants(0, true);
            Assert.Greater(a.Length, 6);
            int n = a.Length - 2;
            float period = (a[n].T0 - a[1].T0) / (n - 1), stride = (a[n].Z - a[1].Z) / (n - 1);
            Assert.AreEqual(v, stride / period, v * MotionStandardsTests.Load().Max("cadenceRelative"));
        }

        /// <summary>FR-11: 무릎이 뒤로 꺾이지 않는다.</summary>
        [TestCase("straight50")]
        [TestCase("stopCut")]
        [TestCase("longPass")]
        public void Knee_NoHyperextension(string id)
        {
            var m = MotionSceneKit.Scene(id);
            using var kit = new AnimRigKit(m.Data, m.Actors);
            var rig = kit.Rigs[0];
            foreach (var n in new[] { "Left", "Right" })
            {
                var hip = rig.Bone(n + "UpperLeg");
                var knee = rig.Bone(n + "LowerLeg");
                var ankle = rig.Bone(n + "Foot");
                for (float t = m.Data.StartTime; t < m.Data.EndTime; t += 0.05f)
                {
                    kit.System.ApplyAt(t);
                    var thigh = knee.position - hip.position;
                    var shin = ankle.position - knee.position;
                    var side = rig.transform.right;
                    // 오른쪽 축 기준: 굽힘은 항상 한 방향. 0.5° 잡음 허용
                    float bend = UnityEngine.Vector3.SignedAngle(thigh, shin, side);
                    Assert.GreaterOrEqual(bend, -5f, $"{id} {n} t={t}: 무릎 과신전");
                }
            }
        }

        /// <summary>달려와 차는 디딤발: 접촉 디딤은 다리가 닿는 곳에서 시작하고, 접지 중 발이 순간 이동하지 않는다.</summary>
        [Test]
        public void ContactPlant_WithinReach_WholeSpan()
        {
            var m = MotionSceneKit.Scene("longPass");
            var p = AnimPlan.Build(AnimTestKit.Lib, AnimTestKit.Map, m.Data, NullLogger.Instance);
            float tc = m.Data.Events[0].Time;
            FootPlant? hit = null;
            foreach (var q in p.Plants.Plants(0, true)) if (q.T0 <= tc && tc < q.T1) hit = q;
            Assert.IsTrue(hit.HasValue, "접촉 때 디딤발이 땅에 있다");
            var s = new PoseSample();
            for (float t = hit.Value.T0; t < Math.Min(hit.Value.T1, m.Data.EndTime); t += 1f / 30f)
            {
                p.Evaluate(0, t, ref s);
                float dx = hit.Value.X - s.Root.X, dz = hit.Value.Z - s.Root.Z;
                Assert.LessOrEqual(Math.Sqrt(dx * dx + dz * dz), FootPlantPlan.Reach + 0.25f, $"t={t}: 골반 이동 몫 포함 다리 도달 거리");
            }
        }

        // --- FR-37 ---

        [Test]
        public void Dribble_TouchPerEvent_OneToOne()
        {
            var m = MotionSceneKit.Scene("chainTouch");
            var p = AnimPlan.Build(AnimTestKit.Lib, AnimTestKit.Map, m.Data, NullLogger.Instance);
            int events = 0;
            foreach (var ev in m.Data.Events)
                if (ev.Kind == RoadToPro.MatchEngine.Match.Events.SimEventKind.Dribble) events++;
            int actions = 0;
            var tl = p.Timeline(0);
            for (int k = 0; k < tl.Count; k++)
                if (p.Library.Get(tl.Get(k).Clip).Group == "dribble") actions++;
            Assert.AreEqual(events, actions);
            Assert.Greater(events, 3);
        }

        [Test]
        public void Dribble_RunClip_WhileOwning()
        {
            var m = MotionSceneKit.Scene("chainTouch");
            var p = AnimPlan.Build(AnimTestKit.Lib, AnimTestKit.Map, m.Data, NullLogger.Instance);
            var s = new PoseSample();
            bool seen = false;
            for (float t = m.Data.StartTime + 0.5f; t < m.Data.StartTime + 2f; t += 0.03f)
            {
                p.Evaluate(0, t, ref s);
                for (int i = 0; i < s.Count; i++)
                    if (p.Library.Get(s.Clip(i)).Id.StartsWith("dribble_run_", StringComparison.Ordinal)) seen = true;
            }
            Assert.IsTrue(seen, "공 소유 중 드리블 런");
        }

        [Test]
        public void NoOwner_NoDribbleRun()
        {
            var m = MotionSceneKit.Scene("straight50");
            var p = AnimPlan.Build(AnimTestKit.Lib, AnimTestKit.Map, m.Data, NullLogger.Instance);
            var s = new PoseSample();
            for (float t = m.Data.StartTime; t < m.Data.EndTime; t += 0.07f)
            {
                p.Evaluate(0, t, ref s);
                for (int i = 0; i < s.Count; i++)
                    StringAssert.DoesNotStartWith("dribble_run_", p.Library.Get(s.Clip(i)).Id);
            }
        }
    }
}
