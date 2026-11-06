using System;
using NUnit.Framework;
using RoadToPro.Presentation.Players.Logic;
using RoadToPro.Verification.Samples.Motion;

namespace RoadToPro.Tests.EditMode.Presentation.Players.Motion
{
    /// <summary>합성 장면 (specs/054 증거 세트 8종·경계 세트).</summary>
    public class MotionScenesTests
    {
        public static readonly string[] AllIds = Combine();

        static string[] Combine()
        {
            var a = new string[MotionSceneBuilder.EvidenceIds.Length + MotionSceneBuilder.EdgeIds.Length];
            MotionSceneBuilder.EvidenceIds.CopyTo(a, 0);
            MotionSceneBuilder.EdgeIds.CopyTo(a, MotionSceneBuilder.EvidenceIds.Length);
            return a;
        }

        [TestCaseSource(nameof(AllIds))]
        public void Build_TwoRuns_SameHash(string id)
        {
            var a = MotionSceneKit.Scene(id);
            var b = MotionSceneKit.Scene(id);
            Assert.AreEqual(a.Data.Hash(), b.Data.Hash());
            Assert.Greater(a.Actors.Length, 0);
            Assert.IsNotEmpty(a.Keys);
        }

        [TestCaseSource(nameof(AllIds))]
        public void Build_ActorSpeed_Within005Limits(string id)
        {
            var m = MotionSceneKit.Scene(id);
            if (m.ExpectSimFault) return;
            const int E = HighlightData.Entities;
            foreach (var e in m.Actors)
                for (int s = 1; s * E + e < m.RawX.Length; s++)
                {
                    float dx = m.RawX[s * E + e] - m.RawX[(s - 1) * E + e], dz = m.RawZ[s * E + e] - m.RawZ[(s - 1) * E + e];
                    float v = (float)Math.Sqrt(dx * dx + dz * dz) / MotionScene.RawDt;
                    Assert.LessOrEqual(v, 10.5f, $"{id} e{e} s{s}: 속도 {v}");
                }
        }

        /// <summary>FR-80: 시뮬 오류를 일부러 넣은 장면만 오류로 분류된다.</summary>
        [TestCaseSource(nameof(AllIds))]
        public void SimFault_OnlyInExpectedScene(string id)
        {
            var m = MotionSceneKit.Scene(id);
            var t = new RoadToPro.Presentation.Players.Logic.Motion.MotionTrack(m.Data);
            Assert.AreEqual(m.ExpectSimFault, t.Faults.Count > 0, id);
        }

        [Test]
        public void Straight50_ReachesHighSpeed_Covers50m()
        {
            var d = MotionSceneKit.Scene("straight50").Data;
            float dist = (d.Pos(0, d.EndTime) - d.Pos(0, d.StartTime)).Length();
            Assert.GreaterOrEqual(dist, 50f);
        }

        [Test]
        public void TackleScene_EventsInOrder_FoulPairsTackle()
        {
            var d = MotionSceneKit.Scene("tackle").Data;
            Assert.AreEqual(3, d.Events.Count);
            Assert.AreEqual(RoadToPro.MatchEngine.Match.Events.SimEventKind.Foul, d.Events[2].Kind);
            Assert.AreEqual(d.Events[1].Time, d.Events[2].Time);
        }
    }
}
