using System.IO;
using RoadToPro.Presentation.Rendering.Logic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace RoadToPro.Presentation.Editor.Rendering
{
    /// <summary>
    /// Low 프리셋 에셋 생성 (specs/048 T04). 기존 Mobile/PC 에셋은 건드리지 않고 Mobile을 복사해 Low를 만든다.
    /// 배치: Unity -batchmode -quit -executeMethod RoadToPro.Presentation.Editor.Rendering.LowPresetAssets.GenerateBatch
    /// </summary>
    public static class LowPresetAssets
    {
        public const string Dir = "Assets/Settings";
        public const string RpPath = Dir + "/Low_RPAsset.asset";
        public const string RendererPath = Dir + "/Low_Renderer.asset";
        public const string VolumePath = Dir + "/LowVolumeProfile.asset";
        const string MobileRp = Dir + "/Mobile_RPAsset.asset";
        const string MobileRenderer = Dir + "/Mobile_Renderer.asset";

        /// <summary>약한 블룸: 밝은 조명탑·하늘만 살짝 번진다.</summary>
        public const float BloomIntensity = 0.25f, BloomThreshold = 1.1f, BloomScatter = 0.5f;

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

        [MenuItem("Road To PRO/Rendering/Generate Low Preset Assets")]
        public static void Generate()
        {
            AssignMax1K();
            var preset = RenderPresetTable.Low;
            if (!File.Exists(RendererPath) && !AssetDatabase.CopyAsset(MobileRenderer, RendererPath))
                throw new IOException("Low_Renderer 복사 실패");
            if (!File.Exists(RpPath) && !AssetDatabase.CopyAsset(MobileRp, RpPath))
                throw new IOException("Low_RPAsset 복사 실패");

            var renderer = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(RendererPath);
            var rp = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(RpPath);
            var volume = Volume();

            var so = new SerializedObject(rp);
            var list = so.FindProperty("m_RendererDataList");
            list.arraySize = 1;
            list.GetArrayElementAtIndex(0).objectReferenceValue = renderer;
            so.FindProperty("m_DefaultRendererIndex").intValue = 0;
            so.FindProperty("m_RenderScale").floatValue = preset.RenderScale;
            so.FindProperty("m_UpscalingFilter").intValue = (int)UpscalingFilterSelection.STP;
            so.FindProperty("m_MSAA").intValue = 1; // STP는 MSAA와 같이 못 쓴다
            so.FindProperty("m_MainLightShadowsSupported").boolValue = false; // 블롭 섀도 (FR-01 Low)
            so.FindProperty("m_AdditionalLightShadowsSupported").boolValue = false;
            so.FindProperty("m_SoftShadowsSupported").boolValue = false;
            so.FindProperty("m_VolumeProfile").objectReferenceValue = volume;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(rp);
            AssetDatabase.SaveAssets();

            Debug.Log($"[048] Low 에셋: 렌더 스케일 {rp.renderScale} 업스케일 {rp.upscalingFilter} MSAA {rp.msaaSampleCount} " +
                      $"메인 그림자 {rp.supportsMainLightShadows} 렌더러 {renderer.name} 볼륨 {volume.name} " +
                      $"(블룸 {BloomIntensity}, 임계 {BloomThreshold}, 톤맵 Neutral)");
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

        static VolumeProfile Volume()
        {
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(VolumePath);
            if (profile == null)
            {
                profile = ScriptableObject.CreateInstance<VolumeProfile>();
                AssetDatabase.CreateAsset(profile, VolumePath);
            }
            if (!profile.TryGet(out Tonemapping tone))
            {
                tone = profile.Add<Tonemapping>(true);
                AssetDatabase.AddObjectToAsset(tone, profile);
            }
            tone.active = true;
            tone.mode.Override(TonemappingMode.Neutral);

            if (!profile.TryGet(out Bloom bloom))
            {
                bloom = profile.Add<Bloom>(true);
                AssetDatabase.AddObjectToAsset(bloom, profile);
            }
            bloom.active = true;
            bloom.intensity.Override(BloomIntensity);
            bloom.threshold.Override(BloomThreshold);
            bloom.scatter.Override(BloomScatter);
            bloom.highQualityFiltering.Override(false);
            EditorUtility.SetDirty(profile);
            return profile;
        }

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
