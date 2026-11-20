namespace RoadToPro.Presentation.Rendering.Logic
{
    /// <summary>
    /// specs/061 FR-08 공통 룩 수치 (일반·고품질 모드·낮·나이트·세 프리셋이 공유). 볼륨 생성기(Editor)와 테스트가 같은 값을 읽는다.
    /// 실존 방송사 색 보정을 복제하지 않은 자체 값이다 (01-legal). T14에서 캡처를 보며 조정한다 — 값은 [제안].
    /// </summary>
    public static class LookParameters
    {
        // 톤매핑: Neutral (색상 이동이 적어 팀 색 ΔE를 지키기 쉽다, FR-32)
        public const bool NeutralTonemap = true;

        // 노출·대비·채도 (FR-05·06): 원색 평면을 줄이고 나이트가 너무 어둡지 않게
        public const float PostExposure = 0.42f; // T14: 0.15 → 0.45(물빠짐) → 0.3, T19: 낮 L* 39~47 → 0.42
        public const float Contrast = 22f; // T14: 8은 납작함 (5~95% 폭 10~50)
        public const float Saturation = -10f; // T14: −22는 우윳빛

        /// <summary>녹색 차분하게 (FR-08): 색상 대 채도 곡선에서 녹색 근처 채도 배율 (0.5 = 그대로).</summary>
        public const float GreenHue = 0.27f, GreenHueWidth = 0.12f, GreenSaturation = 0.36f; // T19: 잔디 화면 평균 채도 0.45 → ≤ 0.40 // 잔디 색상 75~110° ≈ 0.21~0.31

        /// <summary>그림자에 약간 푸른 기, 하이라이트는 약간 따뜻하게 (FR-08). RGB 곱 + 오프셋(w).</summary>
        public static readonly float[] Shadows = { 0.97f, 0.99f, 1.06f, 0f };
        public static readonly float[] Highlights = { 1.02f, 1.0f, 0.97f, 0f };

        // 블룸 (FR-33): 은은하게 — 조명탑 램프만
        public const float BloomIntensity = 0.25f, BloomThreshold = 1.1f, BloomScatter = 0.5f;

        // 비네트 (FR-34: Low 끔, Medium·High 약하게)
        public const float VignetteIntensity = 0.18f, VignetteSmoothness = 0.5f;

        // 피사계 심도 (FR-83, 고품질 모드 볼륨): Gaussian, 초점 뒤 시작·끝 (m), 최대 반경
        public const float DofStartBehindFocus = 4f, DofEndBehindFocus = 18f, DofMaxRadius = 1.0f;
    }
}
