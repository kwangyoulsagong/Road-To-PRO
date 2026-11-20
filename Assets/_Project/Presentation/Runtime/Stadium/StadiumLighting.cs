using RoadToPro.Presentation.Rendering;
using RoadToPro.Presentation.Rendering.Logic;
using UnityEngine;
using UnityEngine.Rendering;

namespace RoadToPro.Presentation.Stadium
{
    /// <summary>
    /// 경기장 조명 (specs/044 FR-40~42, specs/061 FR-10~17). 방향광 1개 + 절차 하늘(스카이박스) + 하늘 색 환경광 + 발광 머티리얼 세기 + 나이트 블롭 그림자.
    /// 하늘 머티리얼이 없으면 기본 그라데이션(단색 배경 + 3색 환경광)과 경고 1회 (AC-03).
    /// </summary>
    public sealed class StadiumLighting : MonoBehaviour
    {
        public const string SkyShaderName = "RoadToPro/Sky";
        static readonly int IdEmission = Shader.PropertyToID("_EmissionColor");
        public static readonly int IdSunDir = Shader.PropertyToID("_SunDir");
        static readonly int IdSunColor = Shader.PropertyToID("_SunColor");
        static readonly int IdSkyTop = Shader.PropertyToID("_SkyTop");
        static readonly int IdSkyHorizon = Shader.PropertyToID("_SkyHorizon");
        static readonly int IdGround = Shader.PropertyToID("_GroundColor");
        static readonly int IdCloudCover = Shader.PropertyToID("_CloudCover");
        static readonly int IdCloudColor = Shader.PropertyToID("_CloudColor");
        static readonly int IdCloudShade = Shader.PropertyToID("_CloudShade");
        static readonly int IdCloudOffset = Shader.PropertyToID("_CloudOffset");
        static readonly int IdCityGlow = Shader.PropertyToID("_CityGlow");
        static readonly int IdBackdrop = Shader.PropertyToID("_BackdropColor");
        public static readonly int IdRim = Shader.PropertyToID("_RoadToProRim");

        [SerializeField] Light sun;
        [SerializeField] Material emissive;
        [SerializeField] Color emissiveColor = new Color(1f, 0.98f, 0.92f);
        [SerializeField] Camera[] cameras;
        [SerializeField] BlobShadow4[] blobShadows;

        Material _sky;
        Cubemap _reflection;
        bool _warnedSky;
        float _cloudSpeed;
        /// <summary>반사 큐브맵 한 변 (Medium·High). Low는 하늘 평균색 4 px (FR-34 "단순").</summary>
        const int ReflectionSize = 32, SimpleReflectionSize = 4;

        public StadiumLightingPreset Current { get; private set; }

        /// <summary>하늘 인스턴스 (없으면 null = 기본 그라데이션).</summary>
        public Material Sky => _sky;

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
            var preset = RenderQualityApplier.Current?.Id ?? PresetId.Low;
            Shader.SetGlobalFloat(IdRim, VisualFeatureTable.Get(preset, VisualFeature.Rim) == FeatureLevel.Off ? 0f : p.rimStrength);
            bool hasSky = ApplySky(p);
            // 061 FR-34: 구름 흐름은 High만, 반사는 Low 단순(하늘 평균색)·Medium·High 하늘 큐브맵
            _cloudSpeed = hasSky && VisualFeatureTable.Get(preset, VisualFeature.Sky) == FeatureLevel.Animated ? p.cloudSpeed : 0f;
            ApplyReflection(p, hasSky, VisualFeatureTable.Get(preset, VisualFeature.Reflection) == FeatureLevel.Simple);
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            if (hasSky)
            {
                SkyModel.Ambient(p.skyTop, p.skyHorizon, p.skyGround, p.ambientIntensity, out var s, out var e, out var g);
                RenderSettings.ambientSkyColor = s + p.ambientSky * p.floodlightFill;
                RenderSettings.ambientEquatorColor = e + p.ambientEquator * p.floodlightFill;
                RenderSettings.ambientGroundColor = g + p.ambientGround * p.floodlightFill;
            }
            else
            {
                RenderSettings.ambientSkyColor = p.ambientSky;
                RenderSettings.ambientEquatorColor = p.ambientEquator;
                RenderSettings.ambientGroundColor = p.ambientGround;
            }
            if (emissive != null)
            {
                emissive.EnableKeyword("_EMISSION");
                emissive.SetColor(IdEmission, emissiveColor * p.emission);
            }
            if (cameras != null)
                foreach (var c in cameras)
                    if (c != null)
                    {
                        c.clearFlags = hasSky ? CameraClearFlags.Skybox : CameraClearFlags.SolidColor;
                        c.backgroundColor = p.background;
                    }
            if (blobShadows != null)
                foreach (var b in blobShadows)
                    if (b != null) b.enabled = p.blobShadows;
        }

        bool ApplySky(StadiumLightingPreset p)
        {
            if (p.skyMaterial == null || p.skyMaterial.shader == null || !p.skyMaterial.shader.isSupported)
            {
                if (!_warnedSky)
                {
                    _warnedSky = true;
                    Debug.LogWarning($"[061] 하늘 머티리얼 없음·손상 ({p.name}): 기본 그라데이션 하늘을 쓴다");
                }
                if (_sky != null) Destroy(_sky);
                _sky = null;
                RenderSettings.skybox = null;
                return false;
            }
            if (_sky == null || _sky.shader != p.skyMaterial.shader)
            {
                if (_sky != null) Destroy(_sky);
                _sky = new Material(p.skyMaterial) { name = p.skyMaterial.name + " (instance)" };
            }
            _sky.SetColor(IdSkyTop, p.skyTop);
            _sky.SetColor(IdSkyHorizon, p.skyHorizon);
            _sky.SetColor(IdGround, p.skyGround);
            _sky.SetColor(IdCloudColor, p.cloudColor);
            _sky.SetColor(IdCloudShade, p.cloudShade);
            _sky.SetFloat(IdCloudCover, p.cloudCover);
            _sky.SetColor(IdCityGlow, p.cityGlow);
            _sky.SetColor(IdBackdrop, p.backdropColor);
            _sky.SetColor(IdSunColor, p.sunColor);
            var toSun = -(Quaternion.Euler(p.sunEuler) * Vector3.forward);
            _sky.SetVector(IdSunDir, new Vector4(toSun.x, toSun.y, toSun.z, 0f));
            _sky.SetVector(IdCloudOffset, Vector4.zero);
            RenderSettings.skybox = _sky;
            return true;
        }

        /// <summary>
        /// 반사 (FR-14): 하늘 그라데이션·태양을 CPU로 큐브맵에 굽는다 (GPU 캡처 없음 → 결정론·로딩 짧음). SSR 없음.
        /// 하늘이 없으면 기본 반사(유니티 기본)로 둔다.
        /// </summary>
        void ApplyReflection(StadiumLightingPreset p, bool hasSky, bool simple)
        {
            if (!hasSky)
            {
                RenderSettings.defaultReflectionMode = DefaultReflectionMode.Skybox;
                return;
            }
            int size = simple ? SimpleReflectionSize : ReflectionSize;
            if (_reflection == null || _reflection.width != size)
            {
                if (_reflection != null) Destroy(_reflection);
                _reflection = new Cubemap(size, TextureFormat.RGBAHalf, true) { name = "061 sky reflection", hideFlags = HideFlags.DontSave };
            }
            var toSun = -(Quaternion.Euler(p.sunEuler) * Vector3.forward);
            var avg = SkyModel.AverageSky(p.skyTop, p.skyHorizon, p.skyGround);
            var px = new Color[size * size];
            for (int face = 0; face < 6; face++)
            {
                for (int y = 0; y < size; y++)
                    for (int x = 0; x < size; x++)
                        px[y * size + x] = simple ? avg : SkyModel.SkyColor(FaceDir((CubemapFace)face, (x + 0.5f) / size * 2f - 1f, (y + 0.5f) / size * 2f - 1f),
                            p.skyTop, p.skyHorizon, p.skyGround, toSun, p.sunColor);
                _reflection.SetPixels(px, (CubemapFace)face);
            }
            _reflection.Apply(true, false);
            RenderSettings.defaultReflectionMode = DefaultReflectionMode.Custom;
            RenderSettings.customReflectionTexture = _reflection;
        }

        /// <summary>큐브맵 면 좌표 (u, v ∈ −1~1, v는 아래로) → 방향 (Unity 큐브맵 규약).</summary>
        static Vector3 FaceDir(CubemapFace f, float u, float v) => f switch
        {
            CubemapFace.PositiveX => new Vector3(1f, -v, -u),
            CubemapFace.NegativeX => new Vector3(-1f, -v, u),
            CubemapFace.PositiveY => new Vector3(u, 1f, v),
            CubemapFace.NegativeY => new Vector3(u, -1f, -v),
            CubemapFace.PositiveZ => new Vector3(u, -v, 1f),
            _ => new Vector3(-u, -v, -1f),
        };

        /// <summary>재생 시각 반영 (구름 흐름). 시뮬 시각만의 함수 — 정지 중엔 같은 값 (FR-16).</summary>
        public void SetSimTime(double simTime)
        {
            if (_sky == null || _cloudSpeed <= 0f) return;
            var o = SkyModel.CloudOffset(simTime, _cloudSpeed);
            _sky.SetVector(IdCloudOffset, new Vector4(o.x, o.y, 0f, 0f));
        }

        static void Destroy(Object o)
        {
            if (Application.isPlaying) Object.Destroy(o);
            else Object.DestroyImmediate(o);
        }

        void OnDestroy()
        {
            if (_reflection != null)
            {
                if (RenderSettings.customReflectionTexture == _reflection) RenderSettings.customReflectionTexture = null;
                Destroy(_reflection);
            }
            if (_sky != null)
            {
                if (RenderSettings.skybox == _sky) RenderSettings.skybox = null;
                Destroy(_sky);
            }
        }
    }
}
