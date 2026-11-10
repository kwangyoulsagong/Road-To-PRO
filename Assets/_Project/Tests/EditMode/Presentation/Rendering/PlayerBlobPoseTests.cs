using System.Collections.Generic;
using NUnit.Framework;
using RoadToPro.Presentation.Stadium;
using UnityEngine;

namespace RoadToPro.Tests.EditMode.Presentation.Rendering
{
    /// <summary>Low 블롭 그림자 (specs/048 FR-01 Low: 발밑 블롭, 나이트 4장).</summary>
    public class PlayerBlobPoseTests
    {
        [Test]
        public void DayPose_HighSun_RoundUnderFoot()
        {
            var p = PlayerBlobShadows.DayPose(new Vector3(3f, 0.9f, -2f), Vector3.down);
            Assert.AreEqual(PlayerBlobShadows.DayWidth, p.Length, 1e-4f);
            Assert.AreEqual(3f, p.Center.x, 1e-4f);
            Assert.AreEqual(-2f, p.Center.z, 1e-4f);
            Assert.AreEqual(0f, p.Center.y, 1e-6f, "지면 위");
        }

        [Test]
        public void DayPose_LowSun_LongerAwayFromSunAndClamped()
        {
            var sun = new Vector3(1f, -0.2f, 0f).normalized; // +X 쪽으로 비춤
            var p = PlayerBlobShadows.DayPose(Vector3.zero, sun);
            Assert.Greater(p.Length, PlayerBlobShadows.DayWidth);
            Assert.LessOrEqual(p.Length, PlayerBlobShadows.DayMaxLength + 1e-4f);
            Assert.Greater(p.Direction.x, 0.99f, "그림자는 빛이 가는 방향");
            Assert.Greater(p.Center.x, 0f);
        }

        [Test]
        public void Configure_FewEmitters_DayWithOneDiagnostic()
        {
            var go = new GameObject("blob-test");
            try
            {
                var b = go.AddComponent<PlayerBlobShadows>();
                var diag = new List<string>();
                b.Configure(new[] { go.transform }, null, null, Vector3.down, new[] { Vector3.zero, Vector3.one }, diag.Add);
                Assert.IsFalse(b.IsNight);
                Assert.AreEqual(1, diag.Count);

                diag.Clear();
                var emitters = new[] { new Vector3(-60, 40, -40), new Vector3(60, 40, -40), new Vector3(-60, 40, 40), new Vector3(60, 40, 40) };
                b.Configure(new[] { go.transform }, null, null, Vector3.down, emitters, diag.Add);
                Assert.IsTrue(b.IsNight);
                Assert.IsEmpty(diag);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void Configure_TooManyTargets_ClampedWithDiagnostic()
        {
            var go = new GameObject("blob-test");
            try
            {
                var b = go.AddComponent<PlayerBlobShadows>();
                var targets = new Transform[PlayerBlobShadows.MaxTargets + 3];
                for (int i = 0; i < targets.Length; i++) targets[i] = go.transform;
                var diag = new List<string>();
                b.Configure(targets, null, null, Vector3.down, null, diag.Add);
                Assert.AreEqual(1, diag.Count);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }
    }
}
