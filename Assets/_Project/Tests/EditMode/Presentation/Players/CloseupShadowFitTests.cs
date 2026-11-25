using NUnit.Framework;
using RoadToPro.Presentation.Players.Logic;

namespace RoadToPro.Tests.EditMode.Presentation.Players
{
    /// <summary>088 T30i: 클로즈업 전용 그림자 맵 범위 — 머리 최대 2명을 덮는 구.</summary>
    public class CloseupShadowFitTests
    {
        [Test]
        public void Empty_NoFit()
        {
            Assert.IsFalse(CloseupShadowFit.Fit(new float[0], 0, out _, out _, out _, out _));
        }

        [Test]
        public void OneHead_CenteredWithPad()
        {
            var p = new[] { 1f, 1.7f, -2f };
            Assert.IsTrue(CloseupShadowFit.Fit(p, 1, out float cx, out float cy, out float cz, out float r));
            Assert.AreEqual(1f, cx, 1e-5f);
            Assert.AreEqual(1.7f, cy, 1e-5f);
            Assert.AreEqual(-2f, cz, 1e-5f);
            Assert.AreEqual(CloseupShadowFit.HeadPad, r, 1e-5f);
        }

        [Test]
        public void TwoHeads_BothCovered()
        {
            var p = new[] { 0f, 1.7f, 0f, 0.8f, 1.6f, 0.3f };
            Assert.IsTrue(CloseupShadowFit.Fit(p, 2, out float cx, out float cy, out float cz, out float r));
            for (int i = 0; i < 2; i++)
            {
                float dx = p[i * 3] - cx, dy = p[i * 3 + 1] - cy, dz = p[i * 3 + 2] - cz;
                float d = (float)System.Math.Sqrt(dx * dx + dy * dy + dz * dz);
                Assert.LessOrEqual(d + CloseupShadowFit.HeadPad, r + 1e-4f, $"머리 {i}가 범위 밖");
            }
        }

        [Test]
        public void TexelSize_FromRadius()
        {
            // 머리 하나(반경 0.25 m)를 2048²에 — 텍셀 ≈ 0.24 mm (경기장 맵 얼굴 위 ≈ 3 mm의 1/10 이하)
            float t = CloseupShadowFit.TexelSize(CloseupShadowFit.HeadPad, CloseupShadowFit.Resolution);
            Assert.AreEqual(2f * CloseupShadowFit.HeadPad / CloseupShadowFit.Resolution, t, 1e-7f);
            Assert.Less(t, 0.0003f);
        }
    }
}
