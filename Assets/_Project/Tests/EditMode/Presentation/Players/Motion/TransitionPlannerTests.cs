using System;
using System.Collections.Generic;
using NUnit.Framework;
using RoadToPro.Core.Logging;
using RoadToPro.Core.Math;
using RoadToPro.MatchEngine.Match.Events;
using RoadToPro.Presentation.Players.Logic;
using RoadToPro.Presentation.Players.Logic.Motion;

namespace RoadToPro.Tests.EditMode.Presentation.Players.Motion
{
    /// <summary>전환 동작·자세 (specs/054 FR-13·20~24, AC-09).</summary>
    public class TransitionPlannerTests
    {
        static AnimPlan Plan(string id) =>
            AnimPlan.Build(AnimTestKit.Lib, AnimTestKit.Map, MotionSceneKit.Scene(id).Data, NullLogger.Instance);

        static List<string> Clips(AnimPlan p, int e)
        {
            var o = new List<string>();
            foreach (var t in p.Transitions(e)) o.Add(p.Library.Get(t.Clip).Id);
            return o;
        }

        [Test]
        public void Straight50_HasStart_NoTurns()
        {
            var c = Clips(Plan("straight50"), 0);
            CollectionAssert.Contains(c, "start_f");
            CollectionAssert.DoesNotContain(c, "turn_180");
        }

        [Test]
        public void StopCut_HasBrakeAndDirectionChange()
        {
            var c = Clips(Plan("stopCut"), 0);
            Assert.IsTrue(c.Contains("stop_f") || c.Contains("quick_stop"), string.Join(",", c));
            Assert.IsTrue(c.Exists(x => x.StartsWith("cut_", StringComparison.Ordinal) || x.StartsWith("turn_", StringComparison.Ordinal)), string.Join(",", c));
        }

        [Test]
        public void TurnBackSide_Has180()
        {
            var c = Clips(Plan("turnBackSide"), 0);
            CollectionAssert.Contains(c, "turn_180", string.Join(",", c));
        }

        /// <summary>FR-24: 백페달·사이드스텝은 다른 방향 클립, 제자리 방향 전환은 스텝 턴 (FR-13).</summary>
        [Test]
        public void Backpedal_Sidestep_UseDirectionalClips()
        {
            var p = Plan("turnBackSide");
            var s = new PoseSample();
            bool back = false, side = false;
            for (float t = 105f; t < 106.3f; t += 0.03f)
            {
                p.Evaluate(0, t, ref s);
                for (int i = 0; i < s.Count; i++)
                    if (p.Library.Get(s.Clip(i)).Id.EndsWith("_b", StringComparison.Ordinal) && s.Weight(i) > 0.3f) back = true;
            }
            for (float t = 107.5f; t < 108.8f; t += 0.03f)
            {
                p.Evaluate(0, t, ref s);
                for (int i = 0; i < s.Count; i++)
                {
                    int dir = p.Library.Get(s.Clip(i)).Dir;
                    if (dir >= 45 && dir <= 315 && dir != 180 && s.Weight(i) > 0.3f) side = true;
                }
            }
            Assert.IsTrue(back, "백페달");
            Assert.IsTrue(side, "사이드스텝");
        }

        [Test]
        public void StandingPivot_StepTurn()
        {
            var d = new HighlightData(0f, 91);
            for (int f = 0; f < 91; f++) d.Set(f, 0, 0f, 0f, f < 30 ? 0f : Math.Min(1f, (f - 30) / 15f) * (float)(Math.PI / 2));
            var p = AnimPlan.Build(AnimTestKit.Lib, AnimTestKit.Map, d, NullLogger.Instance);
            CollectionAssert.Contains(Clips(p, 0), "turn_r90", "FR-13");
            CollectionAssert.DoesNotContain(Clips(p, 0), "start_f");
        }

        /// <summary>손으로 만든 궤적: v m/s로 달리다 시각 tc에 deg만큼 방향을 꺾는다.</summary>
        static HighlightData Cut(float v, float deg)
        {
            var d = new HighlightData(0f, 121);
            float x = 0, z = 0, h = 0;
            for (int f = 0; f < 121; f++)
            {
                if (f == 60) h = deg * (float)Math.PI / 180f;
                d.Set(f, 0, x, z, h);
                x += (float)Math.Sin(h) * v / 30f;
                z += (float)Math.Cos(h) * v / 30f;
            }
            return d;
        }

        [TestCase(60f, "cut_r45")]
        [TestCase(-60f, "cut_l45")]
        [TestCase(100f, "cut_r90")]
        [TestCase(-100f, "cut_l90")]
        public void SharpTurnAtSpeed_PicksCutClip(float deg, string want)
        {
            var p = AnimPlan.Build(AnimTestKit.Lib, AnimTestKit.Map, Cut(4f, deg), NullLogger.Instance);
            CollectionAssert.Contains(Clips(p, 0), want);
        }

        [Test]
        public void CutAt44Degrees_NoCut()
        {
            var p = AnimPlan.Build(AnimTestKit.Lib, AnimTestKit.Map, Cut(4f, 40f), NullLogger.Instance);
            Assert.IsFalse(Clips(p, 0).Exists(x => x.StartsWith("cut_", StringComparison.Ordinal)));
        }

        [Test]
        public void CutPlant_WithinLimitOfTurnPoint()
        {
            var d = Cut(4f, 60f);
            var p = AnimPlan.Build(AnimTestKit.Lib, AnimTestKit.Map, d, NullLogger.Instance);
            var turn = d.Pos(0, 2f);
            var c = p.Library.Get(p.Library.IndexOf("cut_r45"));
            float best = float.MaxValue;
            foreach (var pl in p.Plants.Plants(0, c.PlantLeft))
                best = Math.Min(best, (float)Math.Sqrt(Math.Pow(pl.X - turn.X, 2) + Math.Pow(pl.Z - turn.Z, 2)));
            Assert.LessOrEqual(best, MotionStandardsTests.Load().Max("plantError"), "FR-23 디딤발 ±20 cm");
        }

        [Test]
        public void ActionWins_TransitionNotOverlappingContact()
        {
            // 공 접촉 직전에 급정지하는 궤적: 전환이 접촉을 덮지 않는다
            var p = Plan("longPass");
            var a = p.Timeline(0).Get(0);
            foreach (var t in p.Transitions(0))
                Assert.IsTrue(t.End <= a.Start || t.Start >= a.End, $"{p.Library.Get(t.Clip).Id} {t.Start}~{t.End} vs 액션 {a.Start}~{a.End}");
            Assert.AreEqual(p.Data.Events[0].Time, a.ContactAt, 1e-4f, "접촉 시각이 밀리지 않는다");
        }

        [Test]
        public void Evaluate_Weights_SumToOne_DuringTransition()
        {
            var p = Plan("stopCut");
            var s = new PoseSample();
            for (float t = p.Data.StartTime; t < p.Data.EndTime; t += 0.013f)
            {
                p.Evaluate(0, t, ref s);
                float sum = 0f;
                for (int i = 0; i < s.Count; i++) sum += s.Weight(i);
                Assert.AreEqual(1f, sum, 2e-3f, $"t={t}");
            }
        }

        /// <summary>AC-09: 렌더 몸 기울기 vs atan(a_lat/g).</summary>
        [Test]
        public void Lean_Within5Degrees_OnCurve()
        {
            var m = MotionSceneKit.Scene("curveRun");
            var p = AnimPlan.Build(AnimTestKit.Lib, AnimTestKit.Map, m.Data, NullLogger.Instance);
            using var kit = new AnimRigKit(m.Data, 0);
            var err = new List<float>();
            int samples = 0;
            for (int f = 30; f < m.Data.Frames - 30; f++)
            {
                float t = m.Data.StartTime + f / 30f;
                float lat = kit.Plan.Track.AccelLat(f, 0);
                if (Math.Abs(lat) < 2f || kit.Plan.Track.Speed(f, 0) < 3f) continue;
                kit.System.ApplyAt(t);
                var rig = kit.Rigs[0];
                var up = rig.Bone("Neck").position - rig.Bone("Hips").position;
                var right = rig.transform.right;
                float roll = (float)Math.Atan2(UnityEngine.Vector3.Dot(up, right), up.y);
                float want = MotionTrack.LeanFor(lat);
                err.Add(Math.Abs(roll - want) * 180f / (float)Math.PI);
                samples++;
            }
            Assert.Greater(samples, 30, "곡선 구간이 있어야 한다");
            Assert.LessOrEqual(MotionStats.Percentile(err, 95), MotionStandardsTests.Load().Max("leanErrorP95"));
        }

        /// <summary>AC-09: 큰 가감속 구간에서 자세가 쓰인다.</summary>
        [TestCase("straight50")]
        [TestCase("stopCut")]
        public void PostureUsage_90Percent(string id)
        {
            var p = Plan(id);
            var tr = p.Track;
            var s = new PoseSample();
            int acc = 0, accOn = 0, dec = 0, decOn = 0;
            for (int f = 0; f < tr.Frames; f++)
            {
                float a = tr.AccelAlong(f, 0), t = p.Data.StartTime + f / 30f;
                if (a < MotionDefaults.AccelPosture && a > -MotionDefaults.DecelPosture) continue;
                p.Evaluate(0, t, ref s);
                bool clip = false;
                for (int i = 0; i < s.Count; i++)
                {
                    var g = p.Library.Get(s.Clip(i)).Gait;
                    if ((g == "start" || g == "stop") && s.Weight(i) > 0.3f) clip = true;
                }
                if (a > 0) { acc++; if (clip || s.Pitch > 0.03f) accOn++; }
                else { dec++; if (clip || s.Pitch < -0.03f) decOn++; }
            }
            float min = MotionStandardsTests.Load().Min("postureUsage");
            if (acc > 0) Assert.GreaterOrEqual(accOn / (float)acc, min, $"가속 {accOn}/{acc}");
            if (dec > 0) Assert.GreaterOrEqual(decOn / (float)dec, min, $"감속 {decOn}/{dec}");
            Assert.Greater(acc + dec, 0);
        }
    }
}
