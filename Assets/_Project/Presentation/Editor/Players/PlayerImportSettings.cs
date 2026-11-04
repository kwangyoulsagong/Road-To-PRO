using UnityEditor;

namespace RoadToPro.Presentation.Editor.Players
{
    /// <summary>
    /// 선수 에셋 임포트 규칙 (specs/043). 글리프 아틀라스는 CPU 합성(KitCompositor)이 읽으므로 읽기 가능 · 비압축 · 밉맵 없음 · 선형.
    /// </summary>
    public sealed class PlayerImportSettings : AssetPostprocessor
    {
        public const string PlayersDir = "Assets/_Project/Presentation/Players";
        public const string FontsDir = PlayersDir + "/Fonts";
        public const string GeneratedDir = PlayersDir + "/Generated";

        void OnPreprocessTexture()
        {
            if (assetPath.StartsWith(GeneratedDir + "/Textures/"))
            {
                // 생성 텍스처는 마스크·명암·노말 데이터 → 선형, 최대 2K(FR-12), 밉맵(원거리 LOD)
                var t = (TextureImporter)assetImporter;
                bool normal = System.IO.Path.GetFileName(assetPath).StartsWith("skin") && assetPath.EndsWith("_normal.png");
                // 056: skin*_normal은 탄젠트 노멀(kit_normal은 043 규칙 그대로 데이터 텍스처)
                t.textureType = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
                t.sRGBTexture = false;
                t.mipmapEnabled = true;
                t.maxTextureSize = 2048;
                t.wrapMode = assetPath.EndsWith("kit_normal.png") ? UnityEngine.TextureWrapMode.Repeat : UnityEngine.TextureWrapMode.Clamp;
                t.textureCompression = TextureImporterCompression.Uncompressed;
                return;
            }
            if (!assetPath.StartsWith(FontsDir + "/kit-glyphs")) return;
            var ti = (TextureImporter)assetImporter;
            ti.textureType = TextureImporterType.SingleChannel;
            ti.isReadable = true;
            ti.mipmapEnabled = false;
            ti.sRGBTexture = false;
            ti.textureCompression = TextureImporterCompression.Uncompressed;
            ti.npotScale = TextureImporterNPOTScale.None;
            ti.maxTextureSize = 2048;
            ti.alphaSource = TextureImporterAlphaSource.None;
            var s = ti.GetDefaultPlatformTextureSettings();
            s.format = TextureImporterFormat.R8;
            ti.SetPlatformTextureSettings(s);
        }
    }
}
