using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace RoadToPro.Tests.EditMode.Presentation.Players
{
    /// <summary>선수 에셋 임포트 검사 (specs/043 T01 스파이크, AC-03).</summary>
    public class PlayerImportTests
    {
        const string SkinTestPath = "Assets/_Project/Presentation/Players/Generated/skin-test.glb";

        [Test]
        public void Budgets()
        {
            const string gen = "Assets/_Project/Presentation/Players/Generated";
            var root = AssetDatabase.LoadAssetAtPath<GameObject>(gen + "/player.glb");
            Assert.IsNotNull(root);
            int[] max = { 10000, 3000, 1000 };
            // 057: 몸 LOD 3 + 헤어 조각 29종(삭발 제외) × 3 + 클로즈업 머리. 몸 + 가장 무거운 헤어 조각 ≤ LOD 예산(056 FR-60, 057 FR-40)
            var smrs = root.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            Assert.AreEqual(3 + 29 * 3 + 1, smrs.Length);
            foreach (var h in RoadToPro.Presentation.Players.PlayerLook.HairMeshes)
                if (h != null)
                    for (int l = 0; l < 3; l++)
                        Assert.IsTrue(smrs.Any(s => s.name == h + "_LOD" + l), h + "_LOD" + l + " 없음");
            var hairMax = new int[3];
            foreach (var smr in smrs)
                if (smr.name.StartsWith("Hair_"))
                {
                    int l = smr.name[smr.name.Length - 1] - '0';
                    hairMax[l] = Mathf.Max(hairMax[l], smr.sharedMesh.triangles.Length / 3);
                }
            foreach (var smr in smrs)
            {
                Assert.LessOrEqual(smr.sharedMaterials.Length, 3, smr.name);
                if (smr.name == "Head_Closeup")
                {
                    Assert.LessOrEqual(smr.sharedMesh.triangles.Length / 3, 6000, smr.name);
                    continue;
                }
                if (!smr.name.StartsWith("Player_LOD")) continue;
                int lod = smr.name[smr.name.Length - 1] - '0';
                Assert.LessOrEqual(smr.sharedMesh.triangles.Length / 3 + hairMax[lod], max[lod], smr.name);
                var names = Enumerable.Range(0, smr.sharedMesh.blendShapeCount).Select(i => smr.sharedMesh.GetBlendShapeName(i)).ToList();
                foreach (var k in new[] { "Thin", "Muscular", "Heavy", "HideHead" }) CollectionAssert.Contains(names, k, smr.name);
                Assert.GreaterOrEqual(names.Count(n => n.StartsWith("Face_")), 20, smr.name);
            }
            foreach (var smr in smrs)
                Assert.AreEqual(smrs[0].sharedMesh.bindposeCount, smr.sharedMesh.bindposeCount, smr.name + ": 뼈대 공유");
            foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { gen + "/Textures" }))
            {
                var t = AssetDatabase.LoadAssetAtPath<Texture2D>(AssetDatabase.GUIDToAssetPath(guid));
                Assert.LessOrEqual(Mathf.Max(t.width, t.height), 2048, t.name);
            }
            var clips = AssetDatabase.LoadAllAssetsAtPath(gen + "/player.glb").OfType<AnimationClip>().Count(c => !c.name.StartsWith("__preview"));
            Assert.GreaterOrEqual(clips, 75);
        }

        [Test]
        public void SkinNormals_MipFadeToFlat()
        {
            // 057 FR-22: 피부 노멀맵은 밉맵 페이드(먼 밉 = 평탄) — 원거리 깜빡임 방지
            foreach (var name in new[] { "skin_normal.png", "skin_closeup_normal.png" })
            {
                var ti = (TextureImporter)AssetImporter.GetAtPath("Assets/_Project/Presentation/Players/Generated/Textures/" + name);
                Assert.IsNotNull(ti, name);
                Assert.AreEqual(TextureImporterType.NormalMap, ti.textureType, name);
                Assert.IsTrue(ti.fadeout, name);
                Assert.Less(ti.mipmapFadeDistanceStart, ti.mipmapFadeDistanceEnd, name);
            }
        }

        [Test]
        public void SkinTest_ClipsBlendShapesSkin()
        {
            var assets = AssetDatabase.LoadAllAssetsAtPath(SkinTestPath);
            Assert.IsNotEmpty(assets, "임포트 실패: " + SkinTestPath);

            var clips = assets.OfType<AnimationClip>().Where(c => !c.name.StartsWith("__preview")).ToArray();
            CollectionAssert.AreEquivalent(new[] { "Bend", "Twist" }, clips.Select(c => c.name).ToArray(), "클립 이름");
            foreach (var c in clips)
            {
                Assert.IsFalse(c.legacy, c.name + ": Mecanim 클립이어야 한다");
                Assert.That(c.length, Is.EqualTo(1f).Within(0.05f), c.name + " 길이");
            }

            var root = AssetDatabase.LoadAssetAtPath<GameObject>(SkinTestPath);
            var smr = root.GetComponentInChildren<SkinnedMeshRenderer>(true);
            Assert.IsNotNull(smr, "SkinnedMeshRenderer 없음");
            Assert.AreEqual(2, smr.bones.Length, "본 수");
            Assert.AreEqual(1, smr.sharedMesh.blendShapeCount, "블렌드셰이프 수");
            Assert.AreEqual("Wide", smr.sharedMesh.GetBlendShapeName(0));

            // Bend 1 s 지점: Tip 본이 90° 꺾여 맨 위 정점이 옆으로 이동해야 한다.
            var inst = Object.Instantiate(root);
            try
            {
                var s = inst.GetComponentInChildren<SkinnedMeshRenderer>(true);
                var bend = clips.First(c => c.name == "Bend");
                bend.SampleAnimation(inst, 0f);
                var m0 = new Mesh(); s.BakeMesh(m0, true);
                bend.SampleAnimation(inst, bend.length);
                var m1 = new Mesh(); s.BakeMesh(m1, true);
                float top0 = m0.vertices.Max(v => v.y), top1 = m1.vertices.Max(v => v.y);
                Assert.That(top0, Is.EqualTo(2f).Within(0.02f), "구부리기 전 높이");
                Assert.That(top1, Is.LessThan(1.8f), "Tip 회전이 스킨에 반영");
            }
            finally { Object.DestroyImmediate(inst); }
        }
    }
}
