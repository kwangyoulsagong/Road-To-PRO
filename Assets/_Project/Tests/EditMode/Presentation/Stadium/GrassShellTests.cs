using NUnit.Framework;
using RoadToPro.Presentation.Rendering.Logic;
using RoadToPro.Presentation.Stadium;
using UnityEngine;

namespace RoadToPro.Tests.EditMode.Presentation.Stadium
{
    /// <summary>specs/061 FR-50~58, AC-08 로직 부분·AC-10: 근거리 셸 잔디 파라미터 (셰이더와 같은 식의 C# 판).</summary>
    public class GrassShellTests
    {
        [Test]
        public void LeafHeight_Max3cm()
        {
            Assert.LessOrEqual(GrassShell.LeafHeight, 0.03f + 1e-6f, "잎 높이 ≤ 3 cm (FR-52) — 공 반지름 0.11 m보다 훨씬 낮다");
            Assert.Greater(GrassShell.LeafHeight, 0.005f);
        }

        [Test]
        public void Fade_15to20m()
        {
            foreach (var p in new[] { PresetId.Medium, PresetId.High })
            {
                Assert.AreEqual(1f, GrassShell.Fade(5f, p), 1e-6f);
                Assert.AreEqual(1f, GrassShell.Fade(15f, p), 1e-6f);
                Assert.AreEqual(0f, GrassShell.Fade(20f, p), 1e-6f);
                Assert.That(GrassShell.Fade(17.5f, p), Is.InRange(0.01f, 0.99f));
            }
            Assert.AreEqual(1f, GrassShell.Fade(7f, PresetId.Low), 1e-6f, "Low 7~10 m (FR-58)");
            Assert.AreEqual(0f, GrassShell.Fade(10f, PresetId.Low), 1e-6f);
            float prev = 2f;
            for (float d = 0f; d < 25f; d += 0.25f)
            {
                float f = GrassShell.Fade(d, PresetId.High);
                Assert.LessOrEqual(f, prev, "거리에 따라 단조 감소 (소멸 경계에서 튀지 않음)");
                prev = f;
            }
        }

        [Test]
        public void LeanDirection_MatchesMowBand15deg()
        {
            // 044 깎기 줄무늬: 직선 패턴은 띠(폭 w)마다 ±X — 잎이 눕는 방향이 띠 방향과 같아야 명암 뒤집힘이 유지된다
            const float w = 5f;
            for (float x = -50f; x <= 50f; x += 1.3f)
            {
                var lean = GrassShell.LeanDirection(new Vector2(x, 7f), w, 0);
                float band = Mathf.Floor(x / w);
                float s = Mathf.Abs(band % 2f) < 0.5f ? 1f : -1f;
                Assert.LessOrEqual(Vector2.Angle(lean, new Vector2(s, 0f)), 15f, $"x={x}");
            }
            // 원형 패턴: 반지름 방향 ±
            var r = GrassShell.LeanDirection(new Vector2(3f, 4f), w, 2);
            Assert.LessOrEqual(Vector2.Angle(new Vector2(Mathf.Abs(r.x), Mathf.Abs(r.y)), new Vector2(0.6f, 0.8f)), 15f);
        }

        [Test]
        public void Wind_SameTime_SamePose()
        {
            var p = new Vector2(12.3f, -4.5f);
            Assert.AreEqual(GrassShell.Wind(321.25d, p), GrassShell.Wind(321.25d, p), "같은 시뮬 시각·위치 → 같은 흔들림 (재생 속도·fps 무관)");
            Assert.AreNotEqual(GrassShell.Wind(321.25d, p), GrassShell.Wind(322.0d, p));
            // 경기 끝 근처 시각도 감싼 위상이라 정밀도 문제 없음
            Assert.IsFalse(float.IsNaN(GrassShell.Wind(5399.97d, p).x));
        }

        [Test]
        public void Wind_Amplitude_Max20pct()
        {
            float max = 0f;
            for (double t = 0d; t < 30d; t += 0.07d)
                for (float x = -20f; x < 20f; x += 3.1f)
                    max = Mathf.Max(max, GrassShell.Wind(t, new Vector2(x, x * 0.3f)).magnitude);
            Assert.LessOrEqual(max, 0.2f + 1e-5f, "흔들림 폭 ≤ 잎 높이의 20% (FR-55, 잎 높이 배율)");
            Assert.Greater(max, 0.02f);
        }

        [Test]
        public void SnowCovered_Off()
        {
            Assert.IsFalse(GrassShell.ShouldDraw(PresetId.High, 1f, snowCovered: true), "눈 덮임이면 끈다 (FR-89)");
            Assert.IsTrue(GrassShell.ShouldDraw(PresetId.High, 1f, snowCovered: false));
        }

        [Test]
        public void LowPreset_Cinematic_4Shells10m()
        {
            Assert.IsTrue(GrassShell.ShouldDraw(PresetId.Low, 1f, false), "Low도 연출 장면 잔디 켬 (FR-58)");
            Assert.AreEqual(4, GrassShell.Shells(PresetId.Low));
            Assert.AreEqual(10f, GrassShell.Radius(PresetId.Low), 1e-6f);
            Assert.AreEqual(8, GrassShell.Shells(PresetId.Medium));
            Assert.AreEqual(12, GrassShell.Shells(PresetId.High));
        }

        [Test]
        public void AllPresets_NormalMode_Off()
        {
            foreach (var p in new[] { PresetId.Low, PresetId.Medium, PresetId.High })
                Assert.IsFalse(GrassShell.ShouldDraw(p, 0f, false), $"{p}: 일반 모드(가중치 0)에서는 입체 잎 없음 (FR-51)");
        }

        [Test]
        public void PatchCenter_SnapsToGrid()
        {
            // 카메라가 조금 움직여도 격자가 미끄러지지 않게 셀 단위로 붙는다
            var a = GrassShell.PatchCenter(new Vector3(1.02f, 1.5f, 2.01f), new Vector3(0f, -0.2f, 1f), PresetId.High);
            var b = GrassShell.PatchCenter(new Vector3(1.04f, 1.5f, 2.03f), new Vector3(0f, -0.2f, 1f), PresetId.High);
            Assert.AreEqual(a, b);
            Assert.AreEqual(0f, Mathf.Repeat(a.x, GrassShell.SnapMeters), 1e-4f);
        }
    }
}
