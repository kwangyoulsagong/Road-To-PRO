using NUnit.Framework;
using RoadToPro.Core.Math;
using RoadToPro.Presentation.Highlight.Broadcast.Logic;
using RoadToPro.Presentation.Players.Logic;
using RoadToPro.Presentation.Rendering;
using RoadToPro.Presentation.Rendering.Logic;
using RoadToPro.Presentation.Stadium;
using UnityEngine;

namespace RoadToPro.Tests.EditMode.Presentation.Rendering
{
    /// <summary>specs/061 FR-80~83·85·87: 고품질 모드 컨트롤러가 구간 가중치대로 DoF 볼륨·림만 바꾸고, 끝나면 되돌린다.</summary>
    public class CinematicModeControllerTests
    {
        Camera _cam;

        [SetUp]
        public void SetUp()
        {
            _cam = new GameObject("cam").AddComponent<Camera>();
            Shader.SetGlobalFloat(StadiumLighting.IdRim, 0.4f);
        }

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(_cam.gameObject);

        static HighlightData Data(int frames)
        {
            var d = new HighlightData(0f, frames);
            for (int f = 0; f < frames; f++)
            {
                d.Ball[f] = new Vec3(0f, 0.11f, 0f);
                for (int e = 0; e < HighlightData.Players; e++) d.Set(f, e, e, 0f, 0f);
            }
            return d;
        }

        /// <summary>0~2 s 메인(40 m), 2~4 s 클로즈업(6 m, 컷).</summary>
        static CameraTrack Track()
        {
            int n = 120;
            var t = new CameraTrack(0d, n, 8f, 30f);
            for (int f = 0; f < n; f++)
            {
                bool close = f >= 60;
                var pos = new Vec3(0f, 1.5f, close ? -6f : -40f);
                t.Poses[f] = new CameraPose(pos, (new Vec3(0f, 0.11f, 0f) - pos).Normalized(), 0.2f, 0.36f);
                t.Shots[f] = close ? ShotKind.Closeup : ShotKind.Main;
                t.CutAt[f] = f == 60;
            }
            return t;
        }

        [Test]
        public void Medium_CloseupTurnsOnDofAndRim_MainOff()
        {
            var data = Data(120);
            var c = new CinematicModeController(_cam, PresetId.Medium, null);
            try
            {
                c.OnSceneBuilt(1, Track(), data, 9);
                _cam.transform.position = new Vector3(0f, 1.5f, -40f);
                c.Apply(1, 1d, data);
                Assert.AreEqual(0f, c.Weight, 1e-6f);
                Assert.AreEqual(0f, c.DofWeight, 1e-6f);
                Assert.AreEqual(0.4f, Shader.GetGlobalFloat(StadiumLighting.IdRim), 1e-5f, "일반 모드 림 = 기본값");

                _cam.transform.position = new Vector3(0f, 1.5f, -6f);
                c.Apply(1, 3d, data);
                Assert.AreEqual(1f, c.Weight, 1e-6f, "컷 진입 = 바로 고품질");
                Assert.AreEqual(1f, c.DofWeight, 1e-6f, "Medium: DoF 켬 (FR-81)");
                Assert.AreEqual(0.4f * CinematicModeController.RimBoost, Shader.GetGlobalFloat(StadiumLighting.IdRim), 1e-5f);
                Assert.AreEqual(3, c.TopTierCount, "가까운 선수 3명 신호 (062)");
            }
            finally
            {
                c.Dispose();
            }
            Assert.AreEqual(0.4f, Shader.GetGlobalFloat(StadiumLighting.IdRim), 1e-5f, "Dispose 후 림 복원");
        }

        [Test]
        public void Low_ModeOn_ButNoDof()
        {
            var data = Data(120);
            var c = new CinematicModeController(_cam, PresetId.Low, null);
            try
            {
                c.OnSceneBuilt(1, Track(), data, -1);
                c.Apply(1, 3d, data);
                Assert.AreEqual(1f, c.Weight, 1e-6f, "Low도 고품질 모드 판단은 같다");
                Assert.AreEqual(0f, c.DofWeight, 1e-6f, "Low DoF 끔 (FR-81)");
            }
            finally
            {
                c.Dispose();
            }
        }

        [Test]
        public void UnknownScene_NormalMode()
        {
            var c = new CinematicModeController(_cam, PresetId.High, null);
            try
            {
                c.Apply(42, 3d, null);
                Assert.AreEqual(0f, c.Weight);
            }
            finally
            {
                c.Dispose();
            }
        }
    }
}
