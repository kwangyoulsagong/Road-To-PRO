using System;

namespace RoadToPro.Presentation.Rendering.Logic
{
    /// <summary>
    /// 프리셋 표 (specs/048 FR-01). P1은 Low만 실제 정의이고 Medium/High는 기존 Mobile/PC 에셋 대역이다.
    /// 대역 열의 값은 기획서 FR-01 표대로 적되 적용은 후속 스펙에서 한다.
    /// </summary>
    public static class RenderPresetTable
    {
        public const string LowLevel = "Low";
        public const string MediumStandInLevel = "Mobile";
        public const string HighStandInLevel = "PC";
        /// <summary>
        /// 이 플랫폼에서 꺼진 대역 레벨(Mobile은 Standalone 제외)을 에셋으로만 덮을 때 밑에 깔 레벨.
        /// 048 전 디바이스팜이 쓰던 조합(PC 레벨 + Mobile 에셋)과 같게 해서 기존 기준 이미지를 지킨다.
        /// </summary>
        public const string FallbackBaseLevel = "PC";

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
            QualityLevel = MediumStandInLevel,
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
            IsStandIn = true,
        };

        public static readonly RenderPreset High = new RenderPreset
        {
            Id = PresetId.High,
            QualityLevel = HighStandInLevel,
            RenderScale = 1.0f,
            Upscaler = Upscaler.None,
            Shadows = ShadowMode.Realtime,
            FloodlightShadows = 4,
            Grass = GrassMode.Shell,
            Crowd = CrowdMode.VertexAnimated,
            PlayerLod = PlayerLodPolicy.Lod0Wide,
            Ssao = false, Volumetric = false, Ssr = false,
            Post = PostStack.ToneMapBloomVignetteReplayDof,
            MaxTextureSize = 2048,
            IsStandIn = true,
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
