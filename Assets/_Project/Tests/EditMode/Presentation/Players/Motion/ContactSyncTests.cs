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
    /// <summary>공 접촉 (specs/054 AC-04·05·06·11, FR-30~36·52·81·82).</summary>
    public class ContactSyncTests
    {
        static readonly string[] ContactIds = { "longPass", "volley", "tackle", "header", "keeper", "oneTouch", "chainTouch", "sameFrame" };

        [TestCaseSource(nameof(ContactIds))]
        public void TimeDistancePart_WithinLimits(string id)
        {
            var s = MotionStandardsTests.Load();
            var r = MotionRun.Measure(id);
            Assert.Greater(r.Contacts.Count, 0, "접촉이 측정돼야 한다");
            foreach (var c in r.Contacts)
            {
                if (c.Missed) continue;
                Assert.LessOrEqual(c.TimeError, s.Max("contactTime"), $"{id} {c.Clip} 시각");
                Assert.LessOrEqual(c.Distance, c.DistanceLimit, $"{id} {c.Clip} 거리");
                if (c.SimPart != BodyPart.None) Assert.AreEqual(c.SimPart, c.ClipPart, $"{id} {c.Clip} 부위");
            }
        }

        [Test]
        public void PlantFoot_95Percent()
        {
            var s = MotionStandardsTests.Load();
            int kicks = 0, ok = 0;
            foreach (var id in ContactIds)
                foreach (var c in MotionRun.Measure(id).Contacts)
                {
                    if (!c.IsKick) continue;
                    kicks++;
                    float relax = c.OneTouch ? s.Max("oneTouchRelax") : 1f;
                    bool side = c.PlantSide >= s.Min("plantSide") / relax && c.PlantSide <= s.Max("plantSide") * relax;
                    bool back = c.PlantBack >= s.Min("plantBack") / relax && c.PlantBack <= s.Max("plantBack") * relax;
                    if (side && back) ok++;
                    else TestContext.WriteLine($"{id} {c.Clip} 옆 {c.PlantSide:0.000} 뒤 {c.PlantBack:0.000} 원터치 {c.OneTouch}");
                }
            Assert.Greater(kicks, 0);
            Assert.GreaterOrEqual(ok / (float)kicks, s.Min("plantRatio"), $"{ok}/{kicks}");
        }

        [Test]
        public void OneTouch_ContactTimeKept_Flagged()
        {
            var r = MotionRun.Measure("oneTouch");
            Assert.AreEqual(2, r.Contacts.Count);
            Assert.IsTrue(r.Contacts[1].OneTouch, "FR-81 진단 표시");
            Assert.LessOrEqual(r.Contacts[1].TimeError, 0.5f * 1.5f / 60f + 1e-3f, "FR-81 접촉 시각은 반드시 지킨다 (렌더 격자 반 프레임 외 0)");
        }

        [Test]
        public void ChainedContacts_BothWithinLimits()
        {
            foreach (var c in MotionRun.Measure("chainTouch").Contacts)
                Assert.LessOrEqual(c.Distance, c.DistanceLimit, c.Clip);
        }

        [Test]
        public void BallAfterContact_2cm_PullCap_15cm()
        {
            var s = MotionStandardsTests.Load();
            foreach (var id in ContactIds)
            {
                var r = MotionRun.Measure(id);
                Assert.LessOrEqual(r.Max("ballAfterContact"), s.Max("ballAfterContact"), id);
                Assert.LessOrEqual(r.Max("ballPull"), s.Max("ballPull") + 1e-4f, id);
            }
        }

        [Test]
        public void BallKeyed_ExactAtContact_MatchesFramesElsewhere()
        {
            var m = MotionSceneKit.Scene("longPass");
            var keys = new ContactKeys(m.Data);
            var ev = m.Data.Events[0];
            Assert.LessOrEqual((keys.Ball(ev.Time) - ev.BallPos).Length(), 1e-4f, "접촉 시각에 정확히");
            float frameT = m.Data.StartTime + 15f / 30f;
            Assert.LessOrEqual((keys.Ball(frameT) - m.Data.Ball[15]).Length(), 1e-4f, "접촉 없는 프레임은 기록 그대로");
        }

        [Test]
        public void BallKeyed_ContactBetweenFrames_NotCutOff()
        {
            // 공이 접촉에서 튀어나가는 꺾임을 프레임 사이에 넣는다
            var d = new HighlightData(0f, 5);
            for (int f = 0; f < 5; f++)
            {
                for (int e = 0; e < HighlightData.Entities; e++) d.Set(f, e, 0, 0, 0);
                d.Ball[f] = f <= 2 ? new Vec3(0, 0.11f, 0) : new Vec3(0, 0.11f, (f - 2) * 1f);
            }
            float tc = 2.5f / 30f;
            d.Events.Add(new AnimEvent(tc, 1, SimEventKind.Pass, 0, -1, BodyPart.RightFoot, ActionOutcome.Success, new Vec3(0, 0.11f, 0), new Vec3(0, 0, 30)));
            var k = new ContactKeys(d);
            Assert.AreEqual(0f, k.Ball(tc).Z, 1e-4f, "접촉까지 공은 제자리");
            Assert.AreEqual(1f, k.Ball(3f / 30f).Z, 1e-4f, "다음 프레임에서 기록 위치");
        }

        /// <summary>AC-11: 30 Hz 보간 vs 100 Hz 원본.</summary>
        [TestCase("stopCut")]
        [TestCase("turnBackSide")]
        [TestCase("straight50")]
        public void Interp30Hz_Vs100Hz_3cm(string id)
        {
            var m = MotionSceneKit.Scene(id);
            const int E = HighlightData.Entities;
            var err = new List<float>();
            int steps = m.RawX.Length / E;
            for (int s = 0; s < steps; s++)
            {
                float t = m.Data.StartTime + s * 0.01f;
                if (t > m.Data.EndTime) break;
                foreach (var e in m.Actors)
                {
                    var p = m.Data.Pos(e, t);
                    float dx = p.X - m.RawX[s * E + e], dz = p.Z - m.RawZ[s * E + e];
                    err.Add((float)Math.Sqrt(dx * dx + dz * dz));
                }
            }
            Assert.LessOrEqual(MotionStats.Percentile(err, 95), MotionStandardsTests.Load().Max("interpP95"));
        }

        [Test]
        public void Interp_ContactEventTime_Exact_At05x()
        {
            var r = MotionRun.Measure("longPass", 0.5f);
            float grid = 0.5f * 0.5f / 60f;   // 렌더 격자 반 프레임 외 어긋남 0
            foreach (var c in r.Contacts) Assert.LessOrEqual(c.TimeError, grid + 1e-3f, "접촉 시각 어긋남 0");
        }

        /// <summary>시뮬 결과를 Fail로 바꾼 복사본.</summary>
        static HighlightData WithFail(HighlightData d, int index)
        {
            var o = new HighlightData(d.StartTime, d.Frames);
            Array.Copy(d.X, o.X, d.X.Length);
            Array.Copy(d.Z, o.Z, d.Z.Length);
            Array.Copy(d.Yaw, o.Yaw, d.Yaw.Length);
            Array.Copy(d.Present, o.Present, d.Present.Length);
            Array.Copy(d.Ball, o.Ball, d.Ball.Length);
            for (int i = 0; i < d.Events.Count; i++)
            {
                var ev = d.Events[i];
                o.Events.Add(new AnimEvent(ev.Time, ev.Seq, ev.Kind, ev.Entity, ev.Other, ev.Part, i == index ? ActionOutcome.Fail : ev.Outcome, ev.BallPos, ev.BallVelOut));
            }
            return o;
        }

        /// <summary>FR-36: 빗맞은 슈팅도 공에는 닿는다.</summary>
        [Test]
        public void MishitShot_StillTouchesBall()
        {
            var m = MotionSceneKit.Scene("volley");
            m.Data = WithFail(m.Data, 0);
            var r = MotionRun.Measure(m);
            Assert.IsFalse(r.Contacts[0].Missed);
            Assert.LessOrEqual(r.Contacts[0].Distance, r.Contacts[0].DistanceLimit);
        }

        [TestCase(SimEventKind.Tackle, true)]
        [TestCase(SimEventKind.Save, true)]
        [TestCase(SimEventKind.Shot, false)]
        [TestCase(SimEventKind.PassReceived, false)]
        [TestCase(SimEventKind.Pass, false)]
        public void MissesBall_ByKind(SimEventKind k, bool miss)
        {
            var ev = new AnimEvent(0f, 1, k, 0, -1, BodyPart.None, ActionOutcome.Fail, default, default);
            Assert.AreEqual(miss, AnimPlan.MissesBall(ev));
        }

        /// <summary>FR-52: 태클·선방도 접촉 키.</summary>
        [Test]
        public void ContactKeys_IncludeTackleSave_ExcludeMiss()
        {
            var d = MotionSceneKit.Scene("tackle").Data;
            var k = new ContactKeys(d);
            Assert.LessOrEqual((k.Ball(d.Events[0].Time) - d.Events[0].BallPos).Length(), 1e-3f, "성공 태클");
            Assert.Greater((k.Ball(d.Events[1].Time) - d.Events[1].BallPos).Length(), 0.1f, "실패 태클은 공 궤적을 꺾지 않는다");
        }

        /// <summary>FR-37: 소유 중 공은 터치 사이에서만 움직인다 — 마지막 터치에서 멀리 간 뒤 혼자 따라가지 않는다.</summary>
        [Test]
        public void Dribble_NoBallMoveWithoutTouch()
        {
            var d = MotionSceneKit.Scene("chainTouch").Data;
            for (int f = 1; f < d.Frames; f++)
            {
                if (d.BallOwner[f] < 0) continue;
                float t = d.StartTime + f / 30f;
                float nextTouch = float.MaxValue, lastTouch = float.MinValue;
                foreach (var ev in d.Events)
                {
                    if (ev.Time >= t) nextTouch = Math.Min(nextTouch, ev.Time);
                    else lastTouch = Math.Max(lastTouch, ev.Time);
                }
                if ((d.Ball[f] - d.Ball[f - 1]).Length() > 1e-4f)
                    Assert.Less(Math.Min(nextTouch - t, t - lastTouch), 0.6f, $"t={t}: 터치 없는 공 이동");
            }
        }

        /// <summary>FR-44: 공 쪽으로 다이빙.</summary>
        [Test]
        public void Keeper_DivesTowardBallSide()
        {
            var lib = AnimTestKit.Lib;
            var d = MotionSceneKit.Scene("keeper").Data;
            var p = AnimPlan.Build(lib, AnimTestKit.Map, d, NullLogger.Instance);
            var c = lib.Get(p.Timeline(0).Get(0).Clip);
            Assert.AreEqual("gk_dive_l", c.Id);
            Assert.Less(c.ContactPoint.X, 0f, "왼쪽 다이브의 손은 왼쪽");
        }

        [Test]
        public void TrapFail_BallLeavesBody_ClipIsFail()
        {
            // FR-36: 실패한 트래핑은 튀어나가는 클립
            var d = MotionSceneKit.Scene("oneTouch").Data;
            var evs = d.Events;
            var ev = evs[0];
            var dd = new HighlightData(d.StartTime, d.Frames);
            Array.Copy(d.X, dd.X, d.X.Length);
            Array.Copy(d.Z, dd.Z, d.Z.Length);
            Array.Copy(d.Yaw, dd.Yaw, d.Yaw.Length);
            Array.Copy(d.Present, dd.Present, d.Present.Length);
            Array.Copy(d.Ball, dd.Ball, d.Ball.Length);
            dd.Events.Add(new AnimEvent(ev.Time, 1, ev.Kind, ev.Entity, -1, ev.Part, ActionOutcome.Fail, ev.BallPos, ev.BallVelOut));
            var p = AnimPlan.Build(AnimTestKit.Lib, AnimTestKit.Map, dd, NullLogger.Instance);
            Assert.AreEqual("trap_fail_r", p.Library.Get(p.Timeline(0).Get(0).Clip).Id);
        }
    }
}
