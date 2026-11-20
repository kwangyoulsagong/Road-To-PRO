using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using RoadToPro.Presentation.Rendering.Logic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace RoadToPro.Tests.EditMode.Presentation.Rendering
{
    /// <summary>
    /// specs/061 FR-08·09·82, AC-01 ③: 공통 룩은 세 프리셋·두 모드가 같은 프로필 하나를 쓰고, 고품질 모드 볼륨에는 피사계 심도만 있다
    /// → 모드가 바뀌어도 톤매핑·색 보정·노출 값은 같다 (구조로 보장).
    /// </summary>
    public class LookInvarianceTests
    {
        const string Look = "Assets/Settings/LookVolumeProfile.asset";
        static readonly string[] RpAssets = { "Assets/Settings/Low_RPAsset.asset", "Assets/Settings/Medium_RPAsset.asset", "Assets/Settings/High_RPAsset.asset" };

        static VolumeProfile Load(string path)
        {
            var p = AssetDatabase.LoadAssetAtPath<VolumeProfile>(path);
            Assert.IsNotNull(p, path);
            return p;
        }

        static HashSet<Type> Types(VolumeProfile p) => new HashSet<Type>(p.components.Where(c => c.active).Select(c => c.GetType()));

        [Test]
        public void AllPresets_ShareLookProfile()
        {
            var look = Load(Look);
            foreach (var path in RpAssets)
            {
                var rp = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(path);
                Assert.IsNotNull(rp, path);
                Assert.AreSame(look, rp.volumeProfile, $"{path}의 기본 볼륨이 공통 룩이 아님");
            }
        }

        [Test]
        public void ModeProfile_OnlyDof()
        {
            var mode = RoadToPro.Presentation.Rendering.CinematicVolumes.CreateModeProfile();
            var vig = RoadToPro.Presentation.Rendering.CinematicVolumes.CreateVignetteProfile();
            try
            {
                CollectionAssert.AreEquivalent(new[] { typeof(DepthOfField) }, Types(mode), "고품질 모드 볼륨에 DoF 외 항목이 있으면 색·톤이 모드에 따라 바뀐다 (FR-82)");
                CollectionAssert.AreEquivalent(new[] { typeof(UnityEngine.Rendering.Universal.Vignette) }, Types(vig));
            }
            finally
            {
                foreach (var c in mode.components) UnityEngine.Object.DestroyImmediate(c);
                foreach (var c in vig.components) UnityEngine.Object.DestroyImmediate(c);
                UnityEngine.Object.DestroyImmediate(mode);
                UnityEngine.Object.DestroyImmediate(vig);
            }
        }

        [Test]
        public void BothModes_SameToneColorExposureLight()
        {
            var look = Load(Look);
            CollectionAssert.IsSubsetOf(Types(look), new[] { typeof(Tonemapping), typeof(Bloom), typeof(ColorAdjustments), typeof(ShadowsMidtonesHighlights), typeof(ColorCurves) },
                "공통 룩 프로필 항목");
            Assert.IsTrue(look.TryGet(out Tonemapping tone) && tone.mode.overrideState);
            Assert.AreEqual(LookParameters.NeutralTonemap ? TonemappingMode.Neutral : TonemappingMode.ACES, tone.mode.value);
            Assert.IsTrue(look.TryGet(out ColorAdjustments c));
            Assert.AreEqual(LookParameters.PostExposure, c.postExposure.value, 1e-5f);
            Assert.AreEqual(LookParameters.Contrast, c.contrast.value, 1e-5f);
            Assert.AreEqual(LookParameters.Saturation, c.saturation.value, 1e-5f);
            Assert.IsTrue(look.TryGet(out Bloom b));
            Assert.AreEqual(LookParameters.BloomIntensity, b.intensity.value, 1e-5f);
            Assert.AreEqual(LookParameters.BloomThreshold, b.threshold.value, 1e-5f);
            Assert.IsTrue(look.TryGet(out ShadowsMidtonesHighlights smh));
            Assert.AreEqual(LookParameters.Shadows[2], smh.shadows.value.z, 1e-5f);
            // 고품질 모드 볼륨은 위 항목을 하나도 갖지 않는다 → 모드 가중치와 무관하게 같은 값
            var cine = RoadToPro.Presentation.Rendering.CinematicVolumes.CreateModeProfile();
            Assert.IsFalse(cine.Has<Tonemapping>() || cine.Has<ColorAdjustments>() || cine.Has<Bloom>() || cine.Has<ShadowsMidtonesHighlights>() || cine.Has<ColorCurves>());
            UnityEngine.Object.DestroyImmediate(cine);
        }
    }
}
