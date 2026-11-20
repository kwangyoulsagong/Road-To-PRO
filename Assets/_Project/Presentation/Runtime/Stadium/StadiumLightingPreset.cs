using UnityEngine;

namespace RoadToPro.Presentation.Stadium
{
    /// <summary>조명 프리셋 하나 (specs/044 FR-40: 낮·나이트 2종 확정, specs/061 FR-10~17: 하늘·환경광·반사).</summary>
    [CreateAssetMenu(menuName = "Road To PRO/Stadium Lighting Preset")]
    public sealed class StadiumLightingPreset : ScriptableObject
    {
        public Color sunColor = Color.white;
        public float sunIntensity = 1.2f;
        public Vector3 sunEuler = new Vector3(50f, -30f, 0f);
        public bool sunShadows = true;
        [Tooltip("하늘이 없을 때만 쓰는 3색 환경광 (기본 그라데이션 대체)")]
        public Color ambientSky = new Color(0.55f, 0.6f, 0.7f);
        public Color ambientEquator = new Color(0.45f, 0.47f, 0.45f);
        public Color ambientGround = new Color(0.2f, 0.22f, 0.2f);
        public Color background = new Color(0.55f, 0.68f, 0.85f);
        [Tooltip("조명탑·지붕 라인 발광 세기 (발광 머티리얼만, FR-42)")]
        public float emission = 0f;
        [Tooltip("나이트: 선수 발밑 블롭 그림자 4장 (FR-41)")]
        public bool blobShadows;

        [Header("061 하늘 (FR-10·11·15)")]
        [Tooltip("RoadToPro/Sky 머티리얼. 없으면 기본 그라데이션(단색 배경) + 경고 1회")]
        public Material skyMaterial;
        [Tooltip("RoadToPro/GrassShell 머티리얼 (061 근거리 셸 잔디 원본 — 참조가 있어야 빌드에 셰이더가 들어간다)")]
        public Material grassShellMaterial;
        public Color skyTop = new Color(0.30f, 0.50f, 0.85f);
        public Color skyHorizon = new Color(0.72f, 0.80f, 0.88f);
        public Color skyGround = new Color(0.32f, 0.34f, 0.30f);
        [Range(0f, 1f)] public float cloudCover = 0.45f;
        public Color cloudColor = new Color(0.95f, 0.96f, 0.98f);
        public Color cloudShade = new Color(0.62f, 0.66f, 0.72f);
        [Tooltip("구름 흐름 m/s (High만, FR-34 — Low·Medium은 정적)")]
        public float cloudSpeed = 3f;
        [Tooltip("나이트 지평선 도시광 (a = 세기)")]
        public Color cityGlow = new Color(1f, 0.62f, 0.35f, 0f);
        public Color backdropColor = new Color(0.42f, 0.47f, 0.52f);

        [Header("061 환경광 (FR-12·13)")]
        [Tooltip("하늘 색에서 뽑은 환경광 세기")]
        public float ambientIntensity = 1f;
        [Tooltip("선수 림 라이트 세기 (061 FR-42)")]
        [Range(0f, 1f)] public float rimStrength = 0.45f;
        [Tooltip("조명탑 채움광: 위 3색 환경광(ambientSky/Equator/Ground)을 이 배율로 더한다 (나이트 1, 낮 0)")]
        public float floodlightFill = 0f;
    }
}
