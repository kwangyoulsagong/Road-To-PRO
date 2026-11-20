using System.IO;
using RoadToPro.Presentation.Rendering.Logic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace RoadToPro.Presentation.Editor.Rendering
{
    /// <summary>
    /// 품질 프리셋 URP 에셋 설정 (specs/048 T04 Low → specs/061 T06 Low·Medium·High).
    /// 2026-11-20: URP 템플릿에서 남은 Mobile·PC 에셋을 Medium·High로 이름을 바꿨다(GUID 유지). PC 전용.
    /// 배치: Unity -batchmode -quit -executeMethod RoadToPro.Presentation.Editor.Rendering.PresetAssets.GenerateBatch
    /// </summary>
    public static class PresetAssets
    {
        public const string Dir = "Assets/Settings";
        public const string RpPath = Dir + "/Low_RPAsset.asset";
        public const string RendererPath = Dir + "/Low_Renderer.asset";
        /// <summary>공통 룩 (061 FR-08, 세 프리셋·두 모드 공유 — URP 에셋 기본 볼륨).</summary>
        public const string VolumePath = Dir + "/LookVolumeProfile.asset";
        // 비네트(Medium·High)·고품질 모드(DoF만) 볼륨은 런타임 CinematicVolumes가 코드로 만든다 (061 T15).
        public const string MediumRpPath = Dir + "/Medium_RPAsset.asset";
        public const string MediumRendererPath = Dir + "/Medium_Renderer.asset";
        public const string HighRpPath = Dir + "/High_RPAsset.asset";
        public const string HighRendererPath = Dir + "/High_Renderer.asset";

        public const string Max1KGroup = "Max1K";

        /// <summary>
        /// 2K 텍스처 중 화면에 그려지는 것 (Low에서 1K로 제한, FR-01). 이름 글리프 아틀라스(2K)는 밉맵 없이 CPU에서만 읽어
        /// 1K 키트 아틀라스를 만드는 원본이라 제외한다.
        /// </summary>
        public static readonly string[] Max1KTextures =
        {
            "Assets/_Project/Presentation/Players/Generated/Textures/skin_closeup.png",
            "Assets/_Project/Presentation/Players/Generated/Textures/skin_closeup_normal.png",
        };

        [MenuItem("Road To PRO/Rendering/Generate Preset Assets")]
        public static void Generate()
        {
            AssignMax1K();
            var volume = Look();
            Configure(RenderPresetTable.Low, RpPath, RendererPath, volume, shadowDistance: 0f, cascades: 1);
            Configure(RenderPresetTable.Medium, MediumRpPath, MediumRendererPath, volume, shadowDistance: 50f, cascades: 1);
            Configure(RenderPresetTable.High, HighRpPath, HighRendererPath, volume, shadowDistance: 50f, cascades: 4);
            // 061 FR-30: 화면 공간 AO는 High 고품질 모드에서만. 에셋에서 끄면 빌드가 SSAO 셰이더를 빼(stripping) 런타임에 켤 수 없다 →
            // 에셋은 켜 두고, 하이라이트 로딩 때 CinematicModeController가 끄고 고품질 모드에서만 켠다.
            SetSsao(HighRendererPath, true);
            AssetDatabase.SaveAssets();
        }

        /// <summary>
        /// 프리셋 한 열을 URP 에셋에 적는다. 블롭 섀도(Low)는 방향광 그림자 거리 0으로 끈다 — '지원'을 끄면 셰이더 프리필터링이 0이 되어
        /// 다른 레벨과 합칠 때 캐스케이드 그림자 변형이 빠진다 (048 T16 회귀).
        /// </summary>
        static void Configure(RenderPreset preset, string rpPath, string rendererPath, VolumeProfile volume, float shadowDistance, int cascades)
        {
            var rp = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(rpPath);
            var renderer = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(rendererPath);
            if (rp == null || renderer == null) throw new IOException($"URP 에셋 없음: {rpPath} / {rendererPath}");
            var so = new SerializedObject(rp);
            var list = so.FindProperty("m_RendererDataList");
            list.arraySize = 1;
            list.GetArrayElementAtIndex(0).objectReferenceValue = renderer;
            so.FindProperty("m_DefaultRendererIndex").intValue = 0;
            so.FindProperty("m_RenderScale").floatValue = preset.RenderScale;
            so.FindProperty("m_UpscalingFilter").intValue = (int)(preset.Upscaler == Upscaler.Stp ? UpscalingFilterSelection.STP : UpscalingFilterSelection.Auto);
            so.FindProperty("m_MSAA").intValue = 1; // STP는 MSAA와 같이 못 쓴다
            so.FindProperty("m_MainLightShadowsSupported").boolValue = true;
            so.FindProperty("m_ShadowDistance").floatValue = shadowDistance;
            so.FindProperty("m_ShadowCascadeCount").intValue = cascades;
            bool realtime = preset.Shadows == ShadowMode.Realtime;
            so.FindProperty("m_AdditionalLightShadowsSupported").boolValue = realtime;
            so.FindProperty("m_SoftShadowsSupported").boolValue = realtime;
            so.FindProperty("m_VolumeProfile").objectReferenceValue = volume;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(rp);
            Debug.Log($"[061] {preset.Id}: 렌더 스케일 {rp.renderScale} 업스케일 {rp.upscalingFilter} MSAA {rp.msaaSampleCount} " +
                      $"그림자 거리 {rp.shadowDistance} 캐스케이드 {rp.shadowCascadeCount} 렌더러 {renderer.name} 볼륨 {volume.name}");
        }

        static void SetSsao(string rendererPath, bool active)
        {
            var renderer = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(rendererPath);
            foreach (var f in renderer.rendererFeatures)
                if (f is ScreenSpaceAmbientOcclusion)
                {
                    f.SetActive(active);
                    EditorUtility.SetDirty(f);
                }
            EditorUtility.SetDirty(renderer);
        }

        static void AssignMax1K()
        {
            foreach (var path in Max1KTextures)
            {
                if (!(AssetImporter.GetAtPath(path) is TextureImporter ti)) throw new IOException("텍스처 없음: " + path);
                if (ti.mipmapLimitGroupName == Max1KGroup) continue;
                ti.mipmapLimitGroupName = Max1KGroup;
                ti.SaveAndReimport();
            }
            Debug.Log($"[048] 밉맵 제한 그룹 {Max1KGroup}: {Max1KTextures.Length}장");
        }

        static VolumeProfile Profile(string path)
        {
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(path);
            if (profile == null)
            {
                profile = ScriptableObject.CreateInstance<VolumeProfile>();
                AssetDatabase.CreateAsset(profile, path);
            }
            return profile;
        }

        static T Get<T>(VolumeProfile profile) where T : VolumeComponent
        {
            if (!profile.TryGet(out T c))
            {
                c = profile.Add<T>(true);
                AssetDatabase.AddObjectToAsset(c, profile);
            }
            c.active = true;
            return c;
        }

        /// <summary>공통 룩 (061 FR-05~08·31·33): 톤맵·노출·대비·채도·녹색 차분·그림자 푸른 기·약한 블룸.</summary>
        static VolumeProfile Look()
        {
            var profile = Profile(VolumePath);
            Get<Tonemapping>(profile).mode.Override(LookParameters.NeutralTonemap ? TonemappingMode.Neutral : TonemappingMode.ACES);

            var bloom = Get<Bloom>(profile);
            bloom.intensity.Override(LookParameters.BloomIntensity);
            bloom.threshold.Override(LookParameters.BloomThreshold);
            bloom.scatter.Override(LookParameters.BloomScatter);
            bloom.highQualityFiltering.Override(false);

            var color = Get<ColorAdjustments>(profile);
            color.postExposure.Override(LookParameters.PostExposure);
            color.contrast.Override(LookParameters.Contrast);
            color.saturation.Override(LookParameters.Saturation);

            var smh = Get<ShadowsMidtonesHighlights>(profile);
            smh.shadows.Override(V4(LookParameters.Shadows));
            smh.highlights.Override(V4(LookParameters.Highlights));

            var curves = Get<ColorCurves>(profile);
            float h = LookParameters.GreenHue, w = LookParameters.GreenHueWidth;
            curves.hueVsSat.Override(new TextureCurve(new[]
            {
                new Keyframe(h - w, 0.5f), new Keyframe(h, LookParameters.GreenSaturation), new Keyframe(h + w, 0.5f),
            }, 0.5f, true, new Vector2(0f, 1f)));
            EditorUtility.SetDirty(profile);
            return profile;
        }

        static Vector4 V4(float[] a) => new Vector4(a[0], a[1], a[2], a[3]);

        public static void GenerateBatch()
        {
            bool ok = false;
            try
            {
                Generate();
                ok = true;
            }
            catch (System.Exception e)
            {
                Debug.LogException(e);
            }
            if (Application.isBatchMode) EditorApplication.Exit(ok ? 0 : 1);
        }
    }
}
