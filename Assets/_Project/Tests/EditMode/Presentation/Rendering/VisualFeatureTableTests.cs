using NUnit.Framework;
using RoadToPro.Presentation.Rendering.Logic;

namespace RoadToPro.Tests.EditMode.Presentation.Rendering
{
    /// <summary>specs/061 FR-34 공통 기능 표 · FR-81 고품질 모드 구성 표 (AC-01 ①).</summary>
    public class VisualFeatureTableTests
    {
        static readonly PresetId[] All = { PresetId.Low, PresetId.Medium, PresetId.High };

        static void Row(VisualFeature f, FeatureLevel low, FeatureLevel med, FeatureLevel high)
        {
            Assert.AreEqual(low, VisualFeatureTable.Get(PresetId.Low, f), $"{f} Low");
            Assert.AreEqual(med, VisualFeatureTable.Get(PresetId.Medium, f), $"{f} Medium");
            Assert.AreEqual(high, VisualFeatureTable.Get(PresetId.High, f), $"{f} High");
        }

        [Test]
        public void MatchesSpecTables()
        {
            // FR-34 공통 기능 표 (스펙 열 순서 그대로)
            Row(VisualFeature.LookStandard, FeatureLevel.On, FeatureLevel.On, FeatureLevel.On);
            Row(VisualFeature.Sky, FeatureLevel.Static, FeatureLevel.On, FeatureLevel.Animated);
            Row(VisualFeature.Ambient, FeatureLevel.On, FeatureLevel.On, FeatureLevel.On);
            Row(VisualFeature.Reflection, FeatureLevel.Simple, FeatureLevel.On, FeatureLevel.On);
            Row(VisualFeature.Backdrop, FeatureLevel.On, FeatureLevel.On, FeatureLevel.On);
            Row(VisualFeature.BakedAo, FeatureLevel.On, FeatureLevel.On, FeatureLevel.On);
            Row(VisualFeature.SsaoNormalMode, FeatureLevel.Off, FeatureLevel.Off, FeatureLevel.Off);
            Row(VisualFeature.ToneGrade, FeatureLevel.On, FeatureLevel.On, FeatureLevel.On);
            Row(VisualFeature.Bloom, FeatureLevel.Weak, FeatureLevel.Weak, FeatureLevel.Weak);
            Row(VisualFeature.Vignette, FeatureLevel.Off, FeatureLevel.Weak, FeatureLevel.Weak);
            Row(VisualFeature.PlayerShadow, FeatureLevel.Blob, FeatureLevel.DirectionalPlusBlob, FeatureLevel.Realtime);
            Row(VisualFeature.ContactDarkening, FeatureLevel.On, FeatureLevel.On, FeatureLevel.On);
            Row(VisualFeature.Rim, FeatureLevel.On, FeatureLevel.On, FeatureLevel.On);
            Row(VisualFeature.FloodlightShafts, FeatureLevel.Off, FeatureLevel.Off, FeatureLevel.Undecided);
            Row(VisualFeature.AntiAliasing, FeatureLevel.Stp, FeatureLevel.Stp, FeatureLevel.Stp);
            Assert.AreEqual(1024, VisualFeatureTable.SkyResolution(PresetId.Low), "하늘 Low 1K (FR-34)");

            // FR-81 고품질 모드 구성 표
            foreach (var p in All)
            {
                Assert.IsTrue(VisualFeatureTable.Cinematic(p, CinematicFeature.PlayerTopTier).On, $"{p} 최고 품질 등급");
                Assert.AreEqual(3, VisualFeatureTable.Cinematic(p, CinematicFeature.PlayerTopTier).Param, $"{p} 최고 품질 3명");
                Assert.IsTrue(VisualFeatureTable.Cinematic(p, CinematicFeature.ShadowRes).On, $"{p} 그림자 해상도");
                Assert.IsTrue(VisualFeatureTable.Cinematic(p, CinematicFeature.AoQuality).On, $"{p} AO 품질");
                Assert.IsTrue(VisualFeatureTable.Cinematic(p, CinematicFeature.RimBoost).On, $"{p} 림 보정");
                Assert.IsTrue(VisualFeatureTable.Cinematic(p, CinematicFeature.NearGrass).On, $"{p} 근거리 잔디 (FR-58: 전 프리셋)");
            }
            Assert.IsFalse(VisualFeatureTable.Cinematic(PresetId.Low, CinematicFeature.DepthOfField).On, "Low DoF 끔");
            Assert.IsTrue(VisualFeatureTable.Cinematic(PresetId.Medium, CinematicFeature.DepthOfField).On);
            Assert.IsTrue(VisualFeatureTable.Cinematic(PresetId.High, CinematicFeature.DepthOfField).On);
            // AO: 0 접촉 어둡기 강화(Low) · 1 강화(Medium) · 2 강화 + 화면 공간 AO(High, Q4)
            Assert.AreEqual(0, VisualFeatureTable.Cinematic(PresetId.Low, CinematicFeature.AoQuality).Param);
            Assert.AreEqual(1, VisualFeatureTable.Cinematic(PresetId.Medium, CinematicFeature.AoQuality).Param);
            Assert.AreEqual(2, VisualFeatureTable.Cinematic(PresetId.High, CinematicFeature.AoQuality).Param);
            // 근거리 잔디 겹 수·반경 (FR-50·58)
            Assert.AreEqual(4, VisualFeatureTable.Cinematic(PresetId.Low, CinematicFeature.NearGrass).Param);
            Assert.AreEqual(8, VisualFeatureTable.Cinematic(PresetId.Medium, CinematicFeature.NearGrass).Param);
            Assert.AreEqual(12, VisualFeatureTable.Cinematic(PresetId.High, CinematicFeature.NearGrass).Param);
            Assert.AreEqual(10f, VisualFeatureTable.GrassRadius(PresetId.Low), 1e-6f);
            Assert.AreEqual(20f, VisualFeatureTable.GrassRadius(PresetId.Medium), 1e-6f);
            Assert.AreEqual(20f, VisualFeatureTable.GrassRadius(PresetId.High), 1e-6f);
        }

        [Test]
        public void CutOrder_MatchesFr81()
        {
            Assert.AreEqual(1, VisualFeatureTable.CutOrder(CinematicFeature.NearGrass));
            Assert.AreEqual(2, VisualFeatureTable.CutOrder(CinematicFeature.DepthOfField));
            Assert.AreEqual(3, VisualFeatureTable.CutOrder(CinematicFeature.ShadowRes));
            Assert.AreEqual(4, VisualFeatureTable.CutOrder(CinematicFeature.AoQuality));
            Assert.AreEqual(5, VisualFeatureTable.CutOrder(CinematicFeature.PlayerTopTier));
            Assert.AreEqual(VisualFeatureTable.NeverCut, VisualFeatureTable.CutOrder(CinematicFeature.RimBoost), "림 보정은 끄지 않음");
        }

        [Test]
        public void NormalMode_NoNearGrass_AllPresets()
        {
            // FR-51: 일반 모드는 입체 잎 없음 — 근거리 잔디는 고품질 모드 구성 표에만 있다
            foreach (var p in All)
                Assert.AreEqual(FeatureLevel.Off, VisualFeatureTable.Get(p, VisualFeature.NearGrassNormalMode), p.ToString());
        }
    }
}
