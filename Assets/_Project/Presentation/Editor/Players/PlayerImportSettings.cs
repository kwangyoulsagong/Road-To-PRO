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

        /// <summary>규칙이 바뀌면 올린다 — Unity가 이 후처리기를 거친 에셋을 다시 임포트한다 (062: 디테일 노멀·짜임 타일 규칙).</summary>
        public override uint GetVersion() => 3;   // 062 T36: AO 맵 1K

        void OnPreprocessTexture()
        {
            if (assetPath.StartsWith(GeneratedDir + "/Textures/"))
            {
                // 생성 텍스처는 마스크·명암·노말 데이터 → 선형, 최대 2K(FR-12), 밉맵(원거리 LOD)
                var t = (TextureImporter)assetImporter;
                string file = System.IO.Path.GetFileName(assetPath);
                // 056: skin*_normal은 탄젠트 노멀(kit_normal은 043 규칙 그대로 데이터 텍스처). 062: body/kit_detail_n도 탄젠트 노멀
                bool normal = (file.StartsWith("skin") && file.EndsWith("_normal.png")) || file == "body_detail_n.png" || file == "kit_detail_n.png";
                bool tile = file == "kit_normal.png" || file == "kit_weave.png";   // 반복 타일
                t.textureType = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
                t.sRGBTexture = false;
                t.mipmapEnabled = true;
                // 062 T36 FR-83·92: 몸·키트 AO는 저주파 → 1K (선수 텍스처 165.7 → 약 134 MB, 예산 160 MB)
                t.maxTextureSize = file == "body_detail_ao.png" || file == "kit_detail_ao.png" ? 1024 : 2048;
                t.wrapMode = tile ? UnityEngine.TextureWrapMode.Repeat : UnityEngine.TextureWrapMode.Clamp;
                t.textureCompression = TextureImporterCompression.Uncompressed;
                // 057 FR-22·062 FR-94: 디테일 노멀·짜임 높이는 먼 밉일수록 평탄하게(노멀 = 위, 높이 = 회색 0.5) — 방송 시점 원거리 깜빡임 방지.
                // 1K는 밉 2→5, 2K(클로즈업·몸/키트 디테일)는 3→6, 짜임 256 타일은 1→4. AO·영역·두께 맵은 값 데이터라 페이드하지 않는다
                bool weave = file == "kit_weave.png";
                t.fadeout = normal || weave;
                if (t.fadeout)
                {
                    bool big = assetPath.Contains("closeup") || file == "body_detail_n.png" || file == "kit_detail_n.png";
                    t.mipmapFadeDistanceStart = weave ? 1 : big ? 3 : 2;
                    t.mipmapFadeDistanceEnd = weave ? 4 : big ? 6 : 5;
                }
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
