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

        [Test]
        public void ContactPose_UnderFoot_AllPresets()
        {
            // specs/061 FR-44: 발 바로 아래(중심–발 접지점 ≤ 5 cm), 0.3 m 반경을 덮고 고리(0.6~1.0 m)는 덮지 않는 크기
            var foot = new Vector3(3f, 0.9f, -2f);
            var p = PlayerBlobShadows.ContactPoint(foot);
            Assert.LessOrEqual(Vector2.Distance(new Vector2(p.x, p.z), new Vector2(foot.x, foot.z)), 0.05f);
            Assert.AreEqual(1f, p.w);
            Assert.GreaterOrEqual(PlayerBlobShadows.ContactRadius, 0.3f);
            Assert.Less(PlayerBlobShadows.ContactRadius, 0.6f, "고리(0.6~1.0 m)는 덮지 않는다 — 대비가 생겨야 함");
        }

        [Test]
        public void Configure_ContactOnly_NotShaped()
        {
            var go = new GameObject("blob-test");
            try
            {
                var b = go.AddComponent<PlayerBlobShadows>();
                b.Configure(new[] { go.transform }, null, null, Vector3.down, null, false, null);
                Assert.IsFalse(b.Shaped, "Medium·High: 접촉 어둡기만 (방향광 그림자가 모양을 맡음)");
                b.Configure(new[] { go.transform }, null, null, Vector3.down, null, null);
                Assert.IsTrue(b.Shaped, "기존 호출(Low)은 모양 그림자 + 접촉");
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }
    }
}
