using System.Collections.Generic;
using NUnit.Framework;
using RoadToPro.Verification;
using UnityEngine;

namespace RoadToPro.Tests.EditMode.Verification
{
    /// <summary>3D 카메라 캡처·영역 주석 (specs/044 T17). 배치(-nographics)라 픽셀 내용은 E2E에서 본다.</summary>
    public class OffscreenCaptureTests
    {
        [Test]
        public void Camera_RendersToTarget()
        {
            var go = new GameObject("cam");
            try
            {
                var cam = go.AddComponent<Camera>();
                // 배치(-nographics)는 그래픽 장치가 없어 RT 생성·연결 에러 로그가 난다 — 객체 연결만 검사
                if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
                    UnityEngine.TestTools.LogAssert.ignoreFailingMessages = true;
                var cap = new OffscreenCapture(320, 180, 1f);
                Assert.IsFalse(cap.HasCameras);
                cap.AttachCamera(cam);
                cap.AttachCamera(cam);
                Assert.IsTrue(cap.HasCameras);
                Assert.AreSame(cap.Target, cam.targetTexture);
                Assert.AreEqual(320, cam.targetTexture.width);
                Assert.AreEqual(180, cam.targetTexture.height);
                Assert.AreEqual(32, cap.Target.depth, "3D 깊이 버퍼");
                cap.Target.Release();
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void Context_RegistersCamerasAndRegions()
        {
            var go = new GameObject("cam");
            try
            {
                var cam = go.AddComponent<Camera>();
                FarmContext.RegisterCamera(cam);
                FarmContext.RegisterCamera(cam);
                Assert.AreEqual(1, FarmContext.Cameras.Count);
                FarmContext.CaptureRegions = (w, h) => new Dictionary<string, object> { { "box", new List<object> { 0, 0, w, h } } };
                var r = FarmContext.CaptureRegions(10, 20);
                Assert.AreEqual(new List<object> { 0, 0, 10, 20 }, r["box"]);
            }
            finally
            {
                FarmContext.Cameras.Clear();
                FarmContext.CaptureRegions = null;
                Object.DestroyImmediate(go);
            }
        }
    }
}
