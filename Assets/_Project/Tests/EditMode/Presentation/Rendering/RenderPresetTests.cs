using System.Collections.Generic;
using NUnit.Framework;
using RoadToPro.Presentation.Rendering.Logic;

namespace RoadToPro.Tests.EditMode.Presentation.Rendering
{
    /// <summary>프리셋 정의 (specs/048 FR-01, AC-01).</summary>
    public class RenderPresetTests
    {
        [Test]
        public void RenderPreset_Low_MatchesFr01Column()
        {
            var p = RenderPresetTable.Low;
            Assert.AreEqual(PresetId.Low, p.Id);
            Assert.AreEqual("Low", p.QualityLevel);
            Assert.AreEqual(0.67f, p.RenderScale, 1e-6f);
            Assert.AreEqual(Upscaler.Stp, p.Upscaler);
            Assert.AreEqual(ShadowMode.Blob, p.Shadows);
            Assert.AreEqual(0, p.FloodlightShadows, "Low에 실시간 플러드라이트 그림자 없음");
            Assert.AreEqual(GrassMode.SingleShader, p.Grass, "Low에 셸 잔디 없음");
            Assert.AreEqual(CrowdMode.Billboard, p.Crowd);
            Assert.AreEqual(PlayerLodPolicy.Lod1Default, p.PlayerLod);
            Assert.IsFalse(p.Ssao);
            Assert.IsFalse(p.Volumetric);
            Assert.IsFalse(p.Ssr);
            Assert.AreEqual(PostStack.ToneMapWeakBloom, p.Post);
            Assert.AreEqual(1024, p.MaxTextureSize);
            Assert.IsFalse(p.IsStandIn);
        }

        [Test]
        public void RenderPreset_OnlyHighHasShellGrassAndFloodlightShadows()
        {
            foreach (var p in new[] { RenderPresetTable.Low, RenderPresetTable.Medium })
            {
                Assert.AreNotEqual(GrassMode.Shell, p.Grass, p.Id.ToString());
                Assert.AreEqual(0, p.FloodlightShadows, p.Id.ToString());
            }
            Assert.AreEqual(GrassMode.Shell, RenderPresetTable.High.Grass);
            Assert.AreEqual(4, RenderPresetTable.High.FloodlightShadows);
        }

        [Test]
        public void RenderPreset_MediumHigh_AreStandInsForExistingAssets()
        {
            Assert.IsTrue(RenderPresetTable.Medium.IsStandIn);
            Assert.IsTrue(RenderPresetTable.High.IsStandIn);
            Assert.AreEqual("Mobile", RenderPresetTable.Medium.QualityLevel);
            Assert.AreEqual("PC", RenderPresetTable.High.QualityLevel);
        }

        [TestCase("Low", PresetId.Low)]
        [TestCase("medium", PresetId.Medium)]
        [TestCase(" HIGH ", PresetId.High)]
        public void RenderPresetTable_Find_ByName(string name, PresetId expected)
        {
            var diag = new List<string>();
            Assert.AreEqual(expected, RenderPresetTable.Find(name, diag.Add).Id);
            Assert.IsEmpty(diag);
        }

        [TestCase("Ultra")]
        [TestCase("")]
        [TestCase(null)]
        [TestCase("7")]
        public void RenderPresetTable_UnknownName_LowWithDiagnostic(string name)
        {
            var diag = new List<string>();
            Assert.AreEqual(PresetId.Low, RenderPresetTable.Find(name, diag.Add).Id);
            Assert.AreEqual(1, diag.Count);
        }
    }
}
