using NUnit.Framework;
using RoadToPro.Presentation.Stadium;
using UnityEngine;
using UnityEngine.TestTools;

namespace RoadToPro.Tests.EditMode.Presentation.Stadium
{
    /// <summary>specs/061 FR-10·16·17, AC-03·05·10: 절차 하늘 · 시간대 · 예외.</summary>
    public class SkyTests
    {
        GameObject _go;
        Light _sun;
        StadiumLighting _lighting;
        Camera _cam;

        [SetUp]
        public void SetUp()
        {
            _go = new GameObject("lighting");
            _lighting = _go.AddComponent<StadiumLighting>();
            _sun = new GameObject("sun").AddComponent<Light>();
            _cam = new GameObject("cam").AddComponent<Camera>();
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_go);
            Object.DestroyImmediate(_sun.gameObject);
            Object.DestroyImmediate(_cam.gameObject);
            if (RenderSettings.skybox != null && RenderSettings.skybox.name.EndsWith("(instance)")) Object.DestroyImmediate(RenderSettings.skybox);
            RenderSettings.skybox = null;
        }

        static StadiumLightingPreset Preset(Vector3 sunEuler, bool sky = true)
        {
            var p = ScriptableObject.CreateInstance<StadiumLightingPreset>();
            p.sunEuler = sunEuler;
            p.skyMaterial = sky ? new Material(Shader.Find(StadiumLighting.SkyShaderName)) : null;
            return p;
        }

        [Test]
        public void SkySunAlignment_Within20deg()
        {
            // 하늘의 밝은 쪽(태양 방향 파라미터)과 방향광 방위가 같다 (FR-17)
            foreach (var euler in new[] { new Vector3(52f, -35f, 0f), new Vector3(30f, 120f, 0f), new Vector3(80f, 0f, 0f) })
            {
                _lighting.Configure(_sun, null, new[] { _cam }, new BlobShadow4[0]);
                _lighting.Apply(Preset(euler));
                var sky = RenderSettings.skybox;
                Assert.IsNotNull(sky, "스카이박스");
                Vector3 toSun = sky.GetVector(StadiumLighting.IdSunDir);
                Vector3 light = -_sun.transform.forward;
                float az = Vector2.Angle(new Vector2(toSun.x, toSun.z), new Vector2(light.x, light.z));
                Assert.LessOrEqual(az, 20f, $"태양 {euler}: 하늘 밝은 쪽과 방향광 방위 차 {az:0.0}°");
                Assert.AreEqual(CameraClearFlags.Skybox, _cam.clearFlags);
            }
        }

        [Test]
        public void CloudOffset_FunctionOfSimTime()
        {
            var a = SkyModel.CloudOffset(1234.5d, 3f);
            var b = SkyModel.CloudOffset(1234.5d, 3f);
            Assert.AreEqual(a, b, "같은 시각 → 같은 구름");
            Assert.AreNotEqual(a, SkyModel.CloudOffset(1240d, 3f), "시각이 가면 구름이 흐른다");
            Assert.AreEqual(Vector2.zero, SkyModel.CloudOffset(1234.5d, 0f), "Low 정적 하늘 (FR-34)");
            // 경기 끝 근처(5400 s)에서도 0~1 범위로 감싼 값 (float 정밀도)
            var late = SkyModel.CloudOffset(5399.9d, 3f);
            Assert.That(late.x, Is.InRange(0f, 1f));
            Assert.That(late.y, Is.InRange(0f, 1f));
        }

        [Test]
        public void CloudFrozen_WhenPaused()
        {
            // 키 모먼트 정지 = 시뮬 시각 고정 → 프레임을 여러 번 그려도 같은 값 (상태 없음)
            var first = SkyModel.CloudOffset(100d, 3f);
            for (int i = 0; i < 50; i++) Assert.AreEqual(first, SkyModel.CloudOffset(100d, 3f));
        }

        [Test]
        public void MissingSky_GradientAndOneWarning()
        {
            _lighting.Configure(_sun, null, new[] { _cam }, new BlobShadow4[0]);
            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("하늘"));
            var p = Preset(new Vector3(50f, 0f, 0f), sky: false);
            _lighting.Apply(p);
            _lighting.Apply(p); // 두 번째는 경고 없음
            Assert.AreEqual(CameraClearFlags.SolidColor, _cam.clearFlags, "기본 그라데이션 = 단색 배경 + 3색 환경광");
            Assert.AreEqual(UnityEngine.Rendering.AmbientMode.Trilight, RenderSettings.ambientMode);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void BadTimeOfDay_DayAndOneWarning()
        {
            int warns = 0;
            Assert.AreEqual(TimeOfDay.Day, SkyModel.ParseTimeOfDay("dusk", _ => warns++));
            Assert.AreEqual(1, warns);
            Assert.AreEqual(TimeOfDay.Night, SkyModel.ParseTimeOfDay("Night", _ => warns++));
            Assert.AreEqual(TimeOfDay.Day, SkyModel.ParseTimeOfDay(null, _ => warns++), "지정 안 함 = 기본 낮 (경고 없음)");
            Assert.AreEqual(TimeOfDay.Day, SkyModel.ParseTimeOfDay("12", _ => warns++));
            Assert.AreEqual(2, warns, "잘못된 값마다 경고 1회");
        }
    }

    /// <summary>specs/061 FR-12·13: 하늘 색에서 온 환경광 · 위치별 환경광.</summary>
    public class AmbientTests
    {
        [Test]
        public void AmbientFromSkyColors()
        {
            var top = new Color(0.35f, 0.55f, 0.9f);
            var horizon = new Color(0.75f, 0.8f, 0.85f);
            var ground = new Color(0.2f, 0.25f, 0.15f);
            SkyModel.Ambient(top, horizon, ground, 1f, out var sky, out var eq, out var gr);
            Assert.Greater(sky.b, sky.r, "위 환경광은 하늘색");
            Assert.Greater(eq.grayscale, gr.grayscale, "지평선 > 지면");
            Assert.Greater(sky.grayscale, 0.2f, "그림자 안이 검게 뭉개지지 않을 만큼 (FR-12)");
            SkyModel.Ambient(top, horizon, ground, 0.5f, out var sky2, out _, out _);
            Assert.AreEqual(sky.grayscale * 0.5f, sky2.grayscale, 1e-4f, "세기 배율");
        }

        [Test]
        public void ReflectionCubemap_SkyGradientAndSun()
        {
            var top = new Color(0.26f, 0.46f, 0.80f);
            var horizon = new Color(0.70f, 0.78f, 0.86f);
            var ground = new Color(0.30f, 0.33f, 0.28f);
            var toSun = new Vector3(0.4f, 0.6f, 0.3f);
            var up = SkyModel.SkyColor(Vector3.up, top, horizon, ground, toSun, Color.white);
            var side = SkyModel.SkyColor(new Vector3(-1f, 0.05f, 0f), top, horizon, ground, toSun, Color.white);
            var down = SkyModel.SkyColor(Vector3.down, top, horizon, ground, toSun, Color.white);
            var sunward = SkyModel.SkyColor(new Vector3(0.4f, 0.3f, 0.3f), top, horizon, ground, toSun, Color.white);
            var away = SkyModel.SkyColor(new Vector3(-0.4f, 0.3f, -0.3f), top, horizon, ground, toSun, Color.white);
            Assert.Greater(up.b - up.r, side.b - side.r, "위는 진한 하늘색, 지평선은 옅게");
            Assert.Less(down.grayscale, side.grayscale, "아래는 지면색");
            Assert.Greater(sunward.grayscale, away.grayscale, "같은 고도에서 태양 쪽이 밝다 (FR-14 반사에 하늘·태양이 비침)");
            var avg = SkyModel.AverageSky(top, horizon, ground);
            Assert.That(avg.grayscale, Is.InRange(ground.grayscale, horizon.grayscale));
        }
    }
}
