using System;

namespace RoadToPro.Presentation.Rendering.Logic
{
    /// <summary>
    /// 프리셋 표 (specs/048 FR-01). 061 T06(2026-11-20)부터 세 프리셋 모두 실제 품질 레벨(Low·Medium·High)이다.
    /// URP 템플릿에서 남은 Mobile·PC 레벨 이름은 PC 전용 결정에 맞춰 정리했다. 061 시각 기능은 VisualFeatureTable.
    /// </summary>
    public static class RenderPresetTable
    {
        public const string LowLevel = "Low";
        public const string MediumLevel = "Medium";
        public const string HighLevel = "High";
        /// <summary>품질 레벨이 없을 때(이름 오타 등) 에셋으로만 덮을 때 밑에 깔 레벨.</summary>
        public const string FallbackBaseLevel = "High";

        public static readonly RenderPreset Low = new RenderPreset
        {
            Id = PresetId.Low,
            QualityLevel = LowLevel,
            RenderScale = 0.67f,
            Upscaler = Upscaler.Stp,
            Shadows = ShadowMode.Blob,
            FloodlightShadows = 0,
            Grass = GrassMode.SingleShader,
            Crowd = CrowdMode.Billboard,
            PlayerLod = PlayerLodPolicy.Lod1Default,
            Ssao = false, Volumetric = false, Ssr = false,
            Post = PostStack.ToneMapWeakBloom,
            MaxTextureSize = 1024,
            IsStandIn = false,
        };

        public static readonly RenderPreset Medium = new RenderPreset
        {
            Id = PresetId.Medium,
            QualityLevel = MediumLevel,
            RenderScale = 0.75f,
            Upscaler = Upscaler.Stp,
            Shadows = ShadowMode.DirectionalPlusBlob,
            FloodlightShadows = 0,
            Grass = GrassMode.SingleShader,
            Crowd = CrowdMode.BillboardLod,
            PlayerLod = PlayerLodPolicy.Lod0Closeup,
            Ssao = false, Volumetric = false, Ssr = false,
            Post = PostStack.ToneMapBloomVignette,
            MaxTextureSize = 2048,
            IsStandIn = false,
        };

        public static readonly RenderPreset High = new RenderPreset
        {
            Id = PresetId.High,
            QualityLevel = HighLevel,
            RenderScale = 1.0f,
            Upscaler = Upscaler.Stp, // 061 FR-34·60: 렌더 스케일 1.0에서 STP = 시간적 AA
            Shadows = ShadowMode.Realtime,
            FloodlightShadows = 4,
            Grass = GrassMode.Shell,
            Crowd = CrowdMode.VertexAnimated,
            PlayerLod = PlayerLodPolicy.Lod0Wide,
            Ssao = false, Volumetric = false, Ssr = false,
            Post = PostStack.ToneMapBloomVignetteReplayDof,
            MaxTextureSize = 2048,
            IsStandIn = false,
        };

        public static RenderPreset Get(PresetId id) => id switch
        {
            PresetId.Low => Low,
            PresetId.Medium => Medium,
            PresetId.High => High,
            _ => Low,
        };

        /// <summary>이름("Low"/"Medium"/"High", 대소문자 무시)으로 찾는다. 모르는 이름이면 Low + 진단 1건.</summary>
        public static RenderPreset Find(string name, Action<string> diagnostic)
        {
            if (name != null && Enum.TryParse(name.Trim(), true, out PresetId id) && Enum.IsDefined(typeof(PresetId), id))
                return Get(id);
            diagnostic?.Invoke($"알 수 없는 품질 프리셋 '{name}': Low를 쓴다");
            return Low;
        }
    }
}
