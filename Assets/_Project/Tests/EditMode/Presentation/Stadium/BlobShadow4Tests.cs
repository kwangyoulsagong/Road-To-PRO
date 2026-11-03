using System.Collections.Generic;
using NUnit.Framework;
using RoadToPro.Presentation.Stadium;
using UnityEngine;

namespace RoadToPro.Tests.EditMode.Presentation.Stadium
{
    /// <summary>나이트 블롭 섀도 4장 (specs/044 FR-41, AC-13 보조).</summary>
    public class BlobShadow4Tests
    {
        [Test]
        public void FourShadows_PointAwayFromEmitters()
        {
            var emitters = StadiumTestKit.DefaultLayout.LightEmitters;
            var foot = new Vector3(3f, 0f, -2f);
            var shadows = new BlobShadowPose[4];
            BlobShadow4.Compute(foot, emitters, shadows);
            for (int i = 0; i < 4; i++)
            {
                var away = new Vector3(foot.x - emitters[i].x, 0f, foot.z - emitters[i].z).normalized;
                Assert.Greater(Vector3.Dot(shadows[i].Direction, away), 0.999f, $"그림자 {i} 방향");
                Assert.Greater(Vector3.Dot(shadows[i].Center - foot, away), 0f, "그림자 중심이 광원 반대쪽");
                Assert.AreEqual(0f, shadows[i].Direction.y, 1e-6f);
            }
        }

        [Test]
        public void CornerEmitters_MakeXPattern()
        {
            var emitters = new List<Vector3> { new(-80, 45, -60), new(80, 45, -60), new(-80, 45, 60), new(80, 45, 60) };
            var shadows = new BlobShadowPose[4];
            BlobShadow4.Compute(Vector3.zero, emitters, shadows);
            // 대각 두 쌍: 0↔3, 1↔2 서로 반대 방향
            Assert.Less(Vector3.Dot(shadows[0].Direction, shadows[3].Direction), -0.99f);
            Assert.Less(Vector3.Dot(shadows[1].Direction, shadows[2].Direction), -0.99f);
        }

        [Test]
        public void TooFewEmitters_Throws()
        {
            Assert.Throws<System.ArgumentException>(() => BlobShadow4.Compute(Vector3.zero, new List<Vector3> { Vector3.one }, new BlobShadowPose[4]));
        }
    }
}
