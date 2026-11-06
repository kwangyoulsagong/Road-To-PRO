using System;
using NUnit.Framework;
using RoadToPro.Core.Logging;
using RoadToPro.Presentation.Players;
using RoadToPro.Presentation.Players.Logic;
using UnityEngine;

namespace RoadToPro.Tests.EditMode.Presentation.Players.Motion
{
    /// <summary>배속·결정론·경계 (specs/054 AC-10·13·18, FR-50·53·80~88).</summary>
    public class MotionSpeedTests
    {
        static readonly string[] Ids = RoadToPro.Verification.Samples.Motion.MotionSceneBuilder.EvidenceIds;

        static System.Collections.Generic.IEnumerable<TestCaseData> Combos()
        {
            foreach (var id in Ids)
                foreach (var rate in MotionRun.Rates)
                    yield return new TestCaseData(id, rate);
        }

        [TestCaseSource(nameof(Combos))]
        public void AllRates_SameLimits(string id, float rate)
        {
            var s = MotionStandardsTests.Load();
            var r = MotionRun.Measure(id, rate);
            var v = r.Violations(s);
            Assert.AreEqual(0, v.Count, $"{id} x{rate}: {MotionFidelityTests.Dump(v)}");
        }

        [Test]
        public void SameSimTime_SamePose_AcrossRates()
        {
            var m = MotionSceneKit.Scene("stopCut");
            var p = AnimPlan.Build(AnimTestKit.Lib, AnimTestKit.Map, m.Data, NullLogger.Instance);
            var a = new PoseSample();
            var b = new PoseSample();
            foreach (var t in new[] { 100.5f, 101.7f, 103.3f, 104.3f, 105.9f })
            {
                p.Evaluate(0, t, ref a);
                // 배속 무관: 평가 전에 다른 시각을 거쳐도 같은 값
                p.Evaluate(0, t * 0.5f + 50f, ref b);
                p.Evaluate(0, t, ref b);
                Assert.AreEqual(a.Hash(), b.Hash());
            }
        }

        [Test]
        public void FixedClock_RateSwitch_NoTimeJump()
        {
            var c = new FixedPlaybackClock { Start = 100f, Fps = 60f, Rate = 1.5f };
            c.Frame = 90;
            float before = c.SimTime;
            c.SwitchRate(3f);
            Assert.AreEqual(before, c.SimTime, 1e-4f);
            c.Frame++;
            Assert.AreEqual(before + 3f / 60f, c.SimTime, 1e-4f);
            c.SwitchRate(0.5f);
            float t2 = c.SimTime;
            c.Frame++;
            Assert.AreEqual(t2 + 0.5f / 60f, c.SimTime, 1e-4f);
        }

        /// <summary>FR-53: 전환 전후 관절 변화 ≤ 같은 속도 재생 최대 변화 × 1.5. 모든 배속 쌍, 여러 관절.</summary>
        [TestCase("straight50", 110f)]
        [TestCase("stopCut", 103.2f)]
        [TestCase("longPass", 102.2f)]
        public void RateSwitch_PoseJump_Bounded(string id, float at)
        {
            var m = MotionSceneKit.Scene(id);
            using var kit = new AnimRigKit(m.Data, 0);
            var rig = kit.Rigs[0];
            var joints = new[] { rig.Bone("LeftUpperLeg"), rig.Bone("LeftLowerLeg"), rig.Bone("RightUpperLeg"), rig.Bone("RightLowerLeg"), rig.Bone("LeftUpperArm"), rig.Bone("Spine") };
            float start = System.Math.Min(at, m.Data.EndTime - 2f);
            float k = MotionStandardsTests.Load().Max("rateSwitchPose");
            var prev = new Quaternion[joints.Length];
            void Save() { for (int j = 0; j < joints.Length; j++) prev[j] = joints[j].localRotation; }
            float Step()
            {
                float w = 0f;
                for (int j = 0; j < joints.Length; j++) w = Mathf.Max(w, Quaternion.Angle(prev[j], joints[j].localRotation));
                return w;
            }
            foreach (var from in MotionRun.Rates)
                foreach (var to in MotionRun.Rates)
                {
                    if (from == to) continue;
                    var c = new FixedPlaybackClock { Start = start, Fps = 60f, Rate = from };
                    kit.System.ApplyAt(c.SimTime);
                    for (int i = 0; i < 10; i++) { c.Frame++; kit.System.ApplyAt(c.SimTime); }
                    Save();
                    c.SwitchRate(to);
                    c.Frame++;
                    kit.System.ApplyAt(c.SimTime);
                    float jump = Step();
                    // 전환 뒤 속도(to)로 정상 재생하는 동안의 최대 변화
                    float steady = 0f;
                    for (int i = 0; i < 20; i++)
                    {
                        Save();
                        c.Frame++;
                        kit.System.ApplyAt(c.SimTime);
                        steady = Mathf.Max(steady, Step());
                    }
                    Assert.LessOrEqual(jump, Mathf.Max(steady * k, 1f), $"{from}x → {to}x");
                }
        }

        // --- 결정론 AC-13 ---

        [Category("Determinism")]
        [TestCase("tackle")]
        [TestCase("stopCut")]
        public void TwoRuns_SameClipsMetrics(string id)
        {
            var a = MotionRun.Measure(MotionSceneKit.Scene(id));
            var b = MotionRun.Measure(MotionSceneKit.Scene(id));
            CollectionAssert.AreEqual(a.ClipSequence, b.ClipSequence);
            Assert.AreEqual(a.Hash(), b.Hash());
        }

        [Test, Category("Determinism")]
        public void Plan_TwoBuilds_SamePoseHash()
        {
            var d = MotionSceneKit.Scene("tackle").Data;
            var p = AnimPlan.Build(AnimTestKit.Lib, AnimTestKit.Map, d, NullLogger.Instance);
            var q = AnimPlan.Build(AnimTestKit.Lib, AnimTestKit.Map, d, NullLogger.Instance);
            var a = new PoseSample();
            var b = new PoseSample();
            for (int e = 0; e < 4; e++)
                for (float t = d.StartTime; t < d.EndTime; t += 0.1f)
                {
                    p.Evaluate(e, t, ref a);
                    q.Evaluate(e, t, ref b);
                    Assert.AreEqual(a.Hash(), b.Hash());
                }
        }

        [Test, Category("Determinism")]
        public void Playback_KeepsSimHash()
        {
            var m = MotionSceneKit.Scene("header");
            ulong h = m.Data.Hash();
            MotionRun.Measure(m, 3f);
            Assert.AreEqual(h, m.Data.Hash());
        }

        [Test]
        public void Evaluate_Allocates_Nothing()
        {
            var d = MotionSceneKit.Scene("tackle").Data;
            var p = AnimPlan.Build(AnimTestKit.Lib, AnimTestKit.Map, d, NullLogger.Instance);
            var s = new PoseSample();
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int k = 0; k < 500; k++)
            {
                p.Evaluate(k % 4, d.StartTime + k * 0.01f, ref s);
                p.Plants.Query(k % 4, true, d.StartTime + k * 0.01f, out _, out _, out _);
                p.BallVisual(d.StartTime + k * 0.01f);
            }
            Assert.AreEqual(0, GC.GetAllocatedBytesForCurrentThread() - before);
        }

        /// <summary>T23: Mono 에디터 값은 기록만. 판정은 IL2CPP 벤치 (D17·043과 같은 규칙).</summary>
        [Test]
        public void MotionCost_Recorded_Mono()
        {
            var m = MotionSceneKit.Scene("tackle");
            int[] all = new int[HighlightData.Entities];
            for (int i = 0; i < all.Length; i++) all[i] = i;
            using var kit = new AnimRigKit(m.Data, all);
            kit.System.TimeMotion = true;
            var ms = new System.Collections.Generic.List<float>();
            for (int k = 0; k < 300; k++)
            {
                kit.System.ApplyAt(m.Data.StartTime + k * 0.025f);
                if (k >= 30) ms.Add((float)kit.System.LastMotionMs);
            }
            TestContext.WriteLine($"054 처리 25명 Mono: p95 {RoadToPro.Presentation.Players.Logic.Motion.MotionStats.Percentile(ms, 95):0.000} ms, 최대 {RoadToPro.Presentation.Players.Logic.Motion.MotionStats.Percentile(ms, 100):0.000} ms");
            Assert.Greater(ms.Count, 0);
        }

        /// <summary>같은 리그로 다른 시각을 거쳐 와도 같은 포즈 (믹서 슬롯 순서 이력, 되감기·리플레이).</summary>
        [Test]
        public void SameRig_SeekAndReturn_SamePose()
        {
            var m = MotionSceneKit.Scene("stopCut");
            using var kit = new AnimRigKit(m.Data, 0);
            var rig = kit.Rigs[0];
            string[] bones = { "Hips", "Spine", "Chest", "LeftUpperLeg", "LeftLowerLeg", "LeftFoot", "RightUpperLeg", "RightLowerLeg", "RightFoot", "LeftUpperArm", "RightLowerArm", "Head" };
            foreach (var t in new[] { 100.7f, 103.4f, 104.1f, 105.2f })
            {
                kit.System.ApplyAt(t);
                var a = new Quaternion[bones.Length];
                var pa = rig.Bone("LeftToes").position;
                for (int i = 0; i < bones.Length; i++) a[i] = rig.Bone(bones[i]).rotation;
                // 다른 클립 조합을 거친다 → 슬롯 배치가 달라진다
                foreach (var o in new[] { 100.1f, 104.6f, 102f, 105.9f, 103.2f }) kit.System.ApplyAt(o);
                kit.System.ApplyAt(t);
                for (int i = 0; i < bones.Length; i++)
                    Assert.LessOrEqual(Quaternion.Angle(a[i], rig.Bone(bones[i]).rotation), 0.02f, $"t={t} {bones[i]}");
                Assert.LessOrEqual((pa - rig.Bone("LeftToes").position).magnitude, 1e-3f, $"t={t} 발끝");
            }
        }

        [Test]
        public void PoseSample_SameClipTwice_Merged_WeightKept()
        {
            // 같은 터치 클립이 짧은 간격으로 연속: 합이 1이고 같은 클립이 두 번 들어가지 않는다
            var d = new HighlightData(0f, 61);
            for (int f = 0; f < 61; f++) d.Set(f, 0, 0f, f * 0.1f, 0f);
            var r = new RoadToPro.Core.Math.Vec3(-0.1f, 0.11f, 0.5f);
            d.Events.Add(new AnimEvent(0.8f, 1, RoadToPro.MatchEngine.Match.Events.SimEventKind.Dribble, 0, -1, RoadToPro.MatchEngine.Match.Events.BodyPart.RightFoot, ActionOutcome.Success, r, default));
            d.Events.Add(new AnimEvent(0.95f, 2, RoadToPro.MatchEngine.Match.Events.SimEventKind.Dribble, 0, -1, RoadToPro.MatchEngine.Match.Events.BodyPart.RightFoot, ActionOutcome.Success, r, default));
            var p = AnimPlan.Build(AnimTestKit.Lib, AnimTestKit.Map, d, NullLogger.Instance);
            var s = new PoseSample();
            for (float t = 0.5f; t < 1.3f; t += 0.01f)
            {
                p.Evaluate(0, t, ref s);
                float sum = 0f;
                for (int i = 0; i < s.Count; i++)
                {
                    sum += s.Weight(i);
                    for (int j = i + 1; j < s.Count; j++) Assert.AreNotEqual(s.Clip(i), s.Clip(j), $"t={t}");
                }
                Assert.AreEqual(1f, sum, 2e-3f, $"t={t}");
            }
        }

        /// <summary>같은 장면을 다른 시작 시각에 둬도 디딤 계획이 같다 (접촉 창 float 틈, 결정론 리뷰).</summary>
        [TestCase(0f)]
        [TestCase(10f)]
        [TestCase(30f)]
        [TestCase(60f)]
        public void Plants_SameAcrossStartTime(float start)
        {
            var src = MotionSceneKit.Scene("longPass").Data;
            var d = new HighlightData(start, src.Frames);
            Array.Copy(src.X, d.X, src.X.Length);
            Array.Copy(src.Z, d.Z, src.Z.Length);
            Array.Copy(src.Yaw, d.Yaw, src.Yaw.Length);
            Array.Copy(src.Present, d.Present, src.Present.Length);
            Array.Copy(src.Ball, d.Ball, src.Ball.Length);
            foreach (var ev in src.Events)
                d.Events.Add(new AnimEvent(ev.Time - src.StartTime + start, ev.Seq, ev.Kind, ev.Entity, ev.Other, ev.Part, ev.Outcome, ev.BallPos, ev.BallVelOut));
            var a = AnimPlan.Build(AnimTestKit.Lib, AnimTestKit.Map, src, NullLogger.Instance).Plants.Plants(0, true);
            var b = AnimPlan.Build(AnimTestKit.Lib, AnimTestKit.Map, d, NullLogger.Instance).Plants.Plants(0, true);
            Assert.AreEqual(a.Length, b.Length);
            float ca = src.Events[0].Time, cb = d.Events[0].Time;
            for (int i = 0; i < a.Length; i++)
            {
                // 일반 디딤은 프레임 반올림으로 한 프레임까지 다를 수 있지만, 접촉 디딤은 접촉 시각으로 고정돼 같아야 한다
                Assert.AreEqual(a[i].X, b[i].X, 0.2f, $"#{i}");
                Assert.AreEqual(a[i].Z, b[i].Z, 0.2f, $"#{i}");
            }
            RoadToPro.Presentation.Players.Logic.Motion.FootPlant? pa = null, pb = null;
            foreach (var q in a) if (q.T0 <= ca && ca < q.T1) pa = q;
            foreach (var q in b) if (q.T0 <= cb && cb < q.T1) pb = q;
            Assert.IsTrue(pa.HasValue && pb.HasValue, $"디딤발이 접촉을 지난다: {pa.HasValue} {pb.HasValue}");
            Assert.AreEqual(pa.Value.X, pb.Value.X, 0.02f, "접촉 디딤");
            Assert.AreEqual(pa.Value.Z, pb.Value.Z, 0.02f, "접촉 디딤");
        }

        [Test]
        public void BallKeyed_PastSceneEnd_Clamped()
        {
            var d = MotionSceneKit.Scene("longPass").Data;
            var k = new RoadToPro.Presentation.Players.Logic.Motion.ContactKeys(d);
            Assert.AreEqual(0f, (k.Ball(d.EndTime + 5f) - k.Ball(d.EndTime)).Length(), 1e-4f);
            Assert.AreEqual(0f, (k.Ball(d.StartTime - 5f) - k.Ball(d.StartTime)).Length(), 1e-4f);
        }

        // --- 경계 AC-18 ---

        [Test]
        public void FlyingStart_FirstFrameAtSpeed_NoIdle()
        {
            var m = MotionSceneKit.Scene("flyingStart");
            var p = AnimPlan.Build(AnimTestKit.Lib, AnimTestKit.Map, m.Data, NullLogger.Instance);
            var s = new PoseSample();
            p.Evaluate(0, m.Data.StartTime, ref s);
            for (int i = 0; i < s.Count; i++) Assert.AreNotEqual("idle", p.Library.Get(s.Clip(i)).Id, "FR-84 첫 프레임 정지 자세 금지");
            Assert.Greater(p.SpeedAt(0, m.Data.StartTime), 5f);
        }

        /// <summary>FR-88: 프레임이 늦어도 재생 시계 기준을 지킨다. 접촉 시각 차는 렌더 격자(45 fps)까지 포함해 판정.</summary>
        [TestCase("tackle")]
        [TestCase("oneTouch")]
        [TestCase("straight50")]
        public void LowFps_45_SameContactAndSlide(string id)
        {
            var s = MotionStandardsTests.Load();
            var r = MotionRun.Measure(id, 1.5f, 45f);
            var v = r.Violations(s);
            Assert.AreEqual(0, v.Count, MotionFidelityTests.Dump(v));
        }

        [Test]
        public void ScaledRig_ContactWithinLimits()
        {
            var m = MotionSceneKit.Scene("longPass");
            var scale = new float[HighlightData.Entities];
            scale[0] = 1.08f;
            var r = MotionRun.Measure(m, 1.5f, 60f, scale);
            foreach (var c in r.Contacts) Assert.LessOrEqual(c.Distance, c.DistanceLimit, "FR-87 접촉 경로의 키 배율");
        }

        [Test]
        public void FastSlow_TopSpeedsReached_AllLimits()
        {
            var m = MotionSceneKit.Scene("fastSlow");
            var p = AnimPlan.Build(AnimTestKit.Lib, AnimTestKit.Map, m.Data, NullLogger.Instance);
            Assert.Greater(p.SpeedAt(0, m.Data.StartTime + 7f), 9f, "최고 능력치 ≈ 9.7 m/s");
            Assert.Less(p.SpeedAt(1, m.Data.StartTime + 7f), 8f, "최저 능력치 ≈ 7.2 m/s");
            var v = MotionRun.Measure("fastSlow").Violations(MotionStandardsTests.Load());
            Assert.AreEqual(0, v.Count, MotionFidelityTests.Dump(v));
        }

        [Test]
        public void ExtremeBody_ScaledRigs_NoViolations()
        {
            var m = MotionSceneKit.Scene("fastSlow");
            var scale = new float[HighlightData.Entities];
            scale[0] = 2.05f / 1.8f;
            scale[1] = 1.60f / 1.8f;
            var r = MotionRun.Measure(m, 1.5f, 60f, scale);
            var v = r.Violations(MotionStandardsTests.Load());
            Assert.AreEqual(0, v.Count, "FR-87 " + MotionFidelityTests.Dump(v));
        }
    }
}
