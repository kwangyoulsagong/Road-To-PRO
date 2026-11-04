using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using RoadToPro.Presentation.Players;
using RoadToPro.Presentation.Players.Logic;
using UnityEngine;

namespace RoadToPro.Tests.EditMode.Presentation.Players
{
    /// <summary>선수별 메시 굽기 (specs/056 plan, AC-02·05·11, FR-61).</summary>
    public class PlayerMeshBakerTests
    {
        static PlayerRig Rig(int id, out FaceShape face, out FaceData data)
        {
            data = FaceGeneratorTests.Data;
            face = FaceGenerator.Generate(data, id);
            var rig = PlayerRigTests.NewRig();
            rig.ApplyAppearance(AppearanceGenerator.Generate(new BodyInput(id)), face, data);
            return rig;
        }

        [Test]
        public void SameSeed_SameMeshHash()
        {
            var a = Rig(77, out _, out _);
            var b = Rig(77, out _, out _);
            var c = Rig(78, out _, out _);
            try
            {
                for (int l = 0; l < 3; l++)
                    Assert.AreEqual(PlayerMeshBaker.Hash(a.Lod(l).sharedMesh), PlayerMeshBaker.Hash(b.Lod(l).sharedMesh), "LOD" + l);
                Assert.AreNotEqual(PlayerMeshBaker.Hash(a.Lod(0).sharedMesh), PlayerMeshBaker.Hash(c.Lod(0).sharedMesh));
            }
            finally
            {
                Object.DestroyImmediate(a.gameObject);
                Object.DestroyImmediate(b.gameObject);
                Object.DestroyImmediate(c.gameObject);
            }
        }

        [Test]
        public void Height_Within1cm()
        {
            foreach (int id in new[] { 3, 40, 512, 9001 })
            {
                var rig = Rig(id, out _, out _);
                try
                {
                    var m = rig.Lod(0).sharedMesh;
                    // 머리카락을 뺀 몸 최고점 = 정수리. 루트 스케일을 곱하면 키
                    float top = 0f;
                    var v = m.vertices;
                    var body = new HashSet<int>(m.GetTriangles(0).Concat(m.GetTriangles(1)));
                    var uv = new List<Vector2>();
                    m.GetUVs(0, uv);
                    foreach (int i in body)
                        if (uv[i].x < 1f) top = Mathf.Max(top, v[i].y);
                    float cm = top * rig.transform.localScale.y * 100f;
                    Assert.AreEqual(rig.Appearance.HeightCm, cm, 1.0f, "id " + id);
                }
                finally { Object.DestroyImmediate(rig.gameObject); }
            }
        }

        [Test]
        public void Baked_OnlyHideHeadBlendShape_TwoSubmeshes()
        {
            var rig = Rig(5, out _, out _);
            try
            {
                for (int l = 0; l < 3; l++)
                {
                    var m = rig.Lod(l).sharedMesh;
                    Assert.AreEqual(1, m.blendShapeCount, "LOD" + l);
                    Assert.AreEqual(PlayerMeshBaker.HideHead, m.GetBlendShapeName(0));
                    Assert.AreEqual(2, m.subMeshCount);
                }
            }
            finally { Object.DestroyImmediate(rig.gameObject); }
        }

        [Test]
        public void HairStyle_AddsHairTriangles_BaldNone()
        {
            var a = AppearanceGenerator.Generate(new BodyInput(9));
            var afro = a.WithCombo(((a.SkinTone * AppearanceGenerator.Faces + a.Face) * AppearanceGenerator.HairStyles + 6) * AppearanceGenerator.HairColors);
            var bald = a.WithCombo(((a.SkinTone * AppearanceGenerator.Faces + a.Face) * AppearanceGenerator.HairStyles + 9) * AppearanceGenerator.HairColors);
            var r1 = PlayerRigTests.NewRig();
            var r2 = PlayerRigTests.NewRig();
            try
            {
                r1.ApplyAppearance(afro);
                r2.ApplyAppearance(bald);
                Assert.Greater(r1.Lod(0).sharedMesh.triangles.Length, r2.Lod(0).sharedMesh.triangles.Length + 300 * 3);
                Assert.LessOrEqual(r1.Lod(0).sharedMesh.triangles.Length / 3, 10_000, "FR-60 LOD0 ≤ 10k (헤어 포함)");
                Assert.LessOrEqual(r1.Lod(1).sharedMesh.triangles.Length / 3, 3_000);
                Assert.LessOrEqual(r1.Lod(2).sharedMesh.triangles.Length / 3, 1_000);
            }
            finally
            {
                Object.DestroyImmediate(r1.gameObject);
                Object.DestroyImmediate(r2.gameObject);
            }
        }

        [Test]
        public void Closeup_TogglesHeadRendererAndHideHead()
        {
            var rig = Rig(11, out _, out _);
            try
            {
                Assert.IsNotNull(rig.CloseupRenderer);
                Assert.IsFalse(rig.CloseupRenderer.enabled);
                Assert.LessOrEqual(rig.CloseupRenderer.sharedMesh.triangles.Length / 3, 6000 + 1100, "클로즈업 머리 ≤ 6k + 헤어 조각");
                rig.SetCloseup(true);
                Assert.IsTrue(rig.CloseupRenderer.enabled);
                Assert.AreEqual(1f, rig.Lod(0).GetBlendShapeWeight(0));
                rig.SetCloseup(false);
                Assert.AreEqual(0f, rig.Lod(0).GetBlendShapeWeight(0));
            }
            finally { Object.DestroyImmediate(rig.gameObject); }
        }

        [Test]
        public void BakeDuringPlayback_Counted()
        {
            var rig = PlayerRigTests.NewRig();
            PlayerMeshBaker.ResetDiagnostics();
            try
            {
                PlayerMeshBaker.PlaybackActive = true;
                UnityEngine.TestTools.LogAssert.ignoreFailingMessages = true;
                rig.ApplyAppearance(AppearanceGenerator.Generate(new BodyInput(1)));
                Assert.Greater(PlayerMeshBaker.BakesDuringPlayback, 0);
            }
            finally
            {
                PlayerMeshBaker.PlaybackActive = false;
                UnityEngine.TestTools.LogAssert.ignoreFailingMessages = false;
                Object.DestroyImmediate(rig.gameObject);
            }
        }
    }
}
