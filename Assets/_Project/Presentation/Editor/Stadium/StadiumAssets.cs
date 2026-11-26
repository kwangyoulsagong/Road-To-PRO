using System.IO;
using RoadToPro.Presentation.Stadium;
using UnityEditor;
using UnityEngine;

namespace RoadToPro.Presentation.Editor.Stadium
{
    /// <summary>
    /// 경기장 Unity 쪽 에셋 생성 (specs/044 T14·T15): 피치 머티리얼, 블롭 그림자 텍스처·머티리얼, 낮·나이트 조명 프리셋.
    /// 에셋 YAML은 손으로 쓰지 않고 이 메서드로 만든다. 다시 돌리면 값만 덮어쓴다 (GUID 유지).
    /// 배치: Unity -batchmode -quit -executeMethod RoadToPro.Presentation.Editor.Stadium.StadiumAssets.Generate
    /// </summary>
    public static class StadiumAssets
    {
        public const string Dir = "Assets/_Project/Presentation/Stadium/Materials";
        public const string PitchMatPath = Dir + "/Pitch.mat";
        public const string BlobTexPath = Dir + "/BlobShadow.png";
        public const string BlobMatPath = Dir + "/BlobShadow.mat";
        public const string DayPath = Dir + "/Lighting_Day.asset";
        public const string NightPath = Dir + "/Lighting_Night.asset";
        /// <summary>specs/061 절차 하늘 머티리얼 (RoadToPro/Sky).</summary>
        public const string SkyMatPath = Dir + "/Stadium_Sky.mat";
        /// <summary>specs/061 근거리 셸 잔디 머티리얼 (조명 프리셋이 참조해 빌드에 셰이더가 들어가게).</summary>
        public const string GrassShellMatPath = Dir + "/Stadium_GrassShell.mat";
        const string TexDir = "Assets/_Project/Presentation/Stadium/Textures";
        const int BlobSize = 64;

        [MenuItem("Road To PRO/경기장/에셋 다시 만들기")]
        public static void Generate()
        {
            if (!AssetDatabase.IsValidFolder(Dir)) AssetDatabase.CreateFolder("Assets/_Project/Presentation/Stadium", "Materials");
            ConfigureTextures();
            PitchMaterial();
            BlobMaterial();
            var sky = SkyMaterial();
            var grass = GrassShellMaterial();
            Presets(sky, grass);
            RetuneStructureMaterials();
            AssetDatabase.SaveAssets();
        }

        static void ConfigureTextures()
        {
            foreach (var (file, normal) in new[] { ("Grass004_Color.jpg", false), ("Grass004_NormalGL.jpg", true) })
            {
                var path = TexDir + "/" + file;
                if (!(AssetImporter.GetAtPath(path) is TextureImporter ti)) throw new System.InvalidOperationException("텍스처 없음: " + path);
                ti.textureType = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
                ti.sRGBTexture = !normal;
                ti.maxTextureSize = 1024;
                ti.mipmapEnabled = true;
                ti.wrapMode = TextureWrapMode.Repeat;
                ti.anisoLevel = 2;
                ti.SaveAndReimport();
            }
        }

        static T LoadOrCreate<T>(string path, System.Func<T> make) where T : Object
        {
            var a = AssetDatabase.LoadAssetAtPath<T>(path);
            if (a != null) return a;
            a = make();
            AssetDatabase.CreateAsset(a, path);
            return a;
        }

        public static Material PitchMaterial()
        {
            var shader = Shader.Find("RoadToPro/Pitch");
            if (shader == null) throw new System.InvalidOperationException("셰이더 RoadToPro/Pitch 없음");
            var m = LoadOrCreate(PitchMatPath, () => new Material(shader));
            m.shader = shader;
            m.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(TexDir + "/Grass004_Color.jpg"));
            m.SetTexture("_NormalMap", AssetDatabase.LoadAssetAtPath<Texture2D>(TexDir + "/Grass004_NormalGL.jpg"));
            m.SetColor("_LineColor", new Color(0.85f, 0.85f, 0.83f, 1f)); // specs/061 FR-07: 라인 페인트 180~225 (217), 순백 금지
            EditorUtility.SetDirty(m);
            return m;
        }

        public static Material BlobMaterial()
        {
            // 부드러운 타원 그림자 (가운데 진하고 가장자리 0). 결정론: 수식으로 만든다.
            var tex = new Texture2D(BlobSize, BlobSize, TextureFormat.RGBA32, false);
            for (int y = 0; y < BlobSize; y++)
                for (int x = 0; x < BlobSize; x++)
                {
                    float u = (x + 0.5f) / BlobSize * 2f - 1f, v = (y + 0.5f) / BlobSize * 2f - 1f;
                    float a = Mathf.Clamp01(1f - Mathf.Sqrt(u * u + v * v));
                    tex.SetPixel(x, y, new Color(0f, 0f, 0f, a * a * 0.55f));
                }
            File.WriteAllBytes(BlobTexPath, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(BlobTexPath);
            if (AssetImporter.GetAtPath(BlobTexPath) is TextureImporter ti)
            {
                ti.alphaIsTransparency = true;
                ti.mipmapEnabled = false;
                ti.wrapMode = TextureWrapMode.Clamp;
                ti.SaveAndReimport();
            }
            var shader = Shader.Find("Universal Render Pipeline/Unlit");
            var m = LoadOrCreate(BlobMatPath, () => new Material(shader));
            m.shader = shader;
            m.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(BlobTexPath));
            m.SetColor("_BaseColor", Color.white);
            m.SetFloat("_Surface", 1f);   // Transparent
            m.SetFloat("_Blend", 0f);     // Alpha
            m.SetFloat("_ZWrite", 0f);
            m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            m.SetOverrideTag("RenderType", "Transparent");
            m.enableInstancing = true;
            EditorUtility.SetDirty(m);
            return m;
        }

        /// <summary>
        /// glb(glTFast) 머티리얼 → 같은 색의 URP Lit 머티리얼 (FR-22: 표준 PBR은 URP Lit로 매핑). 이름별로 하나, 다시 돌리면 값만 갱신.
        /// glTFast 기본 셰이더 그래프보다 가볍고, 발광은 URP Lit `_EmissionColor`로 조명 프리셋이 조절한다.
        /// </summary>
        public static Material LitFor(Material src)
        {
            var name = src.name.Replace(" (Instance)", "");
            var path = $"{Dir}/Stadium_{name}.mat";
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            var m = LoadOrCreate(path, () => new Material(shader));
            m.shader = shader;
            var col = src.HasProperty("baseColorFactor") ? src.GetColor("baseColorFactor") : src.HasProperty("_BaseColor") ? src.GetColor("_BaseColor") : src.color;
            m.SetColor("_BaseColor", col);
            m.SetFloat("_Smoothness", name.StartsWith("Metal") ? 0.5f : 0.2f);
            m.SetFloat("_Metallic", 0f);
            var transparent = col.a < 0.99f;
            m.SetFloat("_Surface", transparent ? 1f : 0f);
            m.SetFloat("_Blend", 0f);
            m.SetFloat("_ZWrite", transparent ? 0f : 1f);
            m.SetFloat("_SrcBlend", (float)(transparent ? UnityEngine.Rendering.BlendMode.SrcAlpha : UnityEngine.Rendering.BlendMode.One));
            m.SetFloat("_DstBlend", (float)(transparent ? UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha : UnityEngine.Rendering.BlendMode.Zero));
            if (transparent)
            {
                m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
                m.SetOverrideTag("RenderType", "Transparent");
            }
            else
            {
                m.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
                m.renderQueue = -1;
                m.SetOverrideTag("RenderType", "Opaque");
            }
            if (name.StartsWith("LightEmit"))
            {
                m.EnableKeyword("_EMISSION");
                m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
                m.SetColor("_EmissionColor", Color.black);
            }
            m.enableInstancing = true;
            ApplySurfaceRules(m, name);
            EditorUtility.SetDirty(m);
            return m;
        }

        public const string SurfaceShaderName = "RoadToPro/StadiumSurface";
        /// <summary>specs/059 골망 셰이더 (육각 망 해석 렌더).</summary>
        public const string NetShaderName = "RoadToPro/GoalNet";

        /// <summary>
        /// specs/059 FR-01·10~15: 그물 머티리얼 = GoalNet 셰이더, 셀·줄·페이드 값은 `Data/tuning/goalnet.json` 기본값(런타임·생성기와 같은 원본).
        /// 색은 ≤ 240 기준(061 FR-07) 225.
        /// </summary>
        static void ApplyNet(Material m)
        {
            var shader = Shader.Find(NetShaderName);
            if (shader == null) throw new System.InvalidOperationException("셰이더 " + NetShaderName + " 없음");
            var t = RoadToPro.Core.Tuning.TuningBundle.Defaults(RoadToPro.Core.Tuning.TuningLoader.LoadDefinitionsFile(Path.Combine("Data", "tuning", "goalnet.json")));
            m.shader = shader;
            m.SetColor("_BaseColor", new Color(0.88f, 0.88f, 0.88f, 1f));
            m.SetFloat("_CellSize", t.Get("net.cellSize"));
            m.SetFloat("_Strand", t.Get("net.strand"));
            m.SetFloat("_FadePixels", t.Get("net.fadePixels"));
            m.SetFloat("_Opacity", 1f);
            m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            m.enableInstancing = true;
        }

        /// <summary>
        /// specs/061 FR-07 재질 값 기준 (sRGB 0~255 알베도): 콘크리트 100~180, 좌석 밝기 40~200·채도 ≤ 0.75, 공통 15~240.
        /// 불투명·비발광 구조물은 StadiumSurface(정점 AO, FR-30)로 바꾼다. 그물(059)·발광(조명 프리셋이 세기 조절)은 URP Lit 유지.
        /// glb 인자(좌석 색 등)가 범위를 넘어도 여기서 범위 안으로 접는다.
        /// </summary>
        public static void ApplySurfaceRules(Material m, string name)
        {
            var c = m.GetColor("_BaseColor");
            if (name.StartsWith("Net"))
            {
                ApplyNet(m);
                return;
            }
            if (name.StartsWith("LightEmit")) return;
            float smooth = 0.2f, spec = 0.04f;
            if (name.StartsWith("Concrete")) c = Gray(0.59f);          // 150
            else if (name.StartsWith("Ground")) { c = new Color(0.36f, 0.39f, 0.33f); smooth = 0.05f; } // 경기장 밖 바닥: 회색 대신 흙·잔디 섞인 색 (FR-15)
            else if (name.StartsWith("Roof")) { c = Gray(0.80f); smooth = 0.35f; spec = 0.05f; }    // 도장 금속 지붕 204
            else if (name.StartsWith("Metal")) { c = Gray(0.74f); smooth = 0.45f; spec = 0.06f; }   // 금속: 콘크리트보다 정반사 큼
            else if (name.StartsWith("Seat")) { c = ClampSeat(c); smooth = 0.4f; spec = 0.045f; }  // 좌석 플라스틱: 약한 광택
            else if (name.StartsWith("Board")) { smooth = 0.35f; }
            else if (name.StartsWith("Bench")) { smooth = 0.3f; }
            else if (name.StartsWith("Tunnel")) { smooth = 0.1f; }
            c = ClampAlbedo(c);
            var shader = Shader.Find(SurfaceShaderName);
            if (shader == null) throw new System.InvalidOperationException("셰이더 " + SurfaceShaderName + " 없음");
            m.shader = shader;
            m.SetColor("_BaseColor", new Color(c.r, c.g, c.b, 1f));
            m.SetFloat("_Smoothness", smooth);
            m.SetFloat("_Specular", spec);
            m.SetFloat("_AOStrength", 1f);
            m.renderQueue = -1;
            m.enableInstancing = true;
        }

        static Color Gray(float v) => new Color(v, v, v, 1f);

        /// <summary>좌석: 가장 밝은 채널 ≤ 200/255, 채도(HSV) ≤ 0.75, 어두운 쪽 ≥ 40/255.</summary>
        public static Color ClampSeat(Color c)
        {
            Color.RGBToHSV(c, out float h, out float sat, out float v);
            sat = Mathf.Min(sat, 0.75f);
            v = Mathf.Clamp(v, 40f / 255f, 200f / 255f);
            var o = Color.HSVToRGB(h, sat, v);
            o.a = 1f;
            return o;
        }

        /// <summary>공통 15~240 (FR-07).</summary>
        static Color ClampAlbedo(Color c)
        {
            const float lo = 15f / 255f, hi = 240f / 255f;
            return new Color(Mathf.Clamp(c.r, lo, hi), Mathf.Clamp(c.g, lo, hi), Mathf.Clamp(c.b, lo, hi), c.a);
        }

        /// <summary>이미 만들어진 Stadium_*.mat에 061 재질 규칙을 다시 적용 (생성 순서와 무관하게 값 수렴).</summary>
        static void RetuneStructureMaterials()
        {
            foreach (var guid in AssetDatabase.FindAssets("Stadium_ t:Material", new[] { Dir }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var m = AssetDatabase.LoadAssetAtPath<Material>(path);
                var name = Path.GetFileNameWithoutExtension(path).Substring("Stadium_".Length);
                if (name == "Sky" || name == "GrassShell") continue;
                ApplySurfaceRules(m, name);
                EditorUtility.SetDirty(m);
            }
        }

        /// <summary>specs/061 FR-10: 절차 하늘 머티리얼 (시간대 값은 조명 프리셋이 런타임 인스턴스에 넣는다).</summary>
        static Material SkyMaterial()
        {
            var shader = Shader.Find("RoadToPro/Sky");
            if (shader == null) throw new System.InvalidOperationException("셰이더 RoadToPro/Sky 없음");
            var m = LoadOrCreate(SkyMatPath, () => new Material(shader));
            m.shader = shader;
            EditorUtility.SetDirty(m);
            return m;
        }

        static Material GrassShellMaterial()
        {
            var shader = Shader.Find("RoadToPro/GrassShell");
            if (shader == null) throw new System.InvalidOperationException("셰이더 RoadToPro/GrassShell 없음");
            var m = LoadOrCreate(GrassShellMatPath, () => new Material(shader));
            m.shader = shader;
            m.enableInstancing = true;
            EditorUtility.SetDirty(m);
            return m;
        }

        static void Presets(Material sky, Material grass)
        {
            var day = LoadOrCreate(DayPath, ScriptableObject.CreateInstance<StadiumLightingPreset>);
            day.sunColor = new Color(1f, 0.96f, 0.9f);
            day.sunIntensity = 1.3f;
            day.sunEuler = new Vector3(52f, -35f, 0f);
            day.sunShadows = true;
            day.ambientSky = new Color(0.58f, 0.66f, 0.78f);
            day.ambientEquator = new Color(0.45f, 0.48f, 0.46f);
            day.ambientGround = new Color(0.22f, 0.24f, 0.21f);
            day.background = new Color(0.55f, 0.68f, 0.85f);
            day.emission = 0f;
            day.blobShadows = false;
            // 061 하늘·환경광 [제안] (FR-10~12): 맑은 낮, 구름 조금, 지평선은 옅은 대기색
            day.skyMaterial = sky;
            day.grassShellMaterial = grass;
            day.skyTop = new Color(0.20f, 0.40f, 0.78f);
            day.skyHorizon = new Color(0.62f, 0.74f, 0.88f);
            day.skyGround = new Color(0.30f, 0.33f, 0.28f);
            day.cloudCover = 0.5f;
            day.cloudColor = new Color(0.96f, 0.96f, 0.97f);
            day.cloudShade = new Color(0.64f, 0.68f, 0.74f);
            day.cloudSpeed = 3f;
            day.cityGlow = new Color(1f, 0.62f, 0.35f, 0f);
            day.backdropColor = new Color(0.40f, 0.46f, 0.50f);
            day.ambientIntensity = 1.6f; // T14: 그림자 안이 검게 뭉개지지 않게 (FR-12) 1.3, T19: S1·S3 L* 41~44 → 1.5
            day.floodlightFill = 0f;
            day.rimStrength = 0.45f;
            EditorUtility.SetDirty(day);

            var night = LoadOrCreate(NightPath, ScriptableObject.CreateInstance<StadiumLightingPreset>);
            night.sunColor = new Color(0.92f, 0.95f, 1f);
            night.sunIntensity = 0.85f;
            night.sunEuler = new Vector3(80f, 0f, 0f);  // 대표광: 거의 머리 위 (실시간 그림자는 블롭이 대신)
            night.sunShadows = false;
            night.ambientSky = new Color(0.30f, 0.33f, 0.42f);
            night.ambientEquator = new Color(0.26f, 0.28f, 0.30f);
            night.ambientGround = new Color(0.10f, 0.11f, 0.10f);
            night.background = new Color(0.03f, 0.04f, 0.08f);
            night.emission = 4f;
            night.blobShadows = true;
            // 061 나이트 하늘 [제안] (FR-10): 완전한 검정 아님 + 지평선 도시광, 구름은 옅고 어둡게
            night.skyMaterial = sky;
            night.grassShellMaterial = grass;
            night.skyTop = new Color(0.02f, 0.03f, 0.07f);
            night.skyHorizon = new Color(0.09f, 0.10f, 0.15f);
            night.skyGround = new Color(0.04f, 0.045f, 0.05f);
            night.cloudCover = 0.30f;
            night.cloudColor = new Color(0.16f, 0.16f, 0.20f);
            night.cloudShade = new Color(0.06f, 0.07f, 0.10f);
            night.cloudSpeed = 3f;
            night.cityGlow = new Color(1f, 0.60f, 0.32f, 0.30f);
            night.backdropColor = new Color(0.05f, 0.06f, 0.08f);
            night.ambientIntensity = 1f;
            night.floodlightFill = 1.8f; // T14: 나이트 L* 24~28 → 1.5, T19: S5 30 → 1.8
            night.rimStrength = 0.55f; // 조명탑 역광 // 하늘은 어두우므로 경기장 환경광은 조명탑 채움광(기존 3색)이 맡는다
            EditorUtility.SetDirty(night);
        }
    }
}
