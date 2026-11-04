using System;
using NUnit.Framework;
using RoadToPro.Core.Logging;
using RoadToPro.MatchEngine.Match.Events;
using RoadToPro.MatchEngine.Match.Pipeline;
using RoadToPro.MatchEngine.Match.Replay;
using RoadToPro.Presentation.Players.Logic;

namespace RoadToPro.Tests.EditMode.Presentation.Players
{
    /// <summary>시뮬 이벤트·상태 → 클립 매핑 표 (specs/043 FR-40·47·48, AC-06).</summary>
    public class AnimationMapTests
    {
        [Test]
        public void AllKinds_Mapped()
        {
            var map = AnimTestKit.Map;
            foreach (SimEventKind k in Enum.GetValues(typeof(SimEventKind)))
            {
                if (k == SimEventKind.None) continue;
                Assert.IsTrue(map.Has(k), "SimEvent 매핑 없음: " + k);
                if (!map.IsNone(k)) Assert.Greater(map.Candidates(k).Count, 0, "후보 0: " + k);
            }
            foreach (MotionHint h in Enum.GetValues(typeof(MotionHint)))
                Assert.IsTrue(map.Has(h), "MotionHint 매핑 없음: " + h);
            foreach (BallActionKind a in Enum.GetValues(typeof(BallActionKind)))
            {
                if (a == BallActionKind.None) continue;
                Assert.Greater(map.Candidates(a).Count, 0, "BallAction 매핑 없음: " + a);
            }
            CollectionAssert.IsEmpty(map.Missing, "매핑 표 누락");
        }

        [Test]
        public void Unknown_LogsAndDefault()
        {
            var log = new MemoryLogger(LogLevel.Warning);
            var map = AnimationMap.Parse(AnimTestKit.MapJson, AnimTestKit.Lib, log);
            var c = map.Resolve((SimEventKind)9999, log);
            Assert.AreEqual(1, c.Count);
            Assert.AreEqual(map.DefaultClip, c[0]);
            Assert.AreEqual(1, log.Entries.Count);
        }

        [Test]
        public void UnknownClipInTable_FailsParse()
        {
            var bad = AnimTestKit.MapJson.Replace("\"pass_inside_r\"", "\"pass_nope\"");
            Assert.Throws<FormatException>(() => AnimationMap.Parse(bad, AnimTestKit.Lib, NullLogger.Instance));
        }

        [Test]
        public void Outcome_FailedTrap_And_WonTackle()
        {
            var map = AnimTestKit.Map;
            var lib = AnimTestKit.Lib;
            foreach (var i in map.Candidates(SimEventKind.PassReceived, ActionOutcome.Fail))
                Assert.AreEqual("fail", lib.Get(i).Outcome, lib.Get(i).Id);
            foreach (var i in map.Candidates(SimEventKind.Tackle, ActionOutcome.Win))
                Assert.AreEqual("win", lib.Get(i).Outcome, lib.Get(i).Id);
            foreach (var i in map.Candidates(SimEventKind.PassReceived, ActionOutcome.Success))
                Assert.AreNotEqual("fail", lib.Get(i).Outcome, lib.Get(i).Id);
        }

        [TestCase(0f, "fall_n")]
        [TestCase(90f, "fall_e")]
        [TestCase(100f, "fall_e")]
        [TestCase(-40f, "fall_nw")]
        [TestCase(180f, "fall_s")]
        [TestCase(250f, "fall_w")]
        public void HitDirection_NearestOfEight(float relDeg, string expect)
        {
            var lib = AnimTestKit.Lib;
            // 선수가 +Z(yaw 0)를 볼 때 밀리는 방향 relDeg (0 = 앞, 90 = 오른쪽)
            float r = relDeg * (float)Math.PI / 180f;
            var push = new RoadToPro.Core.Math.Vec3((float)Math.Sin(r), 0, (float)Math.Cos(r));
            Assert.AreEqual(expect, lib.Get(HitDirection.FallClip(lib, push, 0f)).Id);
            // 선수가 +X를 볼 때(yaw 90°) 같은 상대 방향이면 같은 클립
            float r2 = r + (float)Math.PI / 2;
            var push2 = new RoadToPro.Core.Math.Vec3((float)Math.Sin(r2), 0, (float)Math.Cos(r2));
            Assert.AreEqual(expect, lib.Get(HitDirection.FallClip(lib, push2, (float)Math.PI / 2)).Id);
        }
    }
}
