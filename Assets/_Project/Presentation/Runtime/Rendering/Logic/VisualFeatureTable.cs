namespace RoadToPro.Presentation.Rendering.Logic
{
    /// <summary>specs/061 FR-34 공통 기능 (일반·고품질 모드 공유).</summary>
    public enum VisualFeature
    {
        LookStandard,
        Sky,
        Ambient,
        Reflection,
        Backdrop,
        BakedAo,
        SsaoNormalMode,
        ToneGrade,
        Bloom,
        Vignette,
        PlayerShadow,
        ContactDarkening,
        Rim,
        FloodlightShafts,
        AntiAliasing,
        /// <summary>FR-51: 일반 모드 근거리 입체 잎 — 전 프리셋 끔.</summary>
        NearGrassNormalMode,
    }

    public enum FeatureLevel
    {
        Off,
        On,
        /// <summary>반사: 하늘 평균색만.</summary>
        Simple,
        /// <summary>블룸·비네트 약하게.</summary>
        Weak,
        /// <summary>하늘: 정적 (구름 흐름 없음).</summary>
        Static,
        /// <summary>하늘: 구름 흐름.</summary>
        Animated,
        Blob,
        DirectionalPlusBlob,
        Realtime,
        Stp,
        /// <summary>스펙 [미정] — 끔으로 동작.</summary>
        Undecided,
    }

    /// <summary>specs/061 FR-81 고품질 모드에서 더하는 것.</summary>
    public enum CinematicFeature
    {
        PlayerTopTier,
        DepthOfField,
        ShadowRes,
        AoQuality,
        NearGrass,
        RimBoost,
    }

    public readonly struct CinematicSetting
    {
        public readonly bool On;
        /// <summary>항목별 값: 선수 등급 = 인원, AO = 0 접촉 강화·1 강화·2 강화 + SSAO, 잔디 = 셸 겹 수.</summary>
        public readonly int Param;

        public CinematicSetting(bool on, int param = 0)
        {
            On = on;
            Param = param;
        }
    }

    /// <summary>
    /// specs/061 FR-34·81 표 원본. 프리셋 × 기능. 실행 중 자동 조절 없음(Q9) — 예산을 넘으면 이 표를 끄는 순서대로 고친다.
    /// </summary>
    public static class VisualFeatureTable
    {
        public const int NeverCut = int.MaxValue;

        static readonly FeatureLevel[,] Common =
        {
            //                      Low                          Medium                              High
            /* LookStandard     */ { FeatureLevel.On,     FeatureLevel.On,                  FeatureLevel.On },
            /* Sky              */ { FeatureLevel.Static, FeatureLevel.On,                  FeatureLevel.Animated },
            /* Ambient          */ { FeatureLevel.On,     FeatureLevel.On,                  FeatureLevel.On },
            /* Reflection       */ { FeatureLevel.Simple, FeatureLevel.On,                  FeatureLevel.On },
            /* Backdrop         */ { FeatureLevel.On,     FeatureLevel.On,                  FeatureLevel.On },
            /* BakedAo          */ { FeatureLevel.On,     FeatureLevel.On,                  FeatureLevel.On },
            /* SsaoNormalMode   */ { FeatureLevel.Off,    FeatureLevel.Off,                 FeatureLevel.Off },
            /* ToneGrade        */ { FeatureLevel.On,     FeatureLevel.On,                  FeatureLevel.On },
            /* Bloom            */ { FeatureLevel.Weak,   FeatureLevel.Weak,                FeatureLevel.Weak },
            /* Vignette         */ { FeatureLevel.Off,    FeatureLevel.Weak,                FeatureLevel.Weak },
            /* PlayerShadow     */ { FeatureLevel.Blob,   FeatureLevel.DirectionalPlusBlob, FeatureLevel.Realtime },
            /* ContactDarkening */ { FeatureLevel.On,     FeatureLevel.On,                  FeatureLevel.On },
            /* Rim              */ { FeatureLevel.On,     FeatureLevel.On,                  FeatureLevel.On },
            /* FloodlightShafts */ { FeatureLevel.Off,    FeatureLevel.Off,                 FeatureLevel.Undecided },
            /* AntiAliasing     */ { FeatureLevel.Stp,    FeatureLevel.Stp,                 FeatureLevel.Stp },
            /* NearGrassNormal  */ { FeatureLevel.Off,    FeatureLevel.Off,                 FeatureLevel.Off },
        };

        static readonly CinematicSetting[,] Modes =
        {
            //                   Low                          Medium                       High
            /* PlayerTopTier */ { new CinematicSetting(true, 3), new CinematicSetting(true, 3), new CinematicSetting(true, 3) },
            /* DepthOfField  */ { new CinematicSetting(false),   new CinematicSetting(true),    new CinematicSetting(true) },
            /* ShadowRes     */ { new CinematicSetting(true),    new CinematicSetting(true),    new CinematicSetting(true) },
            /* AoQuality     */ { new CinematicSetting(true, 0), new CinematicSetting(true, 1), new CinematicSetting(true, 2) },
            /* NearGrass     */ { new CinematicSetting(true, 4), new CinematicSetting(true, 8), new CinematicSetting(true, 12) },
            /* RimBoost      */ { new CinematicSetting(true),    new CinematicSetting(true),    new CinematicSetting(true) },
        };

        public static FeatureLevel Get(PresetId preset, VisualFeature feature) => Common[(int)feature, Col(preset)];

        public static CinematicSetting Cinematic(PresetId preset, CinematicFeature feature) => Modes[(int)feature, Col(preset)];

        /// <summary>FR-81 "끄는 순서" (예산 초과 시 앞에서부터).</summary>
        public static int CutOrder(CinematicFeature feature) => feature switch
        {
            CinematicFeature.NearGrass => 1,
            CinematicFeature.DepthOfField => 2,
            CinematicFeature.ShadowRes => 3,
            CinematicFeature.AoQuality => 4,
            CinematicFeature.PlayerTopTier => 5,
            _ => NeverCut,
        };

        /// <summary>하늘 텍스처 해상도 (FR-34: Low 1K).</summary>
        public static int SkyResolution(PresetId preset) => preset == PresetId.Low ? 1024 : 2048;

        /// <summary>근거리 잔디 반경 m (FR-50 20 m, FR-58 Low 10 m).</summary>
        public static float GrassRadius(PresetId preset) => preset == PresetId.Low ? 10f : 20f;

        /// <summary>근거리 잔디가 사라지기 시작하는 거리 m (FR-50 15~20 m, FR-58 Low 7~10 m).</summary>
        public static float GrassFadeStart(PresetId preset) => preset == PresetId.Low ? 7f : 15f;

        static int Col(PresetId p) => p switch
        {
            PresetId.Medium => 1,
            PresetId.High => 2,
            _ => 0,
        };
    }
}
