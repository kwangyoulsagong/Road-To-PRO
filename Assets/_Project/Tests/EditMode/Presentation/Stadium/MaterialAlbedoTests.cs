using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace RoadToPro.Tests.EditMode.Presentation.Stadium
{
    /// <summary>
    /// specs/061 FR-07·30, AC-03·06: 표면별 재질 값 범위 (sRGB 0~255 알베도). 알베도 확인 패스 대신 머티리얼 데이터에서 잰다
    /// (기본 색 × 텍스처 평균). 선수 재질 행은 062가 판정.
    /// </summary>
    public class MaterialAlbedoTests
    {
        const string Dir = "Assets/_Project/Presentation/Stadium/Materials";

        static Color Srgb(Material m) => m.GetColor("_BaseColor");

        static IEnumerable<(string name, Material m)> Structures()
        {
            foreach (var guid in AssetDatabase.FindAssets("Stadium_ t:Material", new[] { Dir }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var name = Path.GetFileNameWithoutExtension(path).Substring("Stadium_".Length);
                if (name == "Sky" || name == "LightEmit" || name == "GrassShell") continue; // 하늘·발광 램프는 알베도 표면이 아님
                yield return (name, AssetDatabase.LoadAssetAtPath<Material>(path));
            }
        }

        [Test]
        public void SurfacesWithinSpecRanges()
        {
            int n = 0;
            foreach (var (name, m) in Structures())
            {
                var c = Srgb(m) * 255f;
                float max = Mathf.Max(c.r, Mathf.Max(c.g, c.b)), min = Mathf.Min(c.r, Mathf.Min(c.g, c.b));
                if (name.StartsWith("Concrete"))
                    Assert.That(max, Is.InRange(100f, 180f), "콘크리트 100~180");
                if (name.StartsWith("Seat"))
                {
                    Color.RGBToHSV(Srgb(m), out _, out float sat, out _);
                    Assert.That(max, Is.InRange(40f, 200f), $"{name} 좌석 밝기 40~200");
                    Assert.LessOrEqual(sat, 0.75f + 1e-3f, $"{name} 좌석 채도 ≤ 0.75");
                }
                n++;
            }
            Assert.GreaterOrEqual(n, 8, "경기장 머티리얼 수");

            // 라인 페인트 180~225 (순백 금지)
            var pitch = AssetDatabase.LoadAssetAtPath<Material>(Dir + "/Pitch.mat");
            var line = pitch.GetColor("_LineColor") * 255f;
            Assert.That(Mathf.Max(line.r, Mathf.Max(line.g, line.b)), Is.InRange(180f, 225f), "라인 페인트");

            // 잔디 45~110: 텍스처 평균 × 색조
            var grass = MeanSrgb("Assets/_Project/Presentation/Stadium/Textures/Grass004_Color.jpg");
            var tint = pitch.GetColor("_Tint");
            float g = 0.2126f * grass.r * tint.r + 0.7152f * grass.g * tint.g + 0.0722f * grass.b * tint.b;
            Assert.That(g * 255f, Is.InRange(45f, 110f), "잔디 알베도 밝기");
        }

        [Test]
        public void NoAlbedoOutside15to240()
        {
            foreach (var (name, m) in Structures())
            {
                var c = Srgb(m) * 255f;
                foreach (var v in new[] { c.r, c.g, c.b })
                    Assert.That(v, Is.InRange(15f - 0.5f, 240f + 0.5f), $"{name} 알베도 {c}");
            }
        }

        [Test]
        public void StructuresUseBakedAoShader()
        {
            foreach (var (name, m) in Structures())
            {
                if (name.StartsWith("Net")) continue; // 그물은 059
                Assert.AreEqual("RoadToPro/StadiumSurface", m.shader.name, $"{name}: 정점 AO(FR-30)를 쓰는 구조물 셰이더");
            }
        }

        static Color MeanSrgb(string path)
        {
            var tex = new Texture2D(2, 2);
            Assert.IsTrue(tex.LoadImage(File.ReadAllBytes(path)), path);
            var px = tex.GetPixels32();
            double r = 0, gg = 0, b = 0;
            foreach (var p in px)
            {
                r += p.r;
                gg += p.g;
                b += p.b;
            }
            Object.DestroyImmediate(tex);
            return new Color((float)(r / px.Length / 255d), (float)(gg / px.Length / 255d), (float)(b / px.Length / 255d));
        }
    }

    /// <summary>specs/061 AC-03: 새 외부 텍스처·HDRI는 전부 Art/LICENSES.csv에 CC0로 (061은 절차 생성이라 새 항목 0이 기대값).</summary>
    public class VisualLicenseTests
    {
        [Test]
        public void NewTextures_AllInLicensesCsv()
        {
            var csv = File.ReadAllText("Art/LICENSES.csv");
            foreach (var dir in new[] { "Assets/_Project/Presentation/Stadium/Textures", "Assets/_Project/Presentation/Stadium/Materials" })
                foreach (var f in Directory.GetFiles(dir))
                {
                    var ext = Path.GetExtension(f).ToLowerInvariant();
                    if (ext != ".jpg" && ext != ".png" && ext != ".exr" && ext != ".hdr") continue;
                    var file = Path.GetFileName(f);
                    if (file == "BlobShadow.png") continue; // 수식으로 생성 (StadiumAssets.BlobMaterial)
                    // CSV는 묶음 표기(Grass004_{Color,NormalGL}.jpg)를 쓰므로 에셋 이름(첫 '_' 앞)과 폴더로 대조
                    var stem = Path.GetFileNameWithoutExtension(file).Split('_')[0];
                    StringAssert.Contains(stem, csv, $"{file}이 LICENSES.csv에 없음");
                    StringAssert.Contains(dir, csv, $"{file}의 폴더가 LICENSES.csv에 없음");
                }
            StringAssert.Contains("CC0", csv);
        }
    }
}
