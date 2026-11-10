namespace RoadToPro.Presentation.Rendering.Logic
{
    public enum PresetId { Low, Medium, High }

    public enum Upscaler { None, Stp }

    /// <summary>선수 그림자 방식 (specs/048 FR-01).</summary>
    public enum ShadowMode
    {
        /// <summary>발밑 블롭 (나이트 4장).</summary>
        Blob,
        /// <summary>방향광 그림자 1개 + 블롭.</summary>
        DirectionalPlusBlob,
        /// <summary>실시간 그림자 (방향광 + 플러드라이트 4개).</summary>
        Realtime,
    }

    public enum GrassMode { SingleShader, Shell }

    public enum CrowdMode { Billboard, BillboardLod, VertexAnimated }

    public enum PlayerLodPolicy
    {
        /// <summary>LOD1 위주, 클로즈업만 LOD0.</summary>
        Lod1Default,
        /// <summary>LOD1 기본, 클로즈업 LOD0.</summary>
        Lod0Closeup,
        /// <summary>LOD0 범위 확대.</summary>
        Lod0Wide,
    }

    public enum PostStack
    {
        /// <summary>톤맵 + 약한 블룸.</summary>
        ToneMapWeakBloom,
        /// <summary>톤맵 + 블룸 + 비네트.</summary>
        ToneMapBloomVignette,
        /// <summary>Medium + 리플레이 DoF.</summary>
        ToneMapBloomVignetteReplayDof,
    }

    /// <summary>
    /// 품질 프리셋 정의 한 열 (specs/048 FR-01). 값은 데이터일 뿐이고 적용은 <c>RenderQualityApplier</c>가 한다.
    /// 클로스 시뮬레이션은 모든 프리셋에서 끄므로 항목이 없다.
    /// </summary>
    public sealed class RenderPreset
    {
        public PresetId Id;
        /// <summary>Unity 품질 레벨 이름 (QualitySettings).</summary>
        public string QualityLevel;
        public float RenderScale;
        public Upscaler Upscaler;
        public ShadowMode Shadows;
        /// <summary>실시간 그림자를 만드는 플러드라이트 수 (High 4, 나머지 0).</summary>
        public int FloodlightShadows;
        public GrassMode Grass;
        public CrowdMode Crowd;
        public PlayerLodPolicy PlayerLod;
        public bool Ssao, Volumetric, Ssr;
        public PostStack Post;
        public int MaxTextureSize;
        /// <summary>true면 후속 스펙 전까지 기존 URP 에셋이 대역 (P1, 2026-11-10 결정).</summary>
        public bool IsStandIn;
    }
}
