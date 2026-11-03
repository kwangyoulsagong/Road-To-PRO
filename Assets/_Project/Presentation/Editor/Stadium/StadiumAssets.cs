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
        const string TexDir = "Assets/_Project/Presentation/Stadium/Textures";
        const int BlobSize = 64;

        [MenuItem("Road To PRO/경기장/에셋 다시 만들기")]
        public static void Generate()
        {
            if (!AssetDatabase.IsValidFolder(Dir)) AssetDatabase.CreateFolder("Assets/_Project/Presentation/Stadium", "Materials");
            ConfigureTextures();
            PitchMaterial();
            BlobMaterial();
            Presets();
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
            EditorUtility.SetDirty(m);
            return m;
        }

        static void Presets()
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
            EditorUtility.SetDirty(day);

            var night = LoadOrCreate(NightPath, ScriptableObject.CreateInstance<StadiumLightingPreset>);
            night.sunColor = new Color(0.92f, 0.95f, 1f);
            night.sunIntensity = 0.55f;
            night.sunEuler = new Vector3(80f, 0f, 0f);  // 대표광: 거의 머리 위 (실시간 그림자는 블롭이 대신)
            night.sunShadows = false;
            night.ambientSky = new Color(0.30f, 0.33f, 0.42f);
            night.ambientEquator = new Color(0.26f, 0.28f, 0.30f);
            night.ambientGround = new Color(0.10f, 0.11f, 0.10f);
            night.background = new Color(0.03f, 0.04f, 0.08f);
            night.emission = 4f;
            night.blobShadows = true;
            EditorUtility.SetDirty(night);
        }
    }
}
