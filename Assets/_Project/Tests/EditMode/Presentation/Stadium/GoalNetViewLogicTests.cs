using System.Collections.Generic;
using NUnit.Framework;
using RoadToPro.Core.Math;
using RoadToPro.Core.Pitch;
using RoadToPro.MatchEngine.Ball;
using RoadToPro.Presentation.Highlight.Logic;
using RoadToPro.Presentation.Stadium.Net;
using RoadToPro.Tests.EditMode.MatchEngine.Ball;
using RoadToPro.Tests.EditMode.Presentation.Highlight;

namespace RoadToPro.Tests.EditMode.Presentation.Stadium
{
    /// <summary>
    /// specs/059 FR-27·53·54 · AC-11: 그물 뷰 구동(<see cref="GoalNetDriver"/>, 구운 데이터 보간) — 키 모먼트 정지 중 변화 0,
    /// 장면 건너뛰기 뒤 새 장면 시각 상태, 굽기 없으면 정지 모양·쓰기 없음, 다른 골대 불변.
    /// </summary>
    public class GoalNetViewLogicTests
    {
        static NetCloth _cloth;
        static NetCloth Cloth => _cloth ??= new NetCloth(NetRippleTuningTests.Defaults());
        static GoalBox Box
        {
            get
            {
                var p = BallTestKit.Params();
                return new GoalBox(PitchSpec.Default, BallParams.GoalWidth, BallParams.GoalHeight, p.NetDepth, p.Radius, p.NetPocket);
            }
        }

        /// <summary>왼쪽 골(side −1) 뒤 그물 정면 20 m/s 슛을 굽는다. [골대 번호] 배열.</summary>
        static NetBake[] LeftHit(float start = 0f)
        {
            var data = NetContactDetectorTests.Frames(NetContactDetectorTests.InGoal(0.3f, 0.3f, 1.2f, 20f, 0f, 0f), 4f, start);
            var cs = new List<NetContact>();
            NetContactDetector.Find(data, Box, NetContactSettings.Default, cs);
            return new[] { NetBake.Build(Cloth, data, cs, -1, 52.5f, Box), null };
        }

        static GoalNetDriver Driver() => new GoalNetDriver(Cloth);

        static bool SameAsRest(GoalNetDriver d, int index)
        {
            for (int i = 0; i < d.RestRender.Length; i++) if (!d.Local[index][i].Equals(d.RestRender[i])) return false;
            return true;
        }

        [Test]
        public void KeyMomentPause_NoChange()
        {
            var drv = Driver();
            var bakes = LeftHit();
            double t = bakes[0].StartTime + 0.3;
            Assert.AreNotEqual(0, drv.Update(1, t, bakes), "출렁이는 중");
            var before = (Vec3[])drv.Local[0].Clone();
            for (int k = 0; k < 30; k++) Assert.AreEqual(0, drv.Update(1, t, bakes), "정지 중 쓰기 없음");
            for (int i = 0; i < before.Length; i++) Assert.AreEqual(before[i], drv.Local[0][i]);
        }

        [Test]
        public void SceneSkip_StartsAtSceneState()
        {
            var drv = Driver();
            var bakes = LeftHit();
            Assert.AreNotEqual(0, drv.Update(1, bakes[0].StartTime + 0.3, bakes));
            Assert.IsFalse(SameAsRest(drv, 0));
            int dirty = drv.Update(2, 60d, new NetBake[2]);
            Assert.AreNotEqual(0, dirty & GoalNetDriver.MaskOf(-1), "정지 모양으로 되돌리는 쓰기 1회");
            Assert.IsTrue(SameAsRest(drv, 0), "이전 장면 출렁임이 이어지지 않음");
            Assert.AreEqual(0, drv.Update(2, 60.1d, new NetBake[2]));
        }

        /// <summary>굽기 시작 전 시각은 정지 모양 (이전 장면·접촉 전, FR-54).</summary>
        [Test]
        public void BeforeBake_Rest()
        {
            var drv = Driver();
            var bakes = LeftHit(10f);
            drv.Update(1, bakes[0].StartTime - 0.5, bakes);
            Assert.IsTrue(SameAsRest(drv, 0));
        }

        [Test]
        public void NoBake_NoVertexWrite()
        {
            var drv = Driver();
            for (int k = 0; k < 60; k++) Assert.AreEqual(0, drv.Update(1, k / 60d, new NetBake[2]), $"프레임 {k}");
            Assert.IsTrue(SameAsRest(drv, 0));
        }

        [Test]
        public void OtherGoal_Untouched()
        {
            var drv = Driver();
            var bakes = LeftHit();
            int dirty = drv.Update(1, bakes[0].StartTime + 0.3, bakes);
            Assert.AreEqual(0, dirty & GoalNetDriver.MaskOf(1));
            Assert.IsTrue(SameAsRest(drv, 1));
        }
    }
}
