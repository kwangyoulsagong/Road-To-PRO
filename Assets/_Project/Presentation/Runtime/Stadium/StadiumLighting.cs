using UnityEngine;

namespace RoadToPro.Presentation.Stadium
{
    /// <summary>
    /// 경기장 조명 (specs/044 FR-40~42). 방향광 1개 + 환경광 + 발광 머티리얼 세기 + 나이트 블롭 그림자 켜기.
    /// </summary>
    public sealed class StadiumLighting : MonoBehaviour
    {
        static readonly int IdEmission = Shader.PropertyToID("_EmissionColor");

        [SerializeField] Light sun;
        [SerializeField] Material emissive;
        [SerializeField] Color emissiveColor = new Color(1f, 0.98f, 0.92f);
        [SerializeField] Camera[] cameras;
        [SerializeField] BlobShadow4[] blobShadows;

        public StadiumLightingPreset Current { get; private set; }

        public void Configure(Light sunLight, Material emissiveMaterial, Camera[] cams, BlobShadow4[] blobs)
        {
            sun = sunLight;
            emissive = emissiveMaterial;
            cameras = cams;
            blobShadows = blobs;
        }

        public void Apply(StadiumLightingPreset p)
        {
            Current = p;
            if (sun != null)
            {
                sun.type = LightType.Directional;
                sun.color = p.sunColor;
                sun.intensity = p.sunIntensity;
                sun.shadows = p.sunShadows ? LightShadows.Soft : LightShadows.None;
                sun.transform.rotation = Quaternion.Euler(p.sunEuler);
            }
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = p.ambientSky;
            RenderSettings.ambientEquatorColor = p.ambientEquator;
            RenderSettings.ambientGroundColor = p.ambientGround;
            if (emissive != null)
            {
                emissive.EnableKeyword("_EMISSION");
                emissive.SetColor(IdEmission, emissiveColor * p.emission);
            }
            if (cameras != null)
                foreach (var c in cameras)
                    if (c != null)
                    {
                        c.clearFlags = CameraClearFlags.SolidColor;
                        c.backgroundColor = p.background;
                    }
            if (blobShadows != null)
                foreach (var b in blobShadows)
                    if (b != null) b.enabled = p.blobShadows;
        }
    }
}
