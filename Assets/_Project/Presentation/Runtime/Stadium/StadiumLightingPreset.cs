using UnityEngine;

namespace RoadToPro.Presentation.Stadium
{
    /// <summary>조명 프리셋 하나 (specs/044 FR-40: 낮·나이트 2종 확정).</summary>
    [CreateAssetMenu(menuName = "Road To PRO/Stadium Lighting Preset")]
    public sealed class StadiumLightingPreset : ScriptableObject
    {
        public Color sunColor = Color.white;
        public float sunIntensity = 1.2f;
        public Vector3 sunEuler = new Vector3(50f, -30f, 0f);
        public bool sunShadows = true;
        public Color ambientSky = new Color(0.55f, 0.6f, 0.7f);
        public Color ambientEquator = new Color(0.45f, 0.47f, 0.45f);
        public Color ambientGround = new Color(0.2f, 0.22f, 0.2f);
        public Color background = new Color(0.55f, 0.68f, 0.85f);
        [Tooltip("조명탑·지붕 라인 발광 세기 (발광 머티리얼만, FR-42)")]
        public float emission = 0f;
        [Tooltip("나이트: 선수 발밑 블롭 그림자 4장 (FR-41)")]
        public bool blobShadows;
    }
}
